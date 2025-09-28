using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Models;

namespace RealEstateCRM.Data
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            var context = serviceProvider.GetRequiredService<AppDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Ensure the database is created and migrations are applied
            await context.Database.MigrateAsync();

            // Seed roles and the initial broker account
            await SeedRolesAndBrokerAsync(userManager, roleManager, configuration);
        }

        private static async Task SeedRolesAndBrokerAsync(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IConfiguration configuration)
        {
            // Define role names
            string[] roleNames = { "Broker", "Agent" };

            // Create roles if they don't exist
            foreach (var roleName in roleNames)
            {
                var roleExist = await roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Get broker credentials from configuration
            var brokerEmail = configuration["BrokerCredentials:Email"];
            var brokerPassword = configuration["BrokerCredentials:Password"];

            if (string.IsNullOrEmpty(brokerEmail) || string.IsNullOrEmpty(brokerPassword))
            {
                // If credentials are not in config, do not proceed with user creation.
                // This is a safe-guard for production.
                return;
            }

            // Check if the broker user already exists
            var brokerUser = await userManager.FindByEmailAsync(brokerEmail);
            if (brokerUser == null)
            {
                // Create the broker user
                brokerUser = new IdentityUser
                {
                    UserName = brokerEmail,
                    Email = brokerEmail,
                    EmailConfirmed = true // Confirm email immediately for seeding
                };

                var result = await userManager.CreateAsync(brokerUser, brokerPassword);
                if (result.Succeeded)
                {
                    // Assign the "Broker" role
                    await userManager.AddToRoleAsync(brokerUser, "Broker");
                }
            }
        }
    }
}