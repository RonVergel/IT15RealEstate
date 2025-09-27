using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RealEstateCRM.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class PersonalDataModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}

