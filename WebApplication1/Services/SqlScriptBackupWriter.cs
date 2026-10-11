using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApplication1.Data;

namespace WebApplication1.Services;

// Last-resort backup for hosted SQL Servers that allow neither BACKUP DATABASE nor a DacFx export
// (which needs VIEW DEFINITION): a single .sql script that rebuilds the database. It needs nothing
// but SELECT on the app's own tables —
//   1. the schema, generated from the app's EF model (the live database is migrated to it at every
//      start), plus the __EFMigrationsHistory table — so the app sees nothing pending on restore;
//   2. every table's rows (the migrations history included) as INSERTs, keeping identity values,
//      with foreign keys switched off while loading and re-validated at the end.
// The schema comes from the model rather than replaying every migration because a migration that
// adds a column and back-fills it in raw SQL can't run as one script batch; the migrations' raw SQL
// is data-only, and the data comes from the live tables anyway.
// rowversion and computed columns are skipped (SQL Server generates them). Like a .bacpac it is not
// a point-in-time snapshot. Restore: create an empty database and run the script against it.
public static class SqlScriptBackupWriter
{
    private const int RowsPerInsert = 200; // SQL Server allows at most 1000 rows per VALUES list

    public static async Task WriteAsync(ApplicationDbContext db, string path, CancellationToken cancellationToken = default)
    {
        var historyRepository = db.GetService<IHistoryRepository>();
        var schema = historyRepository.GetCreateScript() + Environment.NewLine + "GO" + Environment.NewLine
                     + db.Database.GenerateCreateScript();

        var tables = db.Model.GetEntityTypes()
            .Where(t => t.GetTableName() is not null && t.GetViewName() is null)
            // No schema in the model means the app uses unqualified names, which SQL Server resolves
            // to the login's default schema — on hosted servers often the login's own schema, not dbo.
            // So those stay unqualified here too, both when reading and in the script.
            .Select(t => (Schema: t.GetSchema(), Name: t.GetTableName()!))
            .Append((Schema: (string?)null, Name: HistoryRepository.DefaultTableName))
            .Distinct()
            .OrderBy(t => t.Schema ?? string.Empty, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync($"""
            -- Database backup script: {db.Database.GetDbConnection().Database}, created {DateTime.Now:yyyy-MM-dd HH:mm:ss}.
            -- Restore: create a new EMPTY database, then run this whole script against it, e.g.
            --   sqlcmd -S <server> -d <NewDatabase> -i "<this file>"
            -- (or open it in SQL Server Management Studio with the new database selected and Execute).
            -- Filtered indexes need these session options; sqlcmd doesn't turn QUOTED_IDENTIFIER on by default.
            SET NOCOUNT ON;
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET NUMERIC_ROUNDABORT OFF;
            GO
            """);
        await writer.WriteLineAsync(schema);
        await writer.WriteLineAsync("GO");

        // Migrations may seed rows; the data below replaces them. Constraints are off while loading
        // so tables can be filled in any order.
        foreach (var (schemaName, name) in tables)
        {
            await writer.WriteLineAsync($"ALTER TABLE {Quote(schemaName, name)} NOCHECK CONSTRAINT ALL;");
        }
        foreach (var (schemaName, name) in tables)
        {
            await writer.WriteLineAsync($"DELETE FROM {Quote(schemaName, name)};");
        }
        await writer.WriteLineAsync("GO");

        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        foreach (var (schemaName, name) in tables)
        {
            await WriteTableAsync(connection, writer, schemaName, name, cancellationToken);
        }

        foreach (var (schemaName, name) in tables)
        {
            await writer.WriteLineAsync($"ALTER TABLE {Quote(schemaName, name)} WITH CHECK CHECK CONSTRAINT ALL;");
        }
        await writer.WriteLineAsync("GO");
    }

    private static async Task WriteTableAsync(SqlConnection connection, StreamWriter writer, string? schemaName, string name, CancellationToken cancellationToken)
    {
        var table = Quote(schemaName, name);
        await using var command = new SqlCommand($"SELECT * FROM {table}", connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = reader.GetColumnSchema()
            .Select((c, ordinal) => (Column: c, Ordinal: ordinal))
            .Where(c => c.Column.IsExpression != true
                        && !string.Equals(c.Column.DataTypeName, "timestamp", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(c.Column.DataTypeName, "rowversion", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var hasIdentity = columns.Any(c => c.Column.IsIdentity == true);
        var insertHead = $"INSERT INTO {table} ({string.Join(", ", columns.Select(c => Quote(c.Column.ColumnName)))}) VALUES";

        await writer.WriteLineAsync($"-- {table}");
        if (hasIdentity) await writer.WriteLineAsync($"SET IDENTITY_INSERT {table} ON;");

        var rowsInStatement = 0;
        var row = new StringBuilder();
        while (await reader.ReadAsync(cancellationToken))
        {
            row.Clear();
            row.Append(rowsInStatement == 0 ? insertHead + Environment.NewLine + "(" : "," + Environment.NewLine + "(");
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) row.Append(", ");
                var (column, ordinal) = columns[i];
                row.Append(Literal(reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal), column.DataTypeName));
            }
            row.Append(')');
            await writer.WriteAsync(row.ToString());

            if (++rowsInStatement == RowsPerInsert)
            {
                await writer.WriteLineAsync(";");
                rowsInStatement = 0;
            }
        }
        if (rowsInStatement > 0) await writer.WriteLineAsync(";");

        if (hasIdentity) await writer.WriteLineAsync($"SET IDENTITY_INSERT {table} OFF;");
        await writer.WriteLineAsync("GO");
    }

    private static string Literal(object? value, string? sqlType) => value switch
    {
        null => "NULL",
        string s => Text(s),
        bool b => b ? "1" : "0",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        Guid g => $"'{g}'",
        DateTime d => (sqlType ?? "").ToLowerInvariant() switch
        {
            "date" => $"'{d:yyyy-MM-dd}'",
            "datetime" or "smalldatetime" => $"'{d.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}'",
            _ => $"'{d.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)}'"
        },
        DateTimeOffset o => $"'{o.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}'",
        TimeSpan t => $"'{t.ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture)}'",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
        _ => Text(value.ToString() ?? string.Empty)
    };

    // N'...' with quotes doubled. Line breaks are written as NCHAR(13)/NCHAR(10) so the script never
    // contains a line reading just "GO" inside a value, which sqlcmd/SSMS would take as a batch end.
    private static string Text(string value)
    {
        if (value.IndexOfAny(['\r', '\n']) < 0)
        {
            return "N'" + value.Replace("'", "''") + "'";
        }

        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in value)
        {
            if (ch is '\r' or '\n')
            {
                if (current.Length > 0) parts.Add("N'" + current.ToString().Replace("'", "''") + "'");
                current.Clear();
                parts.Add(ch == '\r' ? "NCHAR(13)" : "NCHAR(10)");
            }
            else
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0) parts.Add("N'" + current.ToString().Replace("'", "''") + "'");
        // Keep the whole value nvarchar(max) so long text isn't truncated at 4000 characters.
        return "CAST(N'' AS nvarchar(max)) + " + string.Join(" + ", parts);
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    private static string Quote(string? schema, string name) => schema is null ? Quote(name) : Quote(schema) + "." + Quote(name);
}
