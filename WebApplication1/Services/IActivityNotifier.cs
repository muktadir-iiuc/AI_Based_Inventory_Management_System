namespace WebApplication1.Services;

public interface IActivityNotifier
{
    /// <summary>
    /// Pushes a live notification to everyone watching the given warehouse(s), plus Admin/Manager
    /// who watch every warehouse. Fire-and-forget from the caller's point of view: this runs after
    /// the triggering operation has already been committed to the database.
    /// </summary>
    Task NotifyAsync(string title, string message, string icon, IEnumerable<int> warehouseIds);
}
