using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.G2APay
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            //IPN
            routeBuilder.MapRoute("Plugin.Payments.G2APay.IPNHandler",
                 "Plugins/PaymentG2APay/IPNHandler/{storeId?}",
                 new { controller = "PaymentG2APay", action = "IPNHandler"});
        }

        public int Priority
        {
            get { return 0; }
        }
    }
}
