using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Moq;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Services.Saas;
using WebApplication1.Services.Tenancy;

namespace WebApplication1.Tests;

public class PlatformBrandingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ims-branding-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationDbContext _db;
    private readonly PlatformBrandingService _branding;

    private sealed class NoTenant : ITenantContext
    {
        public int TenantId => 0;
        public bool IsFilterBypassed => true;
        public IDisposable UseTenant(int t) => new Noop();
        public IDisposable BypassFilters() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    public PlatformBrandingTests()
    {
        Directory.CreateDirectory(_root);
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new NoTenant());
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(_root);
        env.SetupGet(e => e.ContentRootFileProvider).Returns(new NullFileProvider());
        _branding = new PlatformBrandingService(new PlatformSettingsService(_db, new MemoryCache(new MemoryCacheOptions())), env.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static byte[] Png(int extra = 0) => [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. new byte[8 + extra]];
    private static byte[] Jpeg() => [.. new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, .. new byte[16]];

    private Task<string?> Save(byte[] bytes) => _branding.SaveLogoAsync(new MemoryStream(bytes), bytes.Length);

    [Fact]
    public async Task Without_an_uploaded_logo_there_is_none_and_the_name_defaults_to_the_platform_name()
    {
        Assert.Null(await _branding.GetLogoAsync());
        Assert.Equal("IMS Cloud", await _branding.GetNameAsync());
    }

    [Fact]
    public async Task Png_and_jpeg_are_accepted_and_the_latest_replaces_the_previous()
    {
        Assert.Null(await Save(Png()));
        Assert.Equal("image/png", (await _branding.GetLogoAsync())!.ContentType);

        Assert.Null(await Save(Jpeg()));
        var logo = (await _branding.GetLogoAsync())!;
        Assert.Equal("image/jpeg", logo.ContentType);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "App_Data", "Branding")));   // the PNG is gone

        await _branding.RemoveLogoAsync();
        Assert.Null(await _branding.GetLogoAsync());
    }

    [Theory]
    [InlineData("script")]
    [InlineData("gif")]
    [InlineData("svg")]
    [InlineData("empty")]
    [InlineData("toobig")]
    public async Task Anything_that_is_not_a_small_png_or_jpeg_is_refused(string kind)
    {
        var bytes = kind switch
        {
            "script" => "<script>alert(1)</script>"u8.ToArray(),
            "gif" => "GIF89a"u8.ToArray().Concat(new byte[20]).ToArray(),
            "svg" => "<svg xmlns='http://www.w3.org/2000/svg'><script>1</script></svg>"u8.ToArray(),
            "empty" => [],
            _ => Png(PlatformBrandingService.MaxLogoBytes)
        };

        Assert.NotNull(await Save(bytes));
        Assert.Null(await _branding.GetLogoAsync());
    }

    [Fact]
    public async Task The_declared_content_type_is_never_trusted_only_the_file_signature()
    {
        // A "png" that is really text: rejected because the bytes do not start with the PNG signature.
        Assert.NotNull(await Save("not really a png"u8.ToArray()));
    }

    [Fact]
    public void Platform_sign_in_and_logo_are_anonymous_and_separate_from_the_tenant_sign_in()
    {
        Assert.NotEmpty(typeof(PlatformAccountController).GetCustomAttributes<AllowAnonymousAttribute>());
        var login = typeof(PlatformAccountController).GetMethods().Where(m => m.Name == "Login").ToList();
        Assert.Equal(2, login.Count);
        Assert.Contains(login, m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Any(a => a.Template == "/Platform/Login"));
        Assert.Contains(login, m => m.GetCustomAttributes<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Any());
        Assert.Contains(typeof(PlatformAccountController).GetMethod("Logo")!.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpGetAttribute>(), a => a.Template == "/Platform/Logo");

        // The logo of a business can only be fetched for that business, never as a default for everyone.
        var tenantLogo = typeof(CompanySettingsController).GetMethod("Logo")!;
        Assert.Contains(tenantLogo.GetParameters(), p => p.Name == "tenant");
    }
}

public class ImageSignatureTests
{
    [Fact]
    public void Images_are_recognised_by_content_and_everything_else_is_rejected()
    {
        Assert.Equal("image/png", ImageSignature.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("image/jpeg", ImageSignature.Detect([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/gif", ImageSignature.Detect("GIF89a...."u8));
        Assert.Equal("image/gif", ImageSignature.Detect("GIF87a...."u8));
        Assert.Equal("image/bmp", ImageSignature.Detect("BM...."u8));
        Assert.Null(ImageSignature.Detect("<svg xmlns='http://www.w3.org/2000/svg'/>"u8));
        Assert.Null(ImageSignature.Detect("<html><script>alert(1)</script></html>"u8));
        Assert.Null(ImageSignature.Detect([]));
    }
}
