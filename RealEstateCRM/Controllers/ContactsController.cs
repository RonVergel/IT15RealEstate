using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Controllers
{
    public class ContactsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ContactsController> _logger;

        public ContactsController(AppDbContext db, ILogger<ContactsController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // GET: /Contacts/Index
        public IActionResult Index()
        {
            return View("Index");
        }
    }
}