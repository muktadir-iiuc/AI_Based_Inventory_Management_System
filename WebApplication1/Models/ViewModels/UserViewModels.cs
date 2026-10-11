using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels;

public class UserListItem
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = [];
    public List<string> WarehouseNames { get; set; } = [];
}

public class UserCreateViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public List<int> WarehouseIds { get; set; } = [];

    /// <summary>Menus assigned to this user individually (see Permissions.All).</summary>
    public List<string> PermissionKeys { get; set; } = [];
}

public class UserEditViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public List<string> Roles { get; set; } = [];

    public List<int> WarehouseIds { get; set; } = [];

    /// <summary>Menus assigned to this user individually (see Permissions.All).</summary>
    public List<string> PermissionKeys { get; set; } = [];
}

/// <summary>The Permissions page: which user sees which page.</summary>
public class PagePermissionsViewModel
{
    public List<PagePermissionUserRow> Users { get; set; } = [];
    public (string Key, string Label, string Description)[] Permissions { get; set; } = [];

    /// <summary>"userId|permissionKey" for every current grant.</summary>
    public HashSet<string> Granted { get; set; } = [];
}

public class PagePermissionUserRow
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public List<string> Warehouses { get; set; } = [];
}

/// <summary>One user on the Active Users page.</summary>
public class OnlineUserRow
{
    /// <summary>Used within this many minutes of "now" = Online.</summary>
    public const int OnlineMinutes = 5;

    /// <summary>Used within this many minutes = Idle (signed in, but quiet); beyond that, Offline.</summary>
    public const int IdleMinutes = 30;

    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public List<string> Warehouses { get; set; } = [];
    public string Status { get; set; } = "Offline";
    public DateTime? LastActivityAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsYou { get; set; }
}
