using System.ComponentModel.DataAnnotations;
using WebApplication1.Models.Common;

namespace WebApplication1.Models.ViewModels;

public class BackupSettingsViewModel
{
    [Display(Name = "Enable automatic backups")]
    public bool IsEnabled { get; set; }

    [Display(Name = "Frequency")]
    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

    [Display(Name = "Time of day")]
    public TimeOnly TimeOfDay { get; set; } = new(2, 0);

    [Display(Name = "Day of week")]
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;

    [Range(1, 365, ErrorMessage = "Keep between 1 and 365 backups.")]
    [Display(Name = "Backups to keep")]
    public int RetentionCount { get; set; } = 14;

    [StringLength(400)]
    [Display(Name = "Backup folder")]
    public string? BackupDirectory { get; set; }
}

public class BackupIndexViewModel
{
    public BackupSettingsViewModel Settings { get; set; } = new();

    // Where backups go right now (configured folder, else SQL Server's default), as seen by SQL Server.
    public string? EffectiveDirectory { get; set; }
    public string DatabaseName { get; set; } = string.Empty;

    public DateTime? NextRunLocal { get; set; }
    public DateTime? LastScheduledRunLocal { get; set; }

    public List<DatabaseBackup> Backups { get; set; } = [];

    // Ids whose file the web server can read — only those get a Download button.
    public HashSet<int> Downloadable { get; set; } = [];
}
