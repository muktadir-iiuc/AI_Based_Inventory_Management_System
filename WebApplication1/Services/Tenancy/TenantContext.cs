using System.Security.Claims;

namespace WebApplication1.Services.Tenancy;

public class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public const string ClaimType = "TenantId";

    private int? _override;
    private int _bypassDepth;

    public int TenantId
    {
        get
        {
            if (_override.HasValue)
            {
                return _override.Value;
            }
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimType);
            return int.TryParse(value, out var id) && id > 0 ? id : 0;
        }
    }

    public bool IsFilterBypassed => _bypassDepth > 0;

    public IDisposable UseTenant(int tenantId)
    {
        var previous = _override;
        _override = tenantId;
        return new Scope(() => _override = previous);
    }

    public IDisposable BypassFilters()
    {
        _bypassDepth++;
        return new Scope(() => _bypassDepth--);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose() { _onDispose?.Invoke(); _onDispose = null; }
    }
}
