using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Services;
using RealEstateCRM.Services.Notifications;
using RealEstateCRM.Models;

// Relax legacy timestamp behavior to avoid hard failures on Unspecified kinds
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Accept QuestPDF Community license for PDF generation
QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Check for Render's DATABASE_URL environment variable for production
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    var databaseUri = new Uri(databaseUrl);
    var userInfo = databaseUri.UserInfo.Split(':');
    var dbHost = databaseUri.Host;
    var dbPort = databaseUri.Port;
    var dbUser = userInfo[0];
    var dbPass = userInfo[1];
    var dbName = databaseUri.LocalPath.TrimStart('/');

    // Build the connection string for Npgsql, including SSL settings required by Render
    connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass};SslMode=Require;Trust Server Certificate=true;";
}

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found or DATABASE_URL is not set.");
}


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    options.Password.RequiredUniqueChars = 1;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    options.User.RequireUniqueEmail = true;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<RealEstateCRM.Services.Logging.IAppLogger, RealEstateCRM.Services.Logging.AppLogger>();
builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration.GetSection("Mailjet"));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<RealEstateCRM.Services.Logging.ActionAuditFilter>();
});
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<RealEstateCRM.Services.ContractPdfGenerator>();

// Ensure security stamp is validated every request so locked users are signed out immediately
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.Zero;
});

var app = builder.Build();



if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Minimal error logging middleware to capture unhandled exceptions to SystemLog
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        try
        {
            var logger = ctx.RequestServices.GetService<RealEstateCRM.Services.Logging.IAppLogger>();
            if (logger != null)
            {
                await logger.LogAsync("ERROR", "Unhandled", ex.Message, new { path = ctx.Request.Path.Value, stack = ex.ToString() });
            }
        }
        catch { }
        throw;
    }
});

// Seed the database with initial data (roles and broker account)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        await RealEstateCRM.Data.SeedData.Initialize(services, configuration);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.UseAuthentication();
app.UseAuthorization();

// If a logged-in user becomes locked, sign them out and send to login
app.Use(async (context, next) =>
{
    if (context.User?.Identity?.IsAuthenticated == true)
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user != null && await userManager.IsLockedOutAsync(user))
        {
            await signInManager.SignOutAsync();
            context.Response.Redirect("/Identity/Account/Login");
            return;
        }
    }
    await next();
});

// Handle root requests: send unauthenticated users to login, authenticated to dashboard
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" || string.Equals(context.Request.Path, string.Empty, StringComparison.Ordinal))
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            context.Response.Redirect("/Dashboard/Index");
        }
        else
        {
            context.Response.Redirect("/Identity/Account/Login");
        }
        return;
    }
    await next();
});

// Default MVC route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();

