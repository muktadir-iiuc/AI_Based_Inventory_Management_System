# Payments (Phase 5)

## Flow

1. Tenant admin opens an unpaid invoice (`/Billing/InvoiceDetails/{id}`) and presses **Pay**.
2. `PaymentGatewayService.StartAsync` creates a `PaymentAttempt` (our `tran_id`, 26 chars) for the invoice's **outstanding balance** (never a client-supplied amount) and asks the provider for a checkout URL. The browser is redirected there.
3. The provider calls back (server-to-server IPN, and browser redirects to success / fail / cancel) at
   `POST /Payments/Callback/sslcommerz/{ipn|success|fail|cancel}` (anonymous, rate limited, CSRF-exempt because the provider cannot send a token).
4. `HandleCallbackAsync` never trusts the request. It records the event (de-duplicated), checks the IPN signature when present, then **asks the provider's own API** (validation by `val_id`, or transaction query by our `tran_id`). Only a `VALID`/`VALIDATED` answer whose reference, amount and currency match our attempt reaches `BillingPaymentService.ApplyPaymentAsync`.
5. `ApplyPaymentAsync` is the only code that marks an invoice paid and extends/reactivates a subscription. It is idempotent (unique provider transaction id + idempotency key = attempt reference), uses the invoice row version, and retries on races.
6. A background pass (`BillingSchedulerService`) re-verifies attempts whose callback never arrived, expires abandoned ones and follows up in-flight refunds.

The result page (`/Billing/PaymentResult?reference=`) only shows our stored, verified state.

## SSLCOMMERZ specifics (from developer.sslcommerz.com/doc/v4)

| Item | Value |
|---|---|
| Sandbox / live host | `https://sandbox.sslcommerz.com` / `https://securepay.sslcommerz.com` |
| Initiate | `POST /gwprocess/v4/api.php` (form) |
| Validate | `GET /validator/api/validationserverAPI.php?val_id=` |
| Query by tran_id / sessionkey, refund, refund status | `GET /validator/api/merchantTransIDvalidationAPI.php` |
| Amount limits | 10.00 - 500000.00 BDT per online payment (smaller balances: bank transfer / cash) |
| IPN signature | md5 over the fields named in `verify_key` + `store_passwd=md5(password)`, sorted by key (official PHP library) |
| Risk | `risk_level = 1` on a valid payment is held (`UnderReview`) until a platform admin releases it after re-verification |
| Recurring / tokenised cards | **Not documented by SSLCOMMERZ.** Renewals are invoiced and paid each term; ask SSLCOMMERZ if you need automatic card renewal |
| Live requirements | The query and refund APIs require this server's public IP to be registered with SSLCOMMERZ |

Nagad and Rocket are reached through the SSLCOMMERZ checkout page where your merchant account has them enabled (their names are not in the documented `multi_card_name` list, so the integration does not pre-select them). Direct bKash/Nagad merchant APIs are not integrated; add another `IPaymentProvider` when you hold those merchant credentials.

## Configuration (never commit real values)

```
Payments__SslCommerz__StoreId       = <sandbox store id>
Payments__SslCommerz__StorePassword = <sandbox store password>
Payments__SslCommerz__Sandbox       = true
Payments__PublicBaseUrl             = https://your-public-host   # the URL SSLCOMMERZ can reach (required behind a proxy; use a tunnel for local testing)
```

Use `dotnet user-secrets` locally or environment variables on the server. The SSLCOMMERZ HTTP client has request logging disabled because its query/validation URLs contain the store password. Set the IPN URL in the merchant panel too (`.../Payments/Callback/sslcommerz/ipn`).

## Manual payments

Tenants report bank transfer / cash / wallet payments with a reference. They are stored as `AwaitingConfirmation` and change nothing until a platform user confirms them (`ConfirmManualPaymentAsync`; UI in Phase 6). Instructions shown to tenants: platform setting `payment.manual-instructions`.

## Refunds

`RefundService` (platform admins only): gateway payments go through the provider's refund API (asynchronous on its side; status polled), manual payments are recorded as returned outside the system. Refunding does not suspend the tenant by itself.

## Tests

`PaymentGatewayTests` (stubbed HTTP + fake provider) and `SqlServerConcurrencyTests`. The latter need a real SQL Server:

```
IMS_TEST_SQL="Server=localhost;User Id=sa;Password=..." dotnet test --filter SqlServerConcurrencyTests
```

They create and drop their own throw-away databases. **Not verified against the real SSLCOMMERZ sandbox yet** - that needs your sandbox store id/password and a public callback URL.

## Local development with the sandbox

Keep the sandbox store credentials in user-secrets, not in files (they are loaded automatically when `ASPNETCORE_ENVIRONMENT=Development`):

```
cd WebApplication1
dotnet user-secrets set "Payments:SslCommerz:StoreId" "<store id>"
dotnet user-secrets set "Payments:SslCommerz:StorePassword" "<store password>"
dotnet user-secrets set "Payments:SslCommerz:Sandbox" "true"
```

The browser is redirected back to your local address after paying, which is enough for the payment to be verified and applied. The server-to-server IPN cannot reach `localhost`; use a tunnel and set `Payments:PublicBaseUrl` to test it, otherwise the 5-minute reconciliation job picks the payment up from the gateway.

Verified against the real sandbox with a store of this kind: session initiation returns the `EasyCheckOut` page, the transaction query by `tran_id` answers, a forged `VALID` callback for an unpaid session is not applied, and a cancel is confirmed with the gateway before the attempt is closed. A full paid transaction needs someone to complete the sandbox checkout in a browser (sandbox test card 4111 1111 1111 1111, expiry 12/26, CVV 111, OTP 111111 per the SSLCOMMERZ docs).
