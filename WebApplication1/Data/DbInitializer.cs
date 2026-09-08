using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models.Accounting;
using WebApplication1.Models.Common;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Inventory;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.Sales;
using WebApplication1.Services;

namespace WebApplication1.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stockService = scope.ServiceProvider.GetRequiredService<IStockService>();
        var accountingService = scope.ServiceProvider.GetRequiredService<IAccountingService>();

        await db.Database.MigrateAsync();

        await SeedCompanySettingsAsync(db, scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>());
        await SeedRolesAsync(roleManager);
        var warehouses = await SeedWarehousesAsync(db);
        await SeedUsersAsync(db, userManager, warehouses);
        await SeedDefaultPermissionsAsync(db);

        if (await db.Accounts.AnyAsync())
        {
            return; // business data already seeded
        }

        await SeedChartOfAccountsAsync(db, accountingService);
        var categories = await SeedCategoriesAsync(db);
        var units = await SeedUnitsAsync(db);
        var products = await SeedProductsAsync(db, categories, units);
        var suppliers = await SeedSuppliersAsync(db);
        var customers = await SeedCustomersAsync(db);

        await SeedOpeningBalanceAsync(db, accountingService);
        await SeedPurchaseHistoryAsync(db, stockService, accountingService, products, suppliers, warehouses);
        await SeedSalesHistoryAsync(db, stockService, accountingService, products, customers, warehouses);
        await SeedSamplePaymentsAsync(db, accountingService);
    }

    private static async Task SeedCompanySettingsAsync(ApplicationDbContext db, IWebHostEnvironment env)
    {
        if (await db.CompanySettings.AnyAsync())
        {
            return;
        }

        // Seeded once with the company name and logo the app ships with; an Admin changes both from
        // Company Settings and every page title, the sidebar brand, the sign-in screen, the footer
        // and the invoice PDF header follow.
        var logoPath = Path.Combine(env.WebRootPath, "img", "company-logo.png");
        var logo = File.Exists(logoPath) ? await File.ReadAllBytesAsync(logoPath) : null;

        db.CompanySettings.Add(new CompanySetting
        {
            CompanyName = CompanySettingsService.DefaultCompanyName,
            ShortName = CompanySettingsService.DefaultShortName,
            Address = "House 14, Road 7, Banani, Dhaka-1213, Bangladesh",
            LogoImage = logo,
            LogoMimeType = logo is null ? null : "image/png"
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        var descriptions = new Dictionary<string, string>
        {
            [Roles.Admin] = "Full system access, including user and role management.",
            [Roles.Manager] = "Full operational access across all modules.",
            [Roles.PurchaseOfficer] = "Manages suppliers and purchase invoices.",
            [Roles.SalesOfficer] = "Manages customers and sales invoices.",
            [Roles.Accountant] = "Manages chart of accounts, journal entries and payments.",
            [Roles.Viewer] = "Read-only access to dashboards and reports."
        };

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role) { Description = descriptions[role] });
            }
        }
    }

    private static async Task SeedDefaultPermissionsAsync(ApplicationDbContext db)
    {
        if (await db.RolePermissions.AnyAsync())
        {
            return;
        }

        // Purchase officers are the natural owners of physical stock movement out of the box;
        // an Admin can grant/revoke this (and future permissions) for any role from the UI.
        db.RolePermissions.Add(new RolePermission { RoleName = Roles.PurchaseOfficer, PermissionKey = Permissions.StockTransfer });
        await db.SaveChangesAsync();
    }

    private static async Task<List<Warehouse>> SeedWarehousesAsync(ApplicationDbContext db)
    {
        if (await db.Warehouses.AnyAsync())
        {
            return await db.Warehouses.OrderBy(w => w.Code).ToListAsync();
        }

        var warehouses = new[]
        {
            new Warehouse { Code = "WH-01", Name = "Main Warehouse", Location = "Dhaka Central Depot" },
            new Warehouse { Code = "WH-02", Name = "Downtown Branch", Location = "Dhaka Downtown Retail Hub" },
        };

        db.Warehouses.AddRange(warehouses);
        await db.SaveChangesAsync();
        return warehouses.ToList();
    }

    private static async Task SeedUsersAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, List<Warehouse> warehouses)
    {
        var mainWarehouseId = warehouses[0].Id;
        var branchWarehouseId = warehouses[1].Id;

        var seedUsers = new (string Email, string FullName, string Role, string Password, int[] WarehouseIds)[]
        {
            ("admin@inventory.local", "System Administrator", Roles.Admin, "Admin@123", []),
            ("manager@inventory.local", "Morgan Manager", Roles.Manager, "Manager@123", []),
            ("purchase@inventory.local", "Pat Purchasing", Roles.PurchaseOfficer, "Purchase@123", [mainWarehouseId]),
            ("sales@inventory.local", "Sam Sales", Roles.SalesOfficer, "Sales@123", [branchWarehouseId]),
            ("accounts@inventory.local", "Alex Accountant", Roles.Accountant, "Accounts@123", [mainWarehouseId]),
            ("viewer@inventory.local", "Val Viewer", Roles.Viewer, "Viewer@123", [branchWarehouseId]),
        };

        foreach (var (email, fullName, role, password, warehouseIds) in seedUsers)
        {
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, role);
                db.UserWarehouses.AddRange(warehouseIds.Select(id => new UserWarehouse { UserId = user.Id, WarehouseId = id }));
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedChartOfAccountsAsync(ApplicationDbContext db, IAccountingService accountingService)
    {
        var accounts = new[]
        {
            new Account { Code = SystemAccountCodes.Cash, Name = "Cash and Bank", Type = AccountType.Asset, IsSystemAccount = true },
            new Account { Code = SystemAccountCodes.Inventory, Name = "Inventory", Type = AccountType.Asset, IsSystemAccount = true },
            new Account { Code = SystemAccountCodes.AccountsReceivable, Name = "Accounts Receivable", Type = AccountType.Asset, IsSystemAccount = true },
            new Account { Code = SystemAccountCodes.AccountsPayable, Name = "Accounts Payable", Type = AccountType.Liability, IsSystemAccount = true },
            new Account { Code = SystemAccountCodes.OwnersEquity, Name = "Owner's Equity", Type = AccountType.Equity, IsSystemAccount = true },
            new Account { Code = SystemAccountCodes.SalesRevenue, Name = "Sales Revenue", Type = AccountType.Income, IsSystemAccount = true },
            new Account { Code = SystemAccountCodes.CostOfGoodsSold, Name = "Cost of Goods Sold", Type = AccountType.Expense, IsSystemAccount = true },
        };

        db.Accounts.AddRange(accounts);
        await db.SaveChangesAsync();
    }

    private static async Task<List<Category>> SeedCategoriesAsync(ApplicationDbContext db)
    {
        var categories = new[]
        {
            new Category { Name = "Electronics", Description = "Consumer electronics and accessories" },
            new Category { Name = "Groceries", Description = "Packaged food and household staples" },
            new Category { Name = "Stationery", Description = "Office and school supplies" },
            new Category { Name = "Furniture", Description = "Office and home furniture" },
            new Category { Name = "Apparel", Description = "Clothing and footwear" },
        };

        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();
        return categories.ToList();
    }

    private static async Task<List<UnitOfMeasure>> SeedUnitsAsync(ApplicationDbContext db)
    {
        var units = new[]
        {
            new UnitOfMeasure { Name = "Piece", Symbol = "pc" },
            new UnitOfMeasure { Name = "Box", Symbol = "bx" },
            new UnitOfMeasure { Name = "Kilogram", Symbol = "kg" },
            new UnitOfMeasure { Name = "Litre", Symbol = "L" },
            new UnitOfMeasure { Name = "Dozen", Symbol = "dz" },
        };

        db.UnitOfMeasures.AddRange(units);
        await db.SaveChangesAsync();
        return units.ToList();
    }

    private static async Task<List<Product>> SeedProductsAsync(ApplicationDbContext db, List<Category> categories, List<UnitOfMeasure> units)
    {
        Category Cat(string name) => categories.First(c => c.Name == name);
        UnitOfMeasure Unit(string symbol) => units.First(u => u.Symbol == symbol);

        var products = new[]
        {
            new Product { Sku = "ELEC-001", Name = "Wireless Mouse", CategoryId = Cat("Electronics").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 8.50m, SalePrice = 15.99m, ReorderLevel = 25 },
            new Product { Sku = "ELEC-002", Name = "USB-C Charging Cable", CategoryId = Cat("Electronics").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 3.20m, SalePrice = 7.99m, ReorderLevel = 40 },
            new Product { Sku = "ELEC-003", Name = "Bluetooth Speaker", CategoryId = Cat("Electronics").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 22.00m, SalePrice = 39.99m, ReorderLevel = 15 },
            new Product { Sku = "GRO-001", Name = "Basmati Rice 5kg", CategoryId = Cat("Groceries").Id, UnitOfMeasureId = Unit("kg").Id, CostPrice = 6.00m, SalePrice = 9.50m, ReorderLevel = 50 },
            new Product { Sku = "GRO-002", Name = "Cooking Oil 1L", CategoryId = Cat("Groceries").Id, UnitOfMeasureId = Unit("L").Id, CostPrice = 2.10m, SalePrice = 3.75m, ReorderLevel = 60 },
            new Product { Sku = "STA-001", Name = "A4 Paper Ream", CategoryId = Cat("Stationery").Id, UnitOfMeasureId = Unit("bx").Id, CostPrice = 3.50m, SalePrice = 5.99m, ReorderLevel = 30 },
            new Product { Sku = "STA-002", Name = "Ballpoint Pens (Dozen)", CategoryId = Cat("Stationery").Id, UnitOfMeasureId = Unit("dz").Id, CostPrice = 1.80m, SalePrice = 3.49m, ReorderLevel = 20 },
            new Product { Sku = "FUR-001", Name = "Office Chair", CategoryId = Cat("Furniture").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 45.00m, SalePrice = 89.99m, ReorderLevel = 8 },
            new Product { Sku = "FUR-002", Name = "Study Desk", CategoryId = Cat("Furniture").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 60.00m, SalePrice = 119.99m, ReorderLevel = 6 },
            new Product { Sku = "APP-001", Name = "Cotton T-Shirt", CategoryId = Cat("Apparel").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 4.00m, SalePrice = 9.99m, ReorderLevel = 40 },
            new Product { Sku = "APP-002", Name = "Denim Jeans", CategoryId = Cat("Apparel").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 12.00m, SalePrice = 24.99m, ReorderLevel = 20 },
            new Product { Sku = "APP-003", Name = "Running Shoes", CategoryId = Cat("Apparel").Id, UnitOfMeasureId = Unit("pc").Id, CostPrice = 18.00m, SalePrice = 34.99m, ReorderLevel = 15 },
        };

        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        return products.ToList();
    }

    private static async Task<List<Supplier>> SeedSuppliersAsync(ApplicationDbContext db)
    {
        var suppliers = new[]
        {
            new Supplier { Name = "Global Traders Ltd", ContactPerson = "Karim Uddin", Phone = "+880-1711-000001", Email = "sales@globaltraders.example" },
            new Supplier { Name = "Northline Distributors", ContactPerson = "Farhana Rahman", Phone = "+880-1711-000002", Email = "info@northline.example" },
            new Supplier { Name = "Prime Wholesale Co", ContactPerson = "Imran Hossain", Phone = "+880-1711-000003", Email = "orders@primewholesale.example" },
            new Supplier { Name = "Metro Supply House", ContactPerson = "Nadia Islam", Phone = "+880-1711-000004", Email = "contact@metrosupply.example" },
        };

        db.Suppliers.AddRange(suppliers);
        await db.SaveChangesAsync();
        return suppliers.ToList();
    }

    private static async Task<List<Customer>> SeedCustomersAsync(ApplicationDbContext db)
    {
        var customers = new[]
        {
            new Customer { Name = "Rahman Retail Store", ContactPerson = "Abdur Rahman", Phone = "+880-1811-000001", Email = "rahman.retail@example.com", Address = "45 New Market Road, Dhanmondi, Dhaka-1205" },
            new Customer { Name = "City Mart", ContactPerson = "Sultana Begum", Phone = "+880-1811-000002", Email = "citymart@example.com", Address = "12 Gulshan Avenue, Gulshan-1, Dhaka-1212" },
            new Customer { Name = "Greenfield Supermart", ContactPerson = "Rafiq Ahmed", Phone = "+880-1811-000003", Email = "greenfield@example.com", Address = "78 Agrabad Commercial Area, Chattogram-4100" },
            new Customer { Name = "Downtown Traders", ContactPerson = "Mitu Akter", Phone = "+880-1811-000004", Email = "downtown@example.com", Address = "23 Zindabazar, Sylhet-3100" },
            new Customer { Name = "Sunrise Enterprise", ContactPerson = "Jahangir Alam", Phone = "+880-1811-000005", Email = "sunrise@example.com", Address = "9 Shaheb Bazar, Rajshahi-6100" },
            new Customer { Name = "Lakeview Stores", ContactPerson = "Nasrin Sultana", Phone = "+880-1811-000006", Email = "lakeview@example.com", Address = "56 Nawab Road, Khulna-9100" },
        };

        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
        return customers.ToList();
    }

    private static async Task SeedOpeningBalanceAsync(ApplicationDbContext db, IAccountingService accountingService)
    {
        var cash = await db.Accounts.FirstAsync(a => a.Code == SystemAccountCodes.Cash);
        var equity = await db.Accounts.FirstAsync(a => a.Code == SystemAccountCodes.OwnersEquity);

        db.JournalEntries.Add(new JournalEntry
        {
            EntryNumber = "JE-000001",
            Date = DateTime.UtcNow.Date.AddDays(-91),
            Description = "Opening capital investment",
            Source = JournalSource.Manual,
            SourceReference = "OPENING-BALANCE",
            Lines =
            [
                new JournalEntryLine { AccountId = cash.Id, Debit = 50000m, Credit = 0, Memo = "Opening cash balance" },
                new JournalEntryLine { AccountId = equity.Id, Debit = 0, Credit = 50000m, Memo = "Owner's opening capital" }
            ]
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedPurchaseHistoryAsync(
        ApplicationDbContext db, IStockService stockService, IAccountingService accountingService,
        List<Product> products, List<Supplier> suppliers, List<Warehouse> warehouses)
    {
        var random = new Random(42);
        var invoiceCount = 0;

        foreach (var daysAgo in new[] { 85, 55, 25 })
        {
            var date = DateTime.UtcNow.Date.AddDays(-daysAgo);

            // One restock invoice per warehouse so both locations carry stock.
            foreach (var warehouse in warehouses)
            {
                var supplier = suppliers[random.Next(suppliers.Count)];

                var invoice = new PurchaseInvoice
                {
                    InvoiceNumber = $"PINV-{++invoiceCount:D6}",
                    SupplierId = supplier.Id,
                    WarehouseId = warehouse.Id,
                    Date = date,
                    Status = DocumentStatus.Posted,
                    CreatedBy = "seed"
                };

                foreach (var product in products)
                {
                    var qty = random.Next(60, 160);
                    invoice.Items.Add(new PurchaseInvoiceItem
                    {
                        ProductId = product.Id,
                        Quantity = qty,
                        UnitPrice = product.CostPrice
                    });
                }

                db.PurchaseInvoices.Add(invoice);

                foreach (var item in invoice.Items)
                {
                    await stockService.ReceiveStockAsync(item.ProductId, warehouse.Id, item.Quantity, invoice.InvoiceNumber, notes: "Seed data restock");
                }

                await accountingService.PostPurchaseInvoiceAsync(invoice);
                await db.SaveChangesAsync();
            }
        }
    }

    private static async Task SeedSalesHistoryAsync(
        ApplicationDbContext db, IStockService stockService, IAccountingService accountingService,
        List<Product> products, List<Customer> customers, List<Warehouse> warehouses)
    {
        var random = new Random(7);
        var invoiceCount = 0;

        for (var daysAgo = 84; daysAgo >= 1; daysAgo -= 3)
        {
            var date = DateTime.UtcNow.Date.AddDays(-daysAgo);
            var invoicesToday = random.Next(1, 3);

            for (var n = 0; n < invoicesToday; n++)
            {
                var customer = customers[random.Next(customers.Count)];
                var warehouse = warehouses[random.Next(warehouses.Count)];
                var invoice = new SalesInvoice
                {
                    InvoiceNumber = $"SINV-{++invoiceCount:D6}",
                    CustomerId = customer.Id,
                    WarehouseId = warehouse.Id,
                    Date = date,
                    Status = DocumentStatus.Posted,
                    CreatedBy = "seed"
                };

                var lineCount = random.Next(2, 5);
                var chosenProducts = products.OrderBy(_ => random.Next()).Take(lineCount);

                foreach (var product in chosenProducts)
                {
                    // slight upward demand trend for the first few products to give forecasting a real signal
                    var trendBoost = product == products[0] || product == products[1] ? (84 - daysAgo) / 20 : 0;
                    var qty = random.Next(1, 9) + trendBoost;

                    invoice.Items.Add(new SalesInvoiceItem
                    {
                        ProductId = product.Id,
                        Quantity = qty,
                        UnitPrice = product.SalePrice,
                        UnitCost = product.CostPrice
                    });
                }

                db.SalesInvoices.Add(invoice);

                foreach (var item in invoice.Items)
                {
                    await stockService.IssueStockAsync(item.ProductId, warehouse.Id, item.Quantity, invoice.InvoiceNumber, notes: "Seed data sale");
                }

                await accountingService.PostSalesInvoiceAsync(invoice);
                await db.SaveChangesAsync();
            }
        }
    }

    private static async Task SeedSamplePaymentsAsync(ApplicationDbContext db, IAccountingService accountingService)
    {
        var firstPurchase = await db.PurchaseInvoices.OrderBy(p => p.Id).FirstOrDefaultAsync();
        var firstSale = await db.SalesInvoices.OrderBy(s => s.Id).FirstOrDefaultAsync();

        if (firstPurchase is not null)
        {
            var payment = new Payment
            {
                PaymentNumber = "PAY-000001",
                Direction = PaymentDirection.Out,
                Date = firstPurchase.Date.AddDays(5),
                Amount = Math.Round(firstPurchase.Items.Sum(i => i.Quantity * i.UnitPrice) * 0.5m, 2),
                PurchaseInvoiceId = firstPurchase.Id,
                Notes = "Partial settlement",
                CreatedBy = "seed"
            };
            db.Payments.Add(payment);
            await accountingService.PostPaymentAsync(payment);
            await db.SaveChangesAsync();
        }

        if (firstSale is not null)
        {
            var receipt = new Payment
            {
                PaymentNumber = "PAY-000002",
                Direction = PaymentDirection.In,
                Date = firstSale.Date.AddDays(3),
                Amount = firstSale.Items.Sum(i => i.Quantity * i.UnitPrice),
                SalesInvoiceId = firstSale.Id,
                Notes = "Full settlement",
                CreatedBy = "seed"
            };
            db.Payments.Add(receipt);
            await accountingService.PostPaymentAsync(receipt);
            await db.SaveChangesAsync();
        }
    }
}
