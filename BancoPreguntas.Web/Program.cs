using BancoPreguntas.Data.Identity;
using BancoPreguntas.Data.Repositories;
using BancoPreguntas.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;

// ── Identity (EF Core) ──────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connStr));
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ── Blazor + MudBlazor ──────────────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

// ── Repositorios Dapper ─────────────────────────────────────────────────────
builder.Services.AddScoped(_ => new CuadernilloRepository(connStr));
builder.Services.AddScoped(_ => new IntentoRepository(connStr));
builder.Services.AddScoped(_ => new AdminRepository(connStr));
builder.Services.AddScoped(_ => new EstadisticasRepository(connStr));

// ── Servicios ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<ExamenService>();
//  CORRECTO: .NET se encarga de inyectar el AdminRepository automáticamente
builder.Services.AddScoped<ImportadorService>();

var app = builder.Build();

// ── Seed: crear rol Admin y primer usuario ──────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var roleMgr     = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr     = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    db.Database.EnsureCreated();

    if (!await roleMgr.RoleExistsAsync("Admin"))
        await roleMgr.CreateAsync(new IdentityRole("Admin"));

    var cfg   = app.Configuration.GetSection("AdminInicial");
    var email = cfg["Email"]    ?? "admin@bancodepreguntas.pe";
    var pass  = cfg["Password"] ?? "Admin1234!";

    if (await userMgr.FindByEmailAsync(email) is null)
    {
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var r    = await userMgr.CreateAsync(user, pass);
        if (r.Succeeded) await userMgr.AddToRoleAsync(user, "Admin");
    }
}

// ── Pipeline ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
