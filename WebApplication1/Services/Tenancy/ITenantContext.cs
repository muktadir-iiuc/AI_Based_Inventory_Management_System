namespace WebApplication1.Services.Tenancy;

/// <summary>
/// The tenant every tenant-scoped query and write is bound to. The id comes only from the
/// authenticated user's TenantId claim (set at sign-in from the database) or from an explicit
/// scope opened by trusted server code (seeding, background jobs, platform services) - never from
/// request input.
/// </summary>
public interface ITenantContext
{
    /// <summary>Current tenant id, or 0 when there is none (anonymous, platform staff). 0 matches no rows.</summary>
    int TenantId { get; }

    /// <summary>True while a platform/system scope has switched tenant filtering off.</summary>
    bool IsFilterBypassed { get; }

    /// <summary>Run as this tenant until disposed (jobs, seeding, provisioning).</summary>
    IDisposable UseTenant(int tenantId);

    /// <summary>Cross-tenant access for platform services and jobs. Use narrowly.</summary>
    IDisposable BypassFilters();
}
