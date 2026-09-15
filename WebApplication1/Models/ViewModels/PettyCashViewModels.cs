namespace WebApplication1.Models.ViewModels;

public class PettyCashNameRequest
{
    public string Name { get; set; } = string.Empty;
}

public class PettyCashEntryRequest
{
    public DateTime Date { get; set; }
    public int NameId { get; set; }
    public int Type { get; set; }
    public decimal Amount { get; set; }
}
