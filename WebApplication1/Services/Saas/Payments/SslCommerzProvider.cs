using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WebApplication1.Services.Saas.Payments;

/// <summary>
/// Configuration section "Payments:SslCommerz". Supply StoreId/StorePassword from user-secrets or
/// environment variables (Payments__SslCommerz__StoreId ...), never from source control.
/// </summary>
public class SslCommerzOptions
{
    public const string Section = "Payments:SslCommerz";
    public string? StoreId { get; set; }
    public string? StorePassword { get; set; }
    /// <summary>Sandbox until explicitly switched off. Live also needs this server's public IP whitelisted by SSLCOMMERZ for the query/refund APIs.</summary>
    public bool Sandbox { get; set; } = true;
}

/// <summary>
/// SSLCOMMERZ v4 (https://developer.sslcommerz.com/doc/v4/). Implemented strictly from the documented API:
/// session initiation, order validation by val_id, transaction query by tran_id / sessionkey, the IPN hash
/// check from the official library, and the refund + refund-status endpoints.
/// SSLCOMMERZ documents no recurring or tokenised-card billing, so renewals are invoiced and paid manually each term.
/// </summary>
public class SslCommerzProvider(IHttpClientFactory httpFactory, IOptions<SslCommerzOptions> options, ILogger<SslCommerzProvider> logger) : IPaymentProvider
{
    public const string ProviderCode = "sslcommerz";
    public const string HttpClientName = "sslcommerz";

    private SslCommerzOptions Options => options.Value;
    private string Host => Options.Sandbox ? "https://sandbox.sslcommerz.com" : "https://securepay.sslcommerz.com";

    public string Code => ProviderCode;
    public string DisplayName => "Online payment (bKash, Nagad, Rocket, cards, internet banking)";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Options.StoreId) && !string.IsNullOrWhiteSpace(Options.StorePassword);
    public decimal MinimumAmount => 10m; // "total_amount must be from 10.00 BDT to 500000.00 BDT"

    private HttpClient Client => httpFactory.CreateClient(HttpClientName);

    // ---------------------------------------------------------------- initiate

    public async Task<ProviderInitResult> InitiateAsync(ProviderInitRequest r, CancellationToken ct = default)
    {
        if (!IsConfigured) return new(false, null, null, "SSLCOMMERZ is not configured.");
        if (r.Currency != "BDT") return new(false, null, null, "Only BDT invoices can be paid through this gateway.");
        if (r.Amount < MinimumAmount || r.Amount > 500000m) return new(false, null, null, $"Amount must be between {MinimumAmount:0.00} and 500000.00 BDT for online payment.");

        // The documented mandatory customer fields; values we do not hold are sent as "N/A".
        string Or(string? v, string fallback = "N/A") => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        var form = new Dictionary<string, string>
        {
            ["store_id"] = Options.StoreId!,
            ["store_passwd"] = Options.StorePassword!,
            ["total_amount"] = r.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = r.Currency,
            ["tran_id"] = r.Reference,
            ["success_url"] = r.SuccessUrl,
            ["fail_url"] = r.FailUrl,
            ["cancel_url"] = r.CancelUrl,
            ["ipn_url"] = r.IpnUrl,
            ["cus_name"] = Or(r.CustomerName),
            ["cus_email"] = Or(r.CustomerEmail, "noreply@example.com"),
            ["cus_add1"] = Or(r.CustomerAddress),
            ["cus_city"] = Or(r.CustomerCity, "Dhaka"),
            ["cus_postcode"] = "1000",
            ["cus_country"] = "Bangladesh",
            ["cus_phone"] = Or(r.CustomerPhone, "01000000000"),
            ["shipping_method"] = "NO",
            ["num_of_item"] = "1",
            ["product_name"] = $"Subscription {r.InvoiceNumber}",
            ["product_category"] = "Software subscription",
            ["product_profile"] = "non-physical-goods",
            // Echoed back by the provider; informational only - we always trust our own attempt record.
            ["value_a"] = r.Reference
        };

        try
        {
            using var response = await Client.PostAsync($"{Host}/gwprocess/v4/api.php", new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var status = Str(root, "status");
            if (string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase) && Str(root, "GatewayPageURL") is { Length: > 0 } url)
            {
                return new(true, url, Str(root, "sessionkey"), null);
            }
            return new(false, null, null, Str(root, "failedreason") ?? "The gateway refused to start the payment.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Only the exception type/message - never the request (it contains the store password).
            logger.LogWarning("SSLCOMMERZ initiation failed: {Error}", ex.GetType().Name);
            return new(false, null, null, "Could not reach the payment gateway. Try again shortly.");
        }
    }

    // ---------------------------------------------------------------- callback / IPN

    public ProviderCallback ParseCallback(IReadOnlyDictionary<string, string> fields)
    {
        fields.TryGetValue("tran_id", out var tranId);
        fields.TryGetValue("status", out var status);
        fields.TryGetValue("val_id", out var valId);

        bool? signature = null;
        if (fields.ContainsKey("verify_sign") && fields.ContainsKey("verify_key"))
        {
            signature = IsSignatureValid(fields, Options.StorePassword ?? string.Empty);
        }
        // Prefer the reference we generated (value_a echo is not needed: tran_id is ours).
        return new ProviderCallback(string.IsNullOrWhiteSpace(tranId) ? null : tranId.Trim(), status, string.IsNullOrWhiteSpace(valId) ? null : valId.Trim(), signature);
    }

    /// <summary>
    /// The official library's SSLCOMMERZ_hash_verify: take the fields named in verify_key, add store_passwd = md5(store password),
    /// sort by key, join as key=value with '&amp;', md5 the result and compare to verify_sign.
    /// </summary>
    public static bool IsSignatureValid(IReadOnlyDictionary<string, string> fields, string storePassword)
    {
        if (!fields.TryGetValue("verify_sign", out var sign) || !fields.TryGetValue("verify_key", out var keys)
            || string.IsNullOrWhiteSpace(sign) || string.IsNullOrWhiteSpace(keys) || string.IsNullOrEmpty(storePassword))
        {
            return false;
        }

        var data = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys.Split(','))
        {
            var k = key.Trim();
            if (k.Length > 0 && fields.TryGetValue(k, out var v))
            {
                data[k] = v;
            }
        }
        data["store_passwd"] = Md5Hex(storePassword);

        var text = string.Join("&", data.Select(kv => $"{kv.Key}={kv.Value}"));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Md5Hex(text)), Encoding.ASCII.GetBytes(sign.Trim().ToLowerInvariant()));
    }

    private static string Md5Hex(string input) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    // ---------------------------------------------------------------- verification

    public async Task<ProviderVerification> VerifyAsync(string reference, string? valId, string? sessionKey, CancellationToken ct = default)
    {
        if (!IsConfigured) return Error("SSLCOMMERZ is not configured.");

        try
        {
            if (!string.IsNullOrWhiteSpace(valId))
            {
                var url = $"{Host}/validator/api/validationserverAPI.php?val_id={Uri.EscapeDataString(valId)}&store_id={Uri.EscapeDataString(Options.StoreId!)}&store_passwd={Uri.EscapeDataString(Options.StorePassword!)}&format=json";
                using var json = await GetJsonAsync(url, ct);
                return Map(json.RootElement, reference);
            }

            // No validation id (cancel/fail redirects, reconciliation): ask by our own transaction id.
            var query = $"{Host}/validator/api/merchantTransIDvalidationAPI.php?tran_id={Uri.EscapeDataString(reference)}&store_id={Uri.EscapeDataString(Options.StoreId!)}&store_passwd={Uri.EscapeDataString(Options.StorePassword!)}&format=json";
            using var doc = await GetJsonAsync(query, ct);
            var root = doc.RootElement;
            if (!string.Equals(Str(root, "APIConnect"), "DONE", StringComparison.OrdinalIgnoreCase))
            {
                return Error($"Gateway query not accepted ({Str(root, "APIConnect")}).");
            }
            if (!root.TryGetProperty("element", out var elements) || elements.ValueKind != JsonValueKind.Array || elements.GetArrayLength() == 0)
            {
                return new(VerificationStatus.NotFound, reference, null, null, 0, "BDT", null, "No transaction found.");
            }

            // Several attempts can exist per tran_id: a paid one wins, then pending, else failed.
            JsonElement? paid = null, pending = null, failed = null;
            foreach (var e in elements.EnumerateArray())
            {
                var s = Str(e, "status")?.ToUpperInvariant();
                if (s is "VALID" or "VALIDATED") paid ??= e;
                else if (s == "PENDING") pending ??= e;
                else failed ??= e;
            }
            if (paid is { } p) return Map(p, reference);
            if (pending is { } q) return Map(q, reference);
            return Map(failed!.Value, reference);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning("SSLCOMMERZ verification failed for {Reference}: {Error}", reference, ex.GetType().Name);
            return Error("Could not reach the payment gateway.");
        }
    }

    /// <summary>Turns one validation/query response (or query element) into our neutral result.</summary>
    internal static ProviderVerification Map(JsonElement e, string fallbackReference)
    {
        var status = Str(e, "status")?.ToUpperInvariant();
        var tranId = Str(e, "tran_id") ?? fallbackReference;

        // For non-BDT cards the gateway converts; compare what we actually charge in: its BDT amount, or the original currency amount.
        var currencyType = Str(e, "currency_type");
        var bdt = string.IsNullOrWhiteSpace(currencyType) || currencyType.Equals("BDT", StringComparison.OrdinalIgnoreCase);
        var amount = ParseAmount(bdt ? Str(e, "amount") : Str(e, "currency_amount"));
        var currency = bdt ? (Str(e, "currency") ?? "BDT") : currencyType!;
        var method = Str(e, "card_type");
        var bankTran = Str(e, "bank_tran_id");
        var valId = Str(e, "val_id");
        var risk = Str(e, "risk_level");

        switch (status)
        {
            case "VALID":
            case "VALIDATED":
                return new(risk == "1" ? VerificationStatus.PaidHeld : VerificationStatus.Paid, tranId, valId,
                    string.IsNullOrWhiteSpace(bankTran) ? valId : bankTran, amount, currency.ToUpperInvariant(), method,
                    risk == "1" ? "Provider flagged this payment as risky." : null);
            case "PENDING":
                return new(VerificationStatus.Pending, tranId, valId, bankTran, amount, currency, method, null);
            case "INVALID_TRANSACTION":
                return new(VerificationStatus.NotFound, tranId, valId, null, 0, currency, null, "Unknown validation id.");
            default:
                return new(VerificationStatus.Failed, tranId, valId, bankTran, amount, currency, method, Str(e, "error") ?? "Payment failed.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await Client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(body);
    }

    private static ProviderVerification Error(string message) => new(VerificationStatus.Error, null, null, null, 0, "BDT", null, message);

    // ---------------------------------------------------------------- refunds

    public async Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest r, CancellationToken ct = default)
    {
        if (!IsConfigured) return new(false, null, false, "SSLCOMMERZ is not configured.");
        try
        {
            var url = $"{Host}/validator/api/merchantTransIDvalidationAPI.php" +
                      $"?bank_tran_id={Uri.EscapeDataString(r.ProviderTransactionId)}" +
                      $"&refund_trans_id={Uri.EscapeDataString(r.RefundTransId)}" +
                      $"&refund_amount={r.Amount.ToString("0.00", CultureInfo.InvariantCulture)}" +
                      $"&refund_remarks={Uri.EscapeDataString(r.Reason.Length > 255 ? r.Reason[..255] : r.Reason)}" +
                      (r.PaymentReference is null ? "" : $"&refe_id={Uri.EscapeDataString(r.PaymentReference)}") +
                      $"&store_id={Uri.EscapeDataString(Options.StoreId!)}&store_passwd={Uri.EscapeDataString(Options.StorePassword!)}&v=1&format=json";
            using var json = await GetJsonAsync(url, ct);
            var root = json.RootElement;
            var api = Str(root, "APIConnect");
            var status = Str(root, "status")?.ToLowerInvariant();

            if (!string.Equals(api, "DONE", StringComparison.OrdinalIgnoreCase))
            {
                return new(false, null, false, $"Refund request not accepted ({api}). {Str(root, "errorReason")}".Trim());
            }
            return status switch
            {
                "success" => new(true, Str(root, "refund_ref_id"), false, null),
                "processing" => new(true, Str(root, "refund_ref_id"), true, null),
                _ => new(false, null, false, Str(root, "errorReason") ?? "Refund could not be initiated.")
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning("SSLCOMMERZ refund failed: {Error}", ex.GetType().Name);
            return new(false, null, false, "Could not reach the payment gateway.");
        }
    }

    public async Task<ProviderRefundState> GetRefundStateAsync(string providerRefundRef, CancellationToken ct = default)
    {
        if (!IsConfigured) return ProviderRefundState.Unknown;
        try
        {
            var url = $"{Host}/validator/api/merchantTransIDvalidationAPI.php?refund_ref_id={Uri.EscapeDataString(providerRefundRef)}&store_id={Uri.EscapeDataString(Options.StoreId!)}&store_passwd={Uri.EscapeDataString(Options.StorePassword!)}&format=json";
            using var json = await GetJsonAsync(url, ct);
            return Str(json.RootElement, "status")?.ToLowerInvariant() switch
            {
                "refunded" => ProviderRefundState.Refunded,
                "cancelled" => ProviderRefundState.Cancelled,
                "processing" => ProviderRefundState.Processing,
                _ => ProviderRefundState.Unknown
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return ProviderRefundState.Unknown;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), JsonValueKind.Null => null, _ => v.GetRawText() }
            : null;

    private static decimal ParseAmount(string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
