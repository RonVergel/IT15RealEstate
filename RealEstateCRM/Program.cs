using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Services;
using RealEstateCRM.Services.Notifications;

// Relax legacy timestamp behavior to avoid hard failures on Unspecified kinds
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
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
builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration.GetSection("Mailjet"));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Ensure security stamp is validated every request so locked users are signed out immediately
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.Zero;
});

var app = builder.Build();

// Database initialization (unchanged)...
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
        try
        {
            var context = services.GetRequiredService<AppDbContext>();
            // Create database if needed (no migrations)
            await context.Database.EnsureCreatedAsync();
            // Ensure Notifications table exists when database already existed
            try
            {
                var sql = @"
CREATE TABLE IF NOT EXISTS ""Notifications"" (
    ""Id"" SERIAL PRIMARY KEY,
    ""RecipientUserId"" TEXT NULL,
    ""ActorUserId"" TEXT NULL,
    ""Message"" VARCHAR(512) NOT NULL,
    ""LinkUrl"" VARCHAR(256) NULL,
    ""IsRead"" BOOLEAN NOT NULL DEFAULT FALSE,
    ""CreatedAtUtc"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""Type"" VARCHAR(64) NULL
);
CREATE INDEX IF NOT EXISTS idx_notifications_recipient_read ON ""Notifications"" (""RecipientUserId"", ""IsRead"");
-- Offers table
CREATE TABLE IF NOT EXISTS ""Offers"" (
    ""Id"" SERIAL PRIMARY KEY,
    ""DealId"" INT NOT NULL,
    ""Amount"" NUMERIC(18,2) NOT NULL,
    ""Status"" VARCHAR(32) NOT NULL DEFAULT 'Proposed',
    ""FinancingType"" VARCHAR(64) NULL,
    ""EarnestMoney"" NUMERIC(18,2) NULL,
    ""CloseDate"" DATE NULL,
    ""Notes"" VARCHAR(512) NULL,
    ""CreatedAtUtc"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""UpdatedAtUtc"" TIMESTAMPTZ NULL,
    CONSTRAINT fk_offers_deal FOREIGN KEY (""DealId"") REFERENCES ""Deals"" (""Id"") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_offers_dealid ON ""Offers"" (""DealId"");

-- Deadlines table
CREATE TABLE IF NOT EXISTS ""DealDeadlines"" (
    ""Id"" SERIAL PRIMARY KEY,
    ""DealId"" INT NOT NULL,
    ""Type"" VARCHAR(64) NOT NULL,
    ""DueDate"" DATE NOT NULL,
    ""CreatedAtUtc"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""CompletedAtUtc"" TIMESTAMPTZ NULL,
    ""Notes"" VARCHAR(512) NULL,
    CONSTRAINT fk_deadlines_deal FOREIGN KEY (""DealId"") REFERENCES ""Deals"" (""Id"") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_deadlines_dealid ON ""DealDeadlines"" (""DealId"");
";
                await context.Database.ExecuteSqlRawAsync(sql);
            }
            catch { }

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

        var roles = new[] { "Broker", "Agent" };
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        var brokerEmail = "maevenr81@gmail.com";
        var brokerPassword = "User@123";

        var existingBroker = await userManager.FindByEmailAsync(brokerEmail);
        if (existingBroker == null)
        {
            var brokerUser = new IdentityUser
            {
                UserName = brokerEmail,
                Email = brokerEmail,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(brokerUser, brokerPassword);
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(brokerUser, "Broker");
            }
        }
        else
        {
            if (!await userManager.IsInRoleAsync(existingBroker, "Broker"))
            {
                await userManager.AddToRoleAsync(existingBroker, "Broker");
            }
            if (!existingBroker.EmailConfirmed)
            {
                existingBroker.EmailConfirmed = true;
                await userManager.UpdateAsync(existingBroker);
            }
        }
    }
    catch (Exception ex)
    {
        var logger2 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger2.LogError(ex, "An error occurred while initializing the database.");
        throw;
    }
}

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

app.UseAuthentication();
app.UseAuthorization();

// If a logged-in user becomes locked, sign them out and send to login
app.Use(async (context, next) =>
{
    if (context.User?.Identity?.IsAuthenticated == true)
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
        var signInManager = context.RequestServices.GetRequiredService<SignInManager<IdentityUser>>();
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
