using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

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

var app = builder.Build();

// Database initialization (unchanged)...
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

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

// Always send root requests to the login page so the app lands on login on start.
// This intentionally redirects only when the request path is exactly "/" or empty.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" || string.Equals(context.Request.Path, string.Empty, StringComparison.Ordinal))
    {
        // Prevent redirect loop: if already on the login page, continue.
        if (!context.Request.Path.StartsWithSegments("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/Identity/Account/Login");
            return;
        }
    }

    await next();
});

// Default MVC route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();