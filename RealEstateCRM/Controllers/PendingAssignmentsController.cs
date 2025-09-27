using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using System.Linq;
using System.Threading.Tasks;

namespace RealEstateCRM.Controllers
{
    [Authorize(Roles = "Agent")]
    public class PendingAssignmentsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public PendingAssignmentsController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Shows properties assigned to the current agent only.
        public async Task<IActionResult> Index(string? agent = null)
        {
            // Always resolve to current user's identity; ignore any provided 'agent' value
            string? agentKey = null;
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                // Try FullName claim first; fall back to UserName, then Email
                string? fullName = null;
                try
                {
                    var claims = await _userManager.GetClaimsAsync(user);
                    fullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
                }
                catch { }

                agentKey = !string.IsNullOrWhiteSpace(fullName) ? fullName : (user.UserName ?? user.Email);
            }

            var query = _context.Properties.AsQueryable();
            if (!string.IsNullOrWhiteSpace(agentKey))
            {
                query = query.Where(p => p.Agent != null && p.Agent.ToLower() == agentKey!.ToLower());
            }
            // Show only items in Pending status for clarity
            query = query.Where(p => p.ListingStatus == "Pending");

            var model = await query
                .OrderByDescending(p => p.ListingTime)
                .ToListAsync();
            ViewBag.AgentDisplayName = agentKey;
            return View(model);
        }
    }
}
