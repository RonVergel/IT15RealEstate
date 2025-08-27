using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RealEstateCRM.Models;

namespace RealEstateCRM.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger) => _logger = logger;

        public IActionResult Index() => RedirectToAction("Log_in");

        public IActionResult Log_in() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Log_in(string email, string password, bool? remember)
        {
            // TODO: authenticate user (validate email/password)
            // if (!isValid) { ModelState.AddModelError("", "Invalid credentials"); return View(); }

            // On success, send them to your target screen:
            return RedirectToAction("Dashboard");
        }

        public IActionResult Dashboard() => View();
    }
}
