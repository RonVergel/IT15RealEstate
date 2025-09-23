using System;
using System.Collections.Generic;
using System.Linq;
using RealEstateCRM.Models;
using Microsoft.Extensions.Logging;

namespace RealEstateCRM.Data
{
    /// <summary>
    /// Helper to seed Contacts table with sample data.
    /// Produces up to `targetTotal` contacts and constrains DateCreated
    /// to between 2024-01-01 and 2025-09-30 (inclusive).
    /// </summary>
    public static class SeedContacts
    {
        public static void EnsureSeeded(AppDbContext db, ILogger logger, int targetTotal = 30)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            try
            {
                var existingCount = db.Contacts.Count();
                if (existingCount >= targetTotal)
                {
                    logger.LogInformation("Contacts seeding skipped: existing {Existing} >= target {Target}", existingCount, targetTotal);
                    return;
                }

                var toCreate = targetTotal - existingCount;
                logger.LogInformation("Seeding {ToCreate} contacts (existing {Existing})", toCreate, existingCount);

                var rnd = new Random(12345); // deterministic seed for reproducible results
                var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = new DateTime(2025, 9, 30, 23, 59, 59, DateTimeKind.Utc);
                var totalSeconds = (long)(end - start).TotalSeconds;

                string[] firstNames = { "Alex", "Maya", "Noah", "Lena", "Ethan", "Sofia", "Liam", "Ava", "Lucas", "Mia", "Owen", "Zara", "Caleb", "Nora", "Isaac", "Leah", "Ryan", "Ella", "Sean", "Iris", "Joel", "Ruby", "Victor", "Clara", "Jay", "Megan", "Diego", "Hana", "Marco", "Tara" };
                string[] lastNames = { "Garcia", "Reyes", "Lopez", "Santos", "Cruz", "DelaCruz", "Torres", "Velasco", "Navarro", "Mendoza" };
                string[] occupations = { "Software Engineer", "Sales Associate", "Architect", "Teacher", "Nurse", "Photographer", "Accountant", "Business Owner", "Marketer", "Consultant" };
                string[] agents = { "Rheniel Penional", "Maria Cruz", "Miguel Santos", "Admin", null }; // null allowed = No Agent

                var newContacts = new List<Contact>(toCreate);

                for (int i = 0; i < toCreate; i++)
                {
                    var fn = firstNames[(existingCount + i) % firstNames.Length];
                    var ln = lastNames[(existingCount + i) % lastNames.Length];
                    var name = $"{fn} {ln}";

                    var email = $"{fn.ToLower()}.{ln.ToLower()}{existingCount + i}@example.com";

                    // Philippines-like phone sample (11 digits starting with 09)
                    var phone = $"09{rnd.Next(100, 999):D3}{rnd.Next(1000, 9999):D4}";

                    var occupation = occupations[rnd.Next(occupations.Length)];
                    var salary = Math.Round((decimal)rnd.Next(20000, 200000), 2);

                    // Random UTC date between start and end
                    var seconds = (long)(rnd.NextDouble() * totalSeconds);
                    var dateCreated = start.AddSeconds(seconds);

                    var agent = agents[rnd.Next(agents.Length)];

                    var contact = new Contact
                    {
                        Name = name,
                        Agent = string.IsNullOrWhiteSpace(agent) ? null : agent,
                        Email = email,
                        Phone = phone,
                        Type = "Client",
                        DateCreated = dateCreated,
                        LastContacted = null,
                        Notes = "Seeded contact",
                        IsActive = true,
                        Occupation = occupation,
                        Salary = salary
                    };

                    newContacts.Add(contact);
                }

                db.Contacts.AddRange(newContacts);
                db.SaveChanges();

                logger.LogInformation("Seeded {Count} contacts successfully.", newContacts.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while seeding contacts.");
            }
        }
    }
}
