using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.G2APay.Components
{
    [ViewComponent(Name = "PaymentG2APay")]
    public class PaymentG2APayViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.G2APay/Views/PaymentInfo.cshtml");
        }
    }
}
