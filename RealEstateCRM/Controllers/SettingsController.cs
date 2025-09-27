using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Controllers
{
    [Authorize(Roles = "Broker")]
    public class SettingsController : Controller
    {
        private readonly AppDbContext _db;
        public SettingsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var settings = await _db.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync() ?? new AgencySettings();
            return View(settings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(decimal brokerPercent, decimal agentPercent)
        {
            if (brokerPercent < 0 || brokerPercent > 100 || agentPercent < 0 || agentPercent > 100)
            {
                TempData["ErrorMessage"] = "Percentages must be between 0 and 100.";
                return RedirectToAction("Index");
            }
            if (brokerPercent + agentPercent > 100)
            {
                TempData["ErrorMessage"] = "Broker + Agent percentage cannot exceed 100%.";
                return RedirectToAction("Index");
            }

            var settings = await _db.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AgencySettings
                {
                    BrokerCommissionPercent = brokerPercent,
                    AgentCommissionPercent = agentPercent,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _db.AgencySettings.Add(settings);
            }
            else
            {
                settings.BrokerCommissionPercent = brokerPercent;
                settings.AgentCommissionPercent = agentPercent;
                settings.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Agency settings updated.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGoal(decimal monthlyGoal)
        {
            if (monthlyGoal < 0) monthlyGoal = 0;
            var settings = await _db.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AgencySettings { MonthlyRevenueGoal = monthlyGoal, UpdatedAtUtc = DateTime.UtcNow };
                _db.AgencySettings.Add(settings);
            }
            else
            {
                settings.MonthlyRevenueGoal = monthlyGoal;
                settings.UpdatedAtUtc = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Monthly revenue goal saved.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDeadlinesConfig(int inspection, int appraisal, int loan, int closing)
        {
            inspection = Math.Max(1, inspection);
            appraisal = Math.Max(1, appraisal);
            loan = Math.Max(1, loan);
            closing = Math.Max(1, closing);

            var settings = await _db.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AgencySettings
                {
                    InspectionDays = inspection,
                    AppraisalDays = appraisal,
                    LoanCommitmentDays = loan,
                    ClosingDays = closing,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _db.AgencySettings.Add(settings);
            }
            else
            {
                settings.InspectionDays = inspection;
                settings.AppraisalDays = appraisal;
                settings.LoanCommitmentDays = loan;
                settings.ClosingDays = closing;
                settings.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Deadline defaults saved.";
            return RedirectToAction("Index");
        }
    }
}
