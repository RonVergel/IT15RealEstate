using Microsoft.AspNetCore.Mvc;

namespace RealEstateCRM.Controllers
{
    public class PendingAssignmentsController : Controller
    {
        public IActionResult Index()
        {
            // If you need to pass a model (list of Property) load it here:
            // var model = _context.Properties.Where(...).ToList();
            // return View(model);
            return View();
        }
    }
}