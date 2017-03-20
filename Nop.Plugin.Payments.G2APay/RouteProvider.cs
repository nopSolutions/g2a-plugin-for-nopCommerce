using System.Web.Mvc;
using System.Web.Routing;
using Nop.Web.Framework.Mvc.Routes;

namespace Nop.Plugin.Payments.G2APay
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(RouteCollection routes)
        {
            //IPN
            routes.MapRoute("Plugin.Payments.G2APay.IPNHandler",
                 "Plugins/PaymentG2APay/IPNHandler/{storeId}",
                 new { controller = "PaymentG2APay", action = "IPNHandler", storeId = UrlParameter.Optional },
                 new[] { "Nop.Plugin.Payments.G2APay.Controllers" }
            );
        }

        public int Priority
        {
            get { return 0; }
        }
    }
}
