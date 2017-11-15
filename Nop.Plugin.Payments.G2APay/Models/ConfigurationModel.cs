using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nop.Plugin.Payments.G2APay.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.G2APay.Fields.IpnUrl")]
        public string IpnUrl { get; set; }

        [NopResourceDisplayName("Plugins.Payments.G2APay.Fields.ApiHash")]
        public string ApiHash { get; set; }
        public bool ApiHash_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.G2APay.Fields.SecretKey")]
        public string SecretKey { get; set; }
        public bool SecretKey_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.G2APay.Fields.MerchantEmail")]
        public string MerchantEmail { get; set; }

        [NopResourceDisplayName("Plugins.Payments.G2APay.Fields.UseSandbox")]
        public bool UseSandbox { get; set; }
        public bool UseSandbox_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.G2APay.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFee_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.G2APay.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
        public bool AdditionalFeePercentage_OverrideForStore { get; set; }
    }
}