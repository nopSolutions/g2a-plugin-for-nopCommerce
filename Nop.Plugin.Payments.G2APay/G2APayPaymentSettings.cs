using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.G2APay
{
    public class G2APayPaymentSettings : ISettings
    {
        /// <summary>
        /// Gets or sets API hash
        /// </summary>
        public string ApiHash { get; set; }

        /// <summary>
        /// Gets or sets secret key
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// Gets or sets merchant email (G2A account name)
        /// </summary>
        public string MerchantEmail { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use sandbox (testing environment)
        /// </summary>
        public bool UseSandbox { get; set; }

        /// <summary>
        /// Gets or sets an additional fee
        /// </summary>
        public decimal AdditionalFee { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value.
        /// </summary>
        public bool AdditionalFeePercentage { get; set; }
    }
}
