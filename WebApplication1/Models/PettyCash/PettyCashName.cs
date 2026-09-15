using System.ComponentModel.DataAnnotations;
using WebApplication1.Models.Common;

namespace WebApplication1.Models.PettyCash;

public class PettyCashName : BaseEntity
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public ICollection<PettyCashEntry> Entries { get; set; } = [];
}
