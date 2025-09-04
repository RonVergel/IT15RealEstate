using Microsoft.AspNetCore.Mvc;

namespace RealEstateCRM.Controllers
{
    public class PropertiesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
