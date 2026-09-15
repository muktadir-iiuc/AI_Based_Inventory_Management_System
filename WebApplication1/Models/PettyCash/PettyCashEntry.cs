using System.ComponentModel.DataAnnotations.Schema;
using WebApplication1.Models.Common;

namespace WebApplication1.Models.PettyCash;

public enum PettyCashType
{
    CashIn = 0,
    CashOut = 1
}

public class PettyCashEntry : BaseEntity
{
    public DateTime Date { get; set; }

    public int PettyCashNameId { get; set; }
    public PettyCashName? PettyCashName { get; set; }

    public PettyCashType Type { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
}
