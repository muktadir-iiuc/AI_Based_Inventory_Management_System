using Microsoft.AspNetCore.Authorization;

namespace WebApplication1.Authorization;

public class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}
