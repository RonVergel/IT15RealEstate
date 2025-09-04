using Microsoft.AspNetCore.Mvc;

namespace RealEstateCRM.Controllers
{
    public class LeadsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
