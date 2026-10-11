using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

public class DataAgreementTests
{
    private sealed class NoTenant : ITenantContext
    {
        public int TenantId => 0;
        public bool IsFilterBypassed => true;
        public IDisposable UseTenant(int t) => new Noop();
        public IDisposable BypassFilters() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new NoTenant());

    [Fact]
    public async Task Acceptance_is_recorded_once_per_version_and_tenant()
    {
        using var db = NewDb();
        var service = new DataAgreementService(db, new HttpContextAccessor());

        Assert.False(await service.HasAcceptedCurrentAsync(7));
        await service.AcceptAsync(7, "Portal", "u1", "owner@x.test");
        await db.SaveChangesAsync();
        await service.AcceptAsync(7, "ChoosePlan", "u1", "owner@x.test");   // already accepted: no second row
        await db.SaveChangesAsync();

        Assert.True(await service.HasAcceptedCurrentAsync(7));
        Assert.False(await service.HasAcceptedCurrentAsync(8));   // another tenant is unaffected
        Assert.Equal(1, await db.DataAgreementAcceptances.CountAsync());
        var latest = await service.LatestAsync(7);
        Assert.Equal(DataAgreement.Version, latest!.Version);
        Assert.Equal("Portal", latest.Context);
    }

    [Fact]
    public void Agreement_text_mentions_export_formats_and_retention()
    {
        var text = string.Join(" ", DataAgreement.Sections(45).Select(s => s.Heading + " " + s.Body));
        Assert.Contains("CSV, JSON or Excel", text);
        Assert.Contains("45 days", text);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(true, "true")]
    [InlineData(12.50, "12.5")]
    public void Export_values_are_written_in_invariant_text(object? value, string expected) =>
        Assert.Equal(expected, DataExportService.Text(value));
}
