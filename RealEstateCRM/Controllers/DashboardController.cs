using Microsoft.AspNetCore.Mvc;

namespace RealEstateCRM.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
