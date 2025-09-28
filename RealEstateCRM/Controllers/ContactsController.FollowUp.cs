using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using RealEstateCRM.Models;
using RealEstateCRM.Services.Notifications;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public partial class ContactsController
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFollowUp(int contactId, string? followUpLocal)
        {
            var contact = await _db.Contacts.FindAsync(contactId);
            if (contact == null)
                return NotFound(new { success = false, message = "Contact not found" });

            DateTime? newUtc = null;
            if (!string.IsNullOrWhiteSpace(followUpLocal))
            {
                // Try stricter parse first (browser datetime-local)
                if (DateTime.TryParse(followUpLocal, out var local))
                {
                    if (local.Kind == DateTimeKind.Unspecified)
                        local = DateTime.SpecifyKind(local, DateTimeKind.Local);
                    newUtc = local.ToUniversalTime();
                }
                else
                {
                    return BadRequest(new { success = false, message = "Invalid date/time" });
                }
            }

            contact.NextFollowUpUtc = newUtc;
            contact.FollowUpNotifiedUtc = null;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                nextFollowUpLocal = newUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                iso = newUtc?.ToLocalTime().ToString("s")
            });
        }

        // JS polling trigger to notify overdue follow-ups
        [HttpPost]
        public async Task<IActionResult> CheckFollowUps()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized(new { success = false });

            var fullName = User.Claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
            var now = DateTime.UtcNow;

            var query = _db.Contacts
                .Where(c => c.NextFollowUpUtc != null
                            && c.NextFollowUpUtc <= now
                            && (c.FollowUpNotifiedUtc == null || c.FollowUpNotifiedUtc < c.NextFollowUpUtc));

            if (!User.IsInRole("Broker") && !string.IsNullOrWhiteSpace(fullName))
            {
                query = query.Where(c => c.Agent == fullName);
            }

            var dueList = await query.Take(50).ToListAsync();
            foreach (var c in dueList)
            {
                await _notifications.NotifyUserAsync(
                    user.Id,
                    $"Follow-up due: {c.Name}",
                    "/Contacts/Index",
                    user.Id,
                    "FollowUpDue");

                c.FollowUpNotifiedUtc = now;
            }

            if (dueList.Count > 0)
                await _db.SaveChangesAsync();

            return Ok(new { success = true, count = dueList.Count });
        }
    }
}