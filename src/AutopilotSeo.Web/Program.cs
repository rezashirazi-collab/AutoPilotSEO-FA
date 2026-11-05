using AutopilotSeo.Core;
using AutopilotSeo.Web.Crawl;
using AutopilotSeo.Web.Gsc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
    builder.Configuration.GetConnectionString("Default"),
    sql => sql.UseCompatibilityLevel(150))); // dev server is SQL Server 2019

builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/account/login";
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
});

// Refresh tokens are encrypted with these keys: losing them means every site must reconnect Google.
var keys = builder.Services.AddDataProtection().SetApplicationName("AutopilotSeo")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
if (OperatingSystem.IsWindows()) keys.ProtectKeysWithDpapi();

builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection("Google"));
builder.Services.AddHttpClient<GoogleApi>(c => c.Timeout = TimeSpan.FromMinutes(2));
builder.Services.AddScoped<GscSyncer>();
builder.Services.AddSingleton<GscSyncSignal>();
builder.Services.AddHostedService<GscSyncWorker>();

builder.Services.AddHttpClient<Crawler>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(20);
        c.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.UserAgent);
    })
    .ConfigurePrimaryHttpMessageHandler(Crawler.Handler);
builder.Services.AddHostedService<CrawlWorker>();

builder.Services.AddRouting(o => o.LowercaseUrls = true);
builder.Services.AddRazorPages(o => o.Conventions.AuthorizeFolder("/Panel"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
GscEndpoints.Map(app);

app.Run();
