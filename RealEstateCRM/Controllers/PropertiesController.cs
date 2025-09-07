using Microsoft.AspNetCore.Mvc;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Controllers
{
    public class PropertiesController : Controller
    {
        private readonly AppDbContext _context;

        public PropertiesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Properties/Index
        public IActionResult Index()
        {
            var properties = _context.Properties.ToList();
            return View(properties);
        }

        // POST: Properties/Index - Handle form submission
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(Property property)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Calculate DaysOnMarket if ListingTime is provided
                    if (property.ListingTime != default(DateTime))
                    {
                        property.DaysOnMarket = (DateTime.Now - property.ListingTime).Days;
                    }

                    // Calculate PricePerSQFT if both Price and SQFT are provided
                    if (property.SQFT.HasValue && property.SQFT > 0)
                    {
                        property.PricePerSQFT = property.Price / (decimal)property.SQFT.Value;
                    }

                    _context.Properties.Add(property);
                    _context.SaveChanges();
                    
                    // Redirect to avoid form resubmission on refresh
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    // Log the error (you can add proper logging here)
                    ModelState.AddModelError("", "An error occurred while saving the property: " + ex.Message);
                }
            }

            // If we got this far, something failed, redisplay form with validation errors
            // Return all properties to display the list along with any errors
            var properties = _context.Properties.ToList();
            return View(properties);
        }
    }
}
