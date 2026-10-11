using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models.Identity;

namespace WebApplication1.Data;

/// <summary>Creates the platform roles and, when configured, the first Platform Administrator.</summary>
public static class PlatformSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext db,
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        IWebHostEnvironment env,
        bool demo = false)
    {
        var roles = new Dictionary<string, string>
        {
            [Roles.PlatformAdmin] = "SaaS operator: manages tenants, plans, subscriptions, billing and platform settings.",
            [Roles.PlatformBilling] = "SaaS operator billing staff: invoices, payments and subscription billing."
        };
        foreach (var (name, description) in roles)
        {
            if (!await roleManager.RoleExistsAsync(name))
            {
                await roleManager.CreateAsync(new ApplicationRole(name) { Description = description });
            }
        }

        // Credentials come from configuration (user-secrets / environment: Platform__AdminEmail,
        // Platform__AdminPassword). Nothing is hard-coded; without them no platform admin is created.
        var email = configuration["Platform:AdminEmail"];
        var password = configuration["Platform:AdminPassword"];
        if ((string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) && demo)
        {
            // Demo/development only (same switch as the other demo logins). Never created in production unless Seed:DemoData=true.
            email = "platform@inventory.local";
            password = "Platform@123";
        }
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = configuration["Platform:AdminName"] ?? "Platform Administrator",
            EmailConfirmed = true,
            IsActive = true,
            TenantId = null
        };
        var result = await userManager.CreateAsync(admin, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, Roles.PlatformAdmin);
        }
    }
}
