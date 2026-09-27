namespace WebApplication1.Extensions;

// How dates are shown across the app: always date AND time.
public static class DateDisplay
{
    public const string Format = "MMM dd, yyyy hh:mm tt";
    public const string SortFormat = "yyyyMMddHHmmss";

    // Document dates (invoices, payments, returns, transfers…) are picked in a date box, so they
    // carry no time of their own. The time shown is when the record was entered - its CreatedAt,
    // stored in UTC - on the document's own date (which may be back-dated).
    public static DateTime WithTime(this DateTime date, DateTime createdAtUtc)
        => date.Date + createdAtUtc.ToLocalTime().TimeOfDay;

    public static string ToDateTimeText(this DateTime value) => value.ToString(Format);

    public static string ToDateTimeText(this DateTime date, DateTime createdAtUtc) => date.WithTime(createdAtUtc).ToString(Format);
}
