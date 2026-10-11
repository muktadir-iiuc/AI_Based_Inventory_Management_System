using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WebApplication1.Data;
using WebApplication1.Models.Saas;

namespace WebApplication1.Services.Saas;

public record DataExportResult(string FileName, long Size, int Tables, long Rows);

public interface IDataExportService
{
    /// <summary>Writes one tenant's data - every table that holds rows for it - in the chosen format. Returns the file (inside <see cref="FolderFor"/>).</summary>
    Task<DataExportResult> GenerateAsync(int tenantId, ExportFormat format, CancellationToken ct = default);

    string FolderFor(int tenantId);
    string ContentType(ExportFormat format);
}

/// <summary>
/// Builds the data export a tenant asked for. It reads each tenant-owned table with an explicit TenantId predicate (raw SQL), so it
/// is correct no matter which tenant scope the caller runs in, skips tables with no rows, and never exports password hashes or tokens.
/// </summary>
public class DataExportService(ApplicationDbContext db, IWebHostEnvironment env) : IDataExportService
{
    private const int MaxInlineBinary = 32 * 1024;

    public string FolderFor(int tenantId) => Path.Combine(env.ContentRootPath, "App_Data", "Exports", tenantId.ToString(CultureInfo.InvariantCulture));

    public string ContentType(ExportFormat format) => format switch
    {
        ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/zip"
    };

    private sealed record TableSource(string Name, string[] Columns, IEnumerable<object?[]> Rows);

    public async Task<DataExportResult> GenerateAsync(int tenantId, ExportFormat format, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, ct);
        Directory.CreateDirectory(FolderFor(tenantId));

        var safeCode = new string(tenant.Code.Where(char.IsLetterOrDigit).ToArray());
        var ext = format == ExportFormat.Xlsx ? "xlsx" : "zip";
        var fileName = $"{(safeCode.Length > 0 ? safeCode : "tenant")}-data-{format.ToString().ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{ext}";
        var finalPath = Path.Combine(FolderFor(tenantId), fileName);
        var tempPath = finalPath + ".tmp";

        var wasClosed = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (wasClosed) await db.Database.OpenConnectionAsync(ct);
        var summary = new List<(string Table, long Rows)>();
        try
        {
            await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                ITableSink sink = format switch
                {
                    ExportFormat.Json => new JsonSink(zip),
                    ExportFormat.Xlsx => new XlsxSink(zip),
                    _ => new CsvSink(zip)
                };

                foreach (var source in await LoadSourcesAsync(tenantId, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var rows = sink.Write(source.Name, source.Columns, source.Rows);
                    if (rows > 0) summary.Add((source.Name, rows));
                }
                sink.Finish(tenant.Name, tenant.Code, summary);
            }
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            if (wasClosed) await db.Database.CloseConnectionAsync();
        }

        return new DataExportResult(fileName, new FileInfo(finalPath).Length, summary.Count, summary.Sum(s => s.Rows));
    }

    // ---------------------------------------------------------------- sources

    private async Task<List<TableSource>> LoadSourcesAsync(int tenantId, CancellationToken ct)
    {
        var sources = new List<TableSource>();
        var entityTypes = db.Model.GetEntityTypes()
            .Where(e => ApplicationDbContext.IsTenantEntity(e.ClrType) && e.GetTableName() is not null)
            .OrderBy(e => e.GetTableName())
            .ToList();

        foreach (var entity in entityTypes)
        {
            var table = entity.GetTableName()!;
            var schema = entity.GetSchema() ?? "dbo";
            var store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
            if (store is null) continue;
            var storeId = store.Value;
            var props = entity.GetProperties()
                .Where(p => p.GetColumnName(storeId) is not null && !(p.IsConcurrencyToken && p.ClrType == typeof(byte[])))
                .ToList();
            if (props.Count == 0) continue;

            var cols = props.Select(p => p.GetColumnName(storeId)!).ToArray();
            var sql = $"SELECT {string.Join(", ", cols.Select(c => "[" + c + "]"))} FROM [{schema}].[{table}] WHERE [TenantId] = @tenantId ORDER BY 1";

            var rows = new List<object?[]>();
            await using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = sql;
                var p = cmd.CreateParameter();
                p.ParameterName = "@tenantId";
                p.Value = tenantId;
                cmd.Parameters.Add(p);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var row = new object?[cols.Length];
                    for (var i = 0; i < cols.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
            }
            sources.Add(new TableSource(table, cols, rows));
        }

        sources.Add(await LoadUsersAsync(tenantId, ct));
        return sources;
    }

    // Identity users of the tenant: business-relevant columns only (no password hash, security stamp or tokens).
    private async Task<TableSource> LoadUsersAsync(int tenantId, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Where(u => u.TenantId == tenantId).OrderBy(u => u.Email).ToListAsync(ct);
        var roleRows = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              join u in db.Users on ur.UserId equals u.Id
                              where u.TenantId == tenantId
                              select new { ur.UserId, r.Name }).ToListAsync(ct);
        var roles = roleRows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).OrderBy(n => n)));

        string[] cols = ["Id", "Email", "UserName", "FullName", "PhoneNumber", "Roles", "IsActive", "CreatedAt", "LastLoginAt"];
        var rows = users.Select(u => new object?[]
        {
            u.Id, u.Email, u.UserName, u.FullName, u.PhoneNumber, roles.GetValueOrDefault(u.Id), u.IsActive, u.CreatedAt, u.LastLoginAt
        }).ToList();
        return new TableSource("Users", cols, rows);
    }

    // ---------------------------------------------------------------- value formatting

    private static bool IsNumber(object v) => v is byte or short or int or long or decimal or double or float;

    public static string Text(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => bytes.Length <= MaxInlineBinary ? Convert.ToBase64String(bytes) : $"[binary {bytes.Length} bytes]",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) if (XmlConvert.IsXmlChar(ch) || char.IsSurrogate(ch)) sb.Append(ch);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- sinks

    private interface ITableSink
    {
        /// <summary>Writes a table (nothing at all when it has no rows) and returns the row count.</summary>
        long Write(string name, string[] columns, IEnumerable<object?[]> rows);
        void Finish(string tenantName, string tenantCode, IReadOnlyList<(string Table, long Rows)> tables);
    }

    private static string ReadmeText(string tenantName, string tenantCode, string format, IReadOnlyList<(string Table, long Rows)> tables) =>
        $"""
        Data export for {tenantName} ({tenantCode})
        Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}
        Format: {format}
        Data agreement version: {DataAgreement.Version}

        One file per table that holds data. Dates are UTC, written as yyyy-MM-dd HH:mm:ss. Money and quantities are plain numbers.
        Passwords, security stamps and tokens are never exported.

        Tables ({tables.Count}):
        {string.Join(Environment.NewLine, tables.Select(t => $"  {t.Table}: {t.Rows} rows"))}
        """;

    private sealed class CsvSink(ZipArchive zip) : ITableSink
    {
        public long Write(string name, string[] columns, IEnumerable<object?[]> rows)
        {
            long count = 0;
            StreamWriter? writer = null;
            try
            {
                foreach (var row in rows)
                {
                    if (writer is null)
                    {
                        writer = new StreamWriter(zip.CreateEntry($"csv/{name}.csv", CompressionLevel.Optimal).Open(), new UTF8Encoding(true)) { NewLine = "\r\n" };
                        writer.WriteLine(string.Join(",", columns.Select(Quote)));
                    }
                    writer.WriteLine(string.Join(",", row.Select(v => Quote(Text(v)))));
                    count++;
                }
            }
            finally { writer?.Dispose(); }
            return count;
        }

        // Quoted always; the text is exported exactly as stored (the file is the tenant's own data).
        private static string Quote(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";

        public void Finish(string tenantName, string tenantCode, IReadOnlyList<(string Table, long Rows)> tables) =>
            ReadmeSink.Write(zip, ReadmeText(tenantName, tenantCode, "CSV (UTF-8)", tables), tables);
    }

    private sealed class JsonSink(ZipArchive zip) : ITableSink
    {
        public long Write(string name, string[] columns, IEnumerable<object?[]> rows)
        {
            long count = 0;
            Stream? stream = null;
            Utf8JsonWriter? json = null;
            try
            {
                foreach (var row in rows)
                {
                    if (json is null)
                    {
                        stream = zip.CreateEntry($"json/{name}.json", CompressionLevel.Optimal).Open();
                        json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                        json.WriteStartArray();
                    }
                    json.WriteStartObject();
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var v = row[i];
                        json.WritePropertyName(columns[i]);
                        if (v is null) json.WriteNullValue();
                        else if (v is bool b) json.WriteBooleanValue(b);
                        else if (v is decimal dec) json.WriteNumberValue(dec);
                        else if (v is long l) json.WriteNumberValue(l);
                        else if (v is int n) json.WriteNumberValue(n);
                        else if (v is short sh) json.WriteNumberValue(sh);
                        else if (v is byte by) json.WriteNumberValue(by);
                        else if (v is double d && double.IsFinite(d)) json.WriteNumberValue(d);
                        else if (v is float f && float.IsFinite(f)) json.WriteNumberValue(f);
                        else json.WriteStringValue(Text(v));
                    }
                    json.WriteEndObject();
                    count++;
                }
                if (json is not null) { json.WriteEndArray(); json.Flush(); }
            }
            finally { json?.Dispose(); stream?.Dispose(); }
            return count;
        }

        public void Finish(string tenantName, string tenantCode, IReadOnlyList<(string Table, long Rows)> tables) =>
            ReadmeSink.Write(zip, ReadmeText(tenantName, tenantCode, "JSON (UTF-8)", tables), tables);
    }

    private static class ReadmeSink
    {
        public static void Write(ZipArchive zip, string readme, IReadOnlyList<(string Table, long Rows)> tables)
        {
            using (var w = new StreamWriter(zip.CreateEntry("README.txt").Open(), new UTF8Encoding(true))) w.Write(readme);
            using var stream = zip.CreateEntry("manifest.json").Open();
            using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            json.WriteStartObject();
            json.WriteString("agreementVersion", DataAgreement.Version);
            json.WriteString("generatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            json.WriteStartArray("tables");
            foreach (var (table, rows) in tables)
            {
                json.WriteStartObject();
                json.WriteString("table", table);
                json.WriteNumber("rows", rows);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
    }

    /// <summary>Minimal .xlsx writer: one worksheet per table with inline strings, no styles, no external library.</summary>
    private sealed class XlsxSink(ZipArchive zip) : ITableSink
    {
        private const int MaxRows = 1_048_575;   // Excel allows 1,048,576 rows including the header
        private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private readonly List<(string SheetName, string Part)> _sheets = [];
        private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

        private string UniqueName(string name)
        {
            var clean = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
            if (clean.Length == 0) clean = "Sheet";
            if (clean.Length > 31) clean = clean[..31];
            var candidate = clean;
            for (var i = 2; !_used.Add(candidate); i++)
            {
                var suffix = "_" + i;
                candidate = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix;
            }
            return candidate;
        }

        private static string ColumnLetters(int index)
        {
            var sb = new StringBuilder();
            for (var n = index + 1; n > 0; n = (n - 1) / 26) sb.Insert(0, (char)('A' + (n - 1) % 26));
            return sb.ToString();
        }

        private static void Cell(XmlWriter w, int col, int row, object? value, bool forceText = false)
        {
            if (value is null) return;
            var r = ColumnLetters(col) + row.ToString(CultureInfo.InvariantCulture);
            if (!forceText && IsNumber(value) && !(value is double d && !double.IsFinite(d)) && !(value is float f && !float.IsFinite(f)))
            {
                w.WriteStartElement("c"); w.WriteAttributeString("r", r);
                w.WriteElementString("v", Text(value));
                w.WriteEndElement();
                return;
            }
            var text = Clean(Text(value));
            w.WriteStartElement("c"); w.WriteAttributeString("r", r); w.WriteAttributeString("t", "inlineStr");
            w.WriteStartElement("is"); w.WriteStartElement("t");
            w.WriteAttributeString("xml", "space", null, "preserve");
            w.WriteString(text);
            w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
        }

        private long WriteSheet(string sheetName, string[] columns, IEnumerable<object?[]> rows, bool skipWhenEmpty)
        {
            var part = $"xl/worksheets/sheet{_sheets.Count + 1}.xml";
            long count = 0;
            XmlWriter? w = null;
            Stream? stream = null;
            try
            {
                foreach (var row in rows)
                {
                    if (count >= MaxRows) break;
                    if (w is null)
                    {
                        stream = zip.CreateEntry(part, CompressionLevel.Optimal).Open();
                        w = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true });
                        w.WriteStartDocument();
                        w.WriteStartElement("worksheet", Main);
                        w.WriteStartElement("sheetData");
                        w.WriteStartElement("row"); w.WriteAttributeString("r", "1");
                        for (var c = 0; c < columns.Length; c++) Cell(w, c, 1, columns[c], forceText: true);
                        w.WriteEndElement();
                    }
                    var excelRow = (int)count + 2;
                    w.WriteStartElement("row"); w.WriteAttributeString("r", excelRow.ToString(CultureInfo.InvariantCulture));
                    for (var c = 0; c < columns.Length; c++) Cell(w, c, excelRow, row[c]);
                    w.WriteEndElement();
                    count++;
                }
                if (w is not null)
                {
                    w.WriteEndElement(); w.WriteEndElement(); w.WriteEndDocument(); w.Flush();
                    _sheets.Add((UniqueName(sheetName), part));
                }
            }
            finally { w?.Dispose(); stream?.Dispose(); }
            return count;
        }

        public long Write(string name, string[] columns, IEnumerable<object?[]> rows) => WriteSheet(name, columns, rows, skipWhenEmpty: true);

        public void Finish(string tenantName, string tenantCode, IReadOnlyList<(string Table, long Rows)> tables)
        {
            // An "About" sheet, listed first in the workbook.
            var about = new List<object?[]>
            {
                new object?[] { "Business", tenantName }, new object?[] { "Code", tenantCode },
                new object?[] { "Generated (UTC)", DateTime.UtcNow }, new object?[] { "Data agreement version", DataAgreement.Version },
                new object?[] { "Note", "One sheet per table that holds data. Dates are UTC. Passwords and tokens are never exported." },
                new object?[] { "", "" }
            };
            about.AddRange(tables.Select(t => new object?[] { t.Table, t.Rows }));
            WriteSheet("About", ["Item", "Value"], about, skipWhenEmpty: false);
            var aboutSheet = _sheets[^1];
            _sheets.RemoveAt(_sheets.Count - 1);
            _sheets.Insert(0, aboutSheet);

            using (var w = XmlWriter.Create(zip.CreateEntry("[Content_Types].xml").Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true }))
            {
                const string ct = "http://schemas.openxmlformats.org/package/2006/content-types";
                w.WriteStartDocument(); w.WriteStartElement("Types", ct);
                w.WriteStartElement("Default"); w.WriteAttributeString("Extension", "rels"); w.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml"); w.WriteEndElement();
                w.WriteStartElement("Default"); w.WriteAttributeString("Extension", "xml"); w.WriteAttributeString("ContentType", "application/xml"); w.WriteEndElement();
                w.WriteStartElement("Override"); w.WriteAttributeString("PartName", "/xl/workbook.xml"); w.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"); w.WriteEndElement();
                foreach (var s in _sheets)
                {
                    w.WriteStartElement("Override"); w.WriteAttributeString("PartName", "/" + s.Part);
                    w.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"); w.WriteEndElement();
                }
                w.WriteEndElement(); w.WriteEndDocument();
            }

            const string pr = "http://schemas.openxmlformats.org/package/2006/relationships";
            const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            using (var w = XmlWriter.Create(zip.CreateEntry("_rels/.rels").Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true }))
            {
                w.WriteStartDocument(); w.WriteStartElement("Relationships", pr);
                w.WriteStartElement("Relationship"); w.WriteAttributeString("Id", "rId1"); w.WriteAttributeString("Type", rel + "/officeDocument"); w.WriteAttributeString("Target", "xl/workbook.xml"); w.WriteEndElement();
                w.WriteEndElement(); w.WriteEndDocument();
            }

            using (var w = XmlWriter.Create(zip.CreateEntry("xl/workbook.xml").Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true }))
            {
                w.WriteStartDocument(); w.WriteStartElement("workbook", Main); w.WriteAttributeString("xmlns", "r", null, rel);
                w.WriteStartElement("sheets");
                for (var i = 0; i < _sheets.Count; i++)
                {
                    w.WriteStartElement("sheet");
                    w.WriteAttributeString("name", Clean(_sheets[i].SheetName));
                    w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("r", "id", rel, "rId" + (i + 1));
                    w.WriteEndElement();
                }
                w.WriteEndElement(); w.WriteEndElement(); w.WriteEndDocument();
            }

            using (var w = XmlWriter.Create(zip.CreateEntry("xl/_rels/workbook.xml.rels").Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true }))
            {
                w.WriteStartDocument(); w.WriteStartElement("Relationships", pr);
                for (var i = 0; i < _sheets.Count; i++)
                {
                    w.WriteStartElement("Relationship");
                    w.WriteAttributeString("Id", "rId" + (i + 1));
                    w.WriteAttributeString("Type", rel + "/worksheet");
                    w.WriteAttributeString("Target", _sheets[i].Part["xl/".Length..]);
                    w.WriteEndElement();
                }
                w.WriteEndElement(); w.WriteEndDocument();
            }
        }
    }
}
