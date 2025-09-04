using Microsoft.AspNetCore.Mvc;

namespace RealEstateCRM.Controllers
{
    public class DealsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
