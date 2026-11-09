using System;
using System.IO;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GolfStatsApp.Server.Data;
using GolfStatsApp.Server.Services;
using GolfStatsApp.Server.PgaTour;
using GolfStatsApp.Server.Services.Prediction;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<GolfSimulationService>();
builder.Services.AddScoped<PlayerDataService>();
builder.Services.AddPgaTourClient(builder.Configuration);
builder.Services.AddScoped<TournamentSyncService>();
var predictionOptions = builder.Configuration.GetSection(PredictiveModelingOptions.SectionName).Get<PredictiveModelingOptions>() ?? new PredictiveModelingOptions();
builder.Services.AddSingleton(predictionOptions);
builder.Services.AddSingleton<CutRuleResolver>();
builder.Services.AddSingleton<MonteCarloEngine>();
builder.Services.AddSingleton<SimulationConcurrencyLimiter>();
builder.Services.AddSingleton<IPgaDataSource, PgaTourDataSource>();
builder.Services.AddScoped<IFallbackPlayerSource, PlayerDataFallbackSource>();
builder.Services.AddScoped<GolferSearchService>();
builder.Services.AddScoped<ModelInputService>();
builder.Services.AddScoped<PredictionService>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<TournamentSyncBackgroundService>();

// \"SqlServer\" (default) or \"Sqlite\" (used by the test suite). Read at resolve time so test overrides apply.
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var connectionString = config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    if ((config["DatabaseProvider"] ?? "SqlServer").Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        const string sqlitePrefix = "Data Source=";
        if (connectionString.StartsWith(sqlitePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var dbPath = Path.GetFullPath(connectionString[sqlitePrefix.Length..], env.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            connectionString = sqlitePrefix + dbPath;
        }
        o.UseSqlite(connectionString);
    }
    else
    {
        o.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(3);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });
    }
});
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(o =>
{
    o.ApplicationCookie?.Configure(c =>
    {
        c.Cookie.Name = "golfstats.auth";
        c.Cookie.HttpOnly = true;
        c.Cookie.SameSite = SameSiteMode.Lax;
        c.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        c.ExpireTimeSpan = TimeSpan.FromDays(14);
        c.SlidingExpiration = true;
        // APIs answer 401/403 instead of redirecting to a login page.
        c.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return System.Threading.Tasks.Task.CompletedTask; };
        c.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return System.Threading.Tasks.Task.CompletedTask; };
    });
});
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.InitializeAsync(db, app.Environment.ContentRootPath);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // Blazor WASM debugging proxy is opt-in (set "EnableWasmDebugging": "true", e.g. via the
    // "Blazor WASM debugging" launch profile). When enabled, Visual Studio attaches the Managed Wasm Debugger,
    // which can fail with "fatal error loading the metadata for System.Private.CoreLib.dll".
    if (app.Configuration.GetValue<bool>("EnableWasmDebugging"))
        app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
