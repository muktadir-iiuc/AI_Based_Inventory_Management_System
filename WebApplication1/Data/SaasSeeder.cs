using Microsoft.EntityFrameworkCore;
using WebApplication1.Models.Saas;
using WebApplication1.Services.Saas;

namespace WebApplication1.Data;

/// <summary>Seeds example plans (editable afterwards from the platform admin) and attaches the pre-existing business to a plan.</summary>
public static class SaasSeeder
{
    public static async Task SeedAsync(IServiceProvider services, int defaultTenantId)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        if (!await db.SubscriptionPlans.AnyAsync())
        {
            var allFeatures = Features.All.Select(f => f.Key).ToArray();
            string[] basic = [Features.Purchasing, Features.Sales, Features.Reports, Features.StockAdjustments];

            db.SubscriptionPlans.AddRange(
                Plan("trial", "Trial", "Free trial with limited features.", 0, 1, BillingIntervalUnit.Month, trialDays: 14, grace: 0, isPublic: false, order: 0,
                    users: 2, products: 100, warehouses: 1, features: [Features.Purchasing, Features.Sales, Features.Reports]),
                Plan("basic-monthly", "Basic (Monthly)", "Entry-level plan for a single shop.", 1500, 1, BillingIntervalUnit.Month, order: 10,
                    users: 5, products: 1000, warehouses: 2, features: basic),
                Plan("basic-yearly", "Basic (Yearly)", "Basic plan billed yearly.", 15000, 12, BillingIntervalUnit.Month, order: 11,
                    users: 5, products: 1000, warehouses: 2, features: basic),
                Plan("professional-monthly", "Professional (Monthly)", "More users, warehouses and every module.", 4000, 1, BillingIntervalUnit.Month, order: 20,
                    users: 20, products: 10000, warehouses: 10, features: allFeatures),
                Plan("professional-yearly", "Professional (Yearly)", "Professional plan billed yearly.", 40000, 12, BillingIntervalUnit.Month, order: 21,
                    users: 20, products: 10000, warehouses: 10, features: allFeatures),
                Plan("enterprise", "Enterprise", "Negotiated contract: price and limits are set per tenant by the platform administrator.", 0, 12, BillingIntervalUnit.Month, isPublic: false, order: 30,
                    users: null, products: null, warehouses: null, features: allFeatures),
                Plan("internal", "Internal / Legacy", "Pre-SaaS business: all modules, no limits, no billing.", 0, 12, BillingIntervalUnit.Month, isPublic: false, autoRenew: false, order: 99,
                    users: null, products: null, warehouses: null, features: allFeatures));
            await db.SaveChangesAsync();
        }

        // Provider switches. Whether a provider is actually usable also depends on credentials in configuration.
        if (!await db.PaymentProviders.AnyAsync())
        {
            db.PaymentProviders.AddRange(
                new PaymentProviderConfig { Code = "sslcommerz", DisplayName = "Online payment (bKash, Nagad, Rocket, cards, internet banking)", SupportedMethods = "bKash, Nagad, Rocket, Visa, Mastercard, internet banking", IsEnabled = true, SortOrder = 1 },
                new PaymentProviderConfig { Code = "manual", DisplayName = "Bank transfer / cash", SupportedMethods = "Bank transfer, cash (confirmed by our team)", IsEnabled = true, SortOrder = 9 });
            await db.SaveChangesAsync();
        }

        // The business that existed before multi-tenancy keeps working exactly as before: active, all modules, no limits, no invoices.
        if (!await db.TenantSubscriptions.AnyAsync(s => s.TenantId == defaultTenantId))
        {
            var internalPlan = await db.SubscriptionPlans.FirstAsync(p => p.Code == "internal");
            await services.GetRequiredService<ISubscriptionService>().StartAsync(defaultTenantId, internalPlan.Id, StartMode.ActivateNow, "system");
        }
    }

    private static SubscriptionPlan Plan(string code, string name, string description, decimal price, int intervalCount, BillingIntervalUnit unit,
        int trialDays = 0, int grace = 7, bool isPublic = true, bool autoRenew = true, int order = 0,
        int? users = null, int? products = null, int? warehouses = null, string[]? features = null)
    {
        var plan = new SubscriptionPlan
        {
            Code = code, Name = name, Description = description, Price = price, Currency = "BDT",
            IntervalCount = intervalCount, IntervalUnit = unit, TrialDays = trialDays, GraceDays = grace,
            IsPublic = isPublic, AutoRenew = autoRenew, SortOrder = order, Status = PlanStatus.Active,
            MaxUsers = users, MaxProducts = products, MaxWarehouses = warehouses
        };
        foreach (var f in features ?? []) plan.Features.Add(new PlanFeature { FeatureKey = f });
        return plan;
    }
}
