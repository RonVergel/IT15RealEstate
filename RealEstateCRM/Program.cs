using QuestPDF.Infrastructure;
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

// Accept QuestPDF Community license for PDF generation
QuestPDF.Settings.License = LicenseType.Community;

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

// Database initialization
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        // Create database if needed (no migrations)
        await context.Database.EnsureCreatedAsync();
        
        // Ensure tables exist - Fixed SQL syntax
        try
        {
            var sql = """
                CREATE TABLE IF NOT EXISTS "Notifications" (
                    "Id" SERIAL PRIMARY KEY,
                    "RecipientUserId" TEXT NULL,
                    "ActorUserId" TEXT NULL,
                    "Message" VARCHAR(512) NOT NULL,
                    "LinkUrl" VARCHAR(256) NULL,
                    "IsRead" BOOLEAN NOT NULL DEFAULT FALSE,
                    "CreatedAtUtc" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    "Type" VARCHAR(64) NULL
                );
                CREATE INDEX IF NOT EXISTS idx_notifications_recipient_read ON "Notifications" ("RecipientUserId", "IsRead");
                
                -- Offers table
                CREATE TABLE IF NOT EXISTS "Offers" (
                    "Id" SERIAL PRIMARY KEY,
                    "DealId" INT NOT NULL,
                    "Amount" NUMERIC(18,2) NOT NULL,
                    "Status" VARCHAR(32) NOT NULL DEFAULT 'Proposed',
                    "FinancingType" VARCHAR(64) NULL,
                    "EarnestMoney" NUMERIC(18,2) NULL,
                    "CloseDate" DATE NULL,
                    "Notes" VARCHAR(512) NULL,
                    "CreatedAtUtc" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    "UpdatedAtUtc" TIMESTAMPTZ NULL,
                    CONSTRAINT fk_offers_deal FOREIGN KEY ("DealId") REFERENCES "Deals" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_offers_dealid ON "Offers" ("DealId");

                -- Deadlines table
                CREATE TABLE IF NOT EXISTS "DealDeadlines" (
                    "Id" SERIAL PRIMARY KEY,
                    "DealId" INT NOT NULL,
                    "Type" VARCHAR(64) NOT NULL,
                    "DueDate" DATE NOT NULL,
                    "CreatedAtUtc" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    "CompletedAtUtc" TIMESTAMPTZ NULL,
                    "Notes" VARCHAR(512) NULL,
                    CONSTRAINT fk_deadlines_deal FOREIGN KEY ("DealId") REFERENCES "Deals" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_deadlines_dealid ON "DealDeadlines" ("DealId");

                -- AgencySettings table (single-row configuration)
                CREATE TABLE IF NOT EXISTS "AgencySettings" (
                    "Id" SERIAL PRIMARY KEY,
                    "BrokerCommissionPercent" NUMERIC(5,2) NOT NULL DEFAULT 10.00,
                    "AgentCommissionPercent" NUMERIC(5,2) NOT NULL DEFAULT 5.00,
                    "UpdatedAtUtc" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    "MonthlyRevenueGoal" NUMERIC(18,2) NOT NULL DEFAULT 0.00,
                    "LastNotifiedAchievedPeriod" INT NULL,
                    "LastNotifiedBehindPeriod" INT NULL
                );
                
                -- Seed a default row if table is empty
                INSERT INTO "AgencySettings" ("BrokerCommissionPercent", "AgentCommissionPercent")
                SELECT 10.00, 5.00
                WHERE NOT EXISTS (SELECT 1 FROM "AgencySettings");

                -- Add closed tracking to Deals for revenue analytics
                ALTER TABLE "Deals" ADD COLUMN IF NOT EXISTS "ClosedAtUtc" TIMESTAMPTZ NULL;
                ALTER TABLE "Deals" ADD COLUMN IF NOT EXISTS "ClosedByUserId" TEXT NULL;
                CREATE INDEX IF NOT EXISTS idx_deals_closedat ON "Deals" ("ClosedAtUtc");
                CREATE INDEX IF NOT EXISTS idx_deals_closedby ON "Deals" ("ClosedByUserId");

                -- Add new columns to AgencySettings if table existed before
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "MonthlyRevenueGoal" NUMERIC(18,2) NOT NULL DEFAULT 0.00;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "LastNotifiedAchievedPeriod" INT NULL;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "LastNotifiedBehindPeriod" INT NULL;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "InspectionDays" INT NOT NULL DEFAULT 7;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "AppraisalDays" INT NOT NULL DEFAULT 14;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "LoanCommitmentDays" INT NOT NULL DEFAULT 21;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "ClosingDays" INT NOT NULL DEFAULT 30;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "MaxActiveAssignmentsPerAgent" INT NOT NULL DEFAULT 5;
                ALTER TABLE "AgencySettings" ADD COLUMN IF NOT EXISTS "MaxDeclinesPerAgentPerMonth" INT NOT NULL DEFAULT 3;
                """;
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        catch { }

        // Contact table alterations - Fixed SQL syntax
        try
        {
            var contactSql = """
                ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "NextFollowUpUtc" TIMESTAMPTZ NULL;
                ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "FollowUpNotifiedUtc" TIMESTAMPTZ NULL;
                ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "ArchivedAtUtc" TIMESTAMPTZ NULL;
                """;
            await context.Database.ExecuteSqlRawAsync(contactSql);
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

// Dev-only maintenance endpoint to disable 2FA when recovery codes are lost
if (app.Environment.IsDevelopment())
{
    app.MapGet("/maintenance/disable-2fa", async (HttpContext ctx, UserManager<IdentityUser> userManager, IConfiguration config) =>
    {
        var secret = config["Maintenance:Secret"] ?? string.Empty;
        var provided = ctx.Request.Query["secret"].ToString();
        if (string.IsNullOrWhiteSpace(secret) || !string.Equals(secret, provided))
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Forbidden" });
            return;
        }
        var email = ctx.Request.Query["email"].ToString();
        if (string.IsNullOrWhiteSpace(email))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Missing email" });
            return;
        }
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "User not found" });
            return;
        }
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.UpdateAsync(user);
        await ctx.Response.WriteAsJsonAsync(new { ok = true, message = "2FA disabled and authenticator key reset. You can log in with password and re-enable 2FA." });
    });
}

// Default MVC route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();

