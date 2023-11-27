using Microsoft.AspNetCore.Mvc;

namespace WebDBBackup.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}
