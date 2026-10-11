using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebApplication1.Services.Saas.Payments;

namespace WebApplication1.Controllers;

/// <summary>
/// Where payment gateways send the customer's browser (success / fail / cancel) and their server (IPN).
/// Anonymous by necessity - the gateway's POST carries no login cookie - so nothing here trusts the request:
/// every call is re-verified with the provider's own API before it can change anything (see PaymentGatewayService).
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("payment-callback")]
[Route("Payments/Callback/{provider}/{source}")]
public class PaymentsController(IPaymentGatewayService gateway, ILogger<PaymentsController> logger) : Controller
{
    private static readonly HashSet<string> Sources = ["success", "fail", "cancel", "ipn"];
    private const int MaxFields = 100;
    private const int MaxValueLength = 2000;

    [HttpPost, HttpGet]
    public async Task<IActionResult> Callback(string provider, string source, CancellationToken ct)
    {
        source = source.ToLowerInvariant();
        if (!Sources.Contains(source)) return NotFound();

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Request.HasFormContentType)
        {
            foreach (var (key, value) in Request.Form.Take(MaxFields)) Add(fields, key, value.ToString());
        }
        foreach (var (key, value) in Request.Query.Take(MaxFields)) Add(fields, key, value.ToString());

        CallbackResult result;
        try
        {
            result = await gateway.HandleCallbackAsync(provider, source, fields, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never leak details to the caller. 500 makes a server-to-server IPN get retried by the provider.
            logger.LogError(ex, "Payment callback {Provider}/{Source} failed.", provider, source);
            return source == "ipn" ? StatusCode(500) : RedirectToAction("Index", "Billing");
        }

        if (source == "ipn")
        {
            // The provider only needs to know we got it; ask it to retry when we could not reach it to verify.
            return result.Outcome == "verification-unavailable" ? StatusCode(503) : Ok("OK");
        }

        return result.AttemptReference is { } reference
            ? RedirectToAction("PaymentResult", "Billing", new { reference })
            : RedirectToAction("Index", "Billing");
    }

    private static void Add(Dictionary<string, string> fields, string key, string value)
    {
        if (key.Length is > 0 and <= 60)
        {
            fields.TryAdd(key, value.Length > MaxValueLength ? value[..MaxValueLength] : value);
        }
    }
}
