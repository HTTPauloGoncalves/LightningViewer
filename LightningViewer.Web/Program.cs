using LightningViewer.Web.Data;
using LightningViewer.Web.Infrastructure;
using LightningViewer.Web.Repositories;
using LightningViewer.Web.Services;
using LightningViewer.Web.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── MVC + Cache ────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

// ── Database ───────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── HTTP Client for EUMETSAT API ───────────────────────────────────────────────
builder.Services.AddHttpClient("EumetSat", client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
    client.DefaultRequestHeaders.Add("User-Agent", "LightningViewer/1.0");
});

// EumetSatApiClient is singleton (manages its own OAuth token cache)
builder.Services.AddSingleton<EumetSatApiClient>();

// ── Repositories ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUnidadeRepository, UnidadeRepository>();
builder.Services.AddScoped<ILightningRepository, LightningRepository>();

// ── Services ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUnidadeService, UnidadeService>();
builder.Services.AddScoped<ILightningService, LightningService>();

// ── Background Ingestor Worker ─────────────────────────────────────────────────
builder.Services.AddHostedService<EumetSatIngestorWorker>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply EF Core migrations and seed units on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedUnidadesAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
