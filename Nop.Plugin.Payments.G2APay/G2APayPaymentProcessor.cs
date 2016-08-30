using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Routing;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Plugins;
using Nop.Plugin.Payments.G2APay.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Seo;

namespace Nop.Plugin.Payments.G2APay
{
    /// <summary>
    /// G2APay payment processor
    /// </summary>
    public class G2APayPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly G2APayPaymentSettings _g2apayPaymentSettings;
        private readonly HttpContextBase _httpContext;
        private readonly ICurrencyService _currencyService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        
        #endregion

        #region Ctor

        public G2APayPaymentProcessor(CurrencySettings currencySettings,
            G2APayPaymentSettings g2apayPaymentSettings,
            HttpContextBase httpContext,
            ICurrencyService currencyService,
            ILocalizationService localizationService,
            ILogger logger,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService, 
            IWebHelper webHelper)
        {
            this._currencySettings = currencySettings;
            this._g2apayPaymentSettings = g2apayPaymentSettings;
            this._httpContext = httpContext;
            this._currencyService = currencyService;
            this._localizationService = localizationService;
            this._logger = logger;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._settingService = settingService;            
            this._webHelper = webHelper;            
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets G2APay payment URL
        /// </summary>
        /// <returns>URL</returns>
        protected string GetG2APayUrl()
        {
            return _g2apayPaymentSettings.UseSandbox ? "https://checkout.test.pay.g2a.com" : "https://checkout.pay.g2a.com";
        }

        /// <summary>
        /// Gets G2APay REST API URL
        /// </summary>
        /// <param name="settings">G2APay payment settings</param>
        /// <returns>URL</returns>
        protected string GetG2APayRestUrl(G2APayPaymentSettings settings = null)
        {
            var g2apayPaymentSettings = settings ?? _g2apayPaymentSettings;
            return g2apayPaymentSettings.UseSandbox ? "https://www.test.pay.g2a.com" : "https://pay.g2a.com";
        }

        /// <summary>
        /// Get Authorization header for the request
        /// </summary>
        /// <param name="settings">G2APay payment settings</param>
        /// <returns>Value of header</returns>
        protected string GetAuthHeader(G2APayPaymentSettings settings = null)
        {
            var g2apayPaymentSettings = settings ?? _g2apayPaymentSettings;
            var stringToHash = string.Format("{0}{1}{2}", g2apayPaymentSettings.ApiHash, g2apayPaymentSettings.MerchantEmail, g2apayPaymentSettings.SecretKey);

            return string.Format("{0};{1}", g2apayPaymentSettings.ApiHash, GetSHA256Hash(stringToHash));
        }

        /// <summary>
        /// Get calculated SHA256 hash for the input string
        /// </summary>
        /// <param name="stringToHash">Input string for the hash</param>
        /// <returns>SHA256 hash</returns>
        protected string GetSHA256Hash(string stringToHash)
        {
           return new SHA256Managed().ComputeHash(Encoding.Default.GetBytes(stringToHash))
                .Aggregate(string.Empty, (current, next) => string.Format("{0}{1}", current, next.ToString("x2")));
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //primary currency
            var currency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId);
            if (currency == null)
                throw new NopException("Currency could not be loaded");

            //store location
            var storeLocation = _webHelper.GetStoreLocation();

            //hash
            var stringToHash = string.Format("{0}{1}{2}{3}",
                postProcessPaymentRequest.Order.OrderGuid,
                postProcessPaymentRequest.Order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture),
                currency.CurrencyCode,
                _g2apayPaymentSettings.SecretKey);

            //post parameters
            var parameters = HttpUtility.ParseQueryString(string.Empty);
            parameters.Add("api_hash", _g2apayPaymentSettings.ApiHash);
            parameters.Add("hash", GetSHA256Hash(stringToHash));
            parameters.Add("order_id", postProcessPaymentRequest.Order.OrderGuid.ToString());
            parameters.Add("amount", postProcessPaymentRequest.Order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture));
            parameters.Add("currency", currency.CurrencyCode);
            parameters.Add("description", string.Format("Order #{0}", postProcessPaymentRequest.Order.Id));
            parameters.Add("url_ok", string.Format("{0}checkout/completed/{1}", storeLocation, postProcessPaymentRequest.Order.Id));
            parameters.Add("url_failure", string.Format("{0}orderdetails/{1}", storeLocation, postProcessPaymentRequest.Order.Id));

            //items parameters
            var items = postProcessPaymentRequest.Order.OrderItems.Select(item =>
                new G2APayPaymentItem
                {
                    Id = item.Id,
                    Name = item.Product.Name,
                    Sku = !string.IsNullOrEmpty(item.Product.Sku) ? item.Product.Sku : item.Product.Id.ToString(),
                    Price = item.UnitPriceInclTax.ToString("0.00", CultureInfo.InvariantCulture),
                    Quantity = item.Quantity,
                    Amount = item.PriceInclTax.ToString("0.00", CultureInfo.InvariantCulture),
                    Url = string.Format("{0}{1}", storeLocation, item.Product.GetSeName())
                }).ToList();
            //add special item for the shipping rate, payment fee, tax, etc
            var difference = postProcessPaymentRequest.Order.OrderTotal - postProcessPaymentRequest.Order.OrderItems.Sum(item => item.PriceInclTax);
            if (difference != 0)
                items.Add(new G2APayPaymentItem
                {
                    Id = 1, 
                    Name = _localizationService.GetResource("Plugins.Payments.G2APay.SpecialItem"),
                    Sku = "spec_item",
                    Price = difference.ToString("0.00", CultureInfo.InvariantCulture),
                    Quantity = 1,
                    Amount = difference.ToString("0.00", CultureInfo.InvariantCulture),
                    Url = storeLocation
                });
            parameters.Add("items", JsonConvert.SerializeObject(items.ToArray()));

            var postData = Encoding.Default.GetBytes(parameters.ToString());

            //post
            var request = (HttpWebRequest)WebRequest.Create(string.Format("{0}/index/createQuote", GetG2APayUrl()));
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = postData.Length;
            request.Headers.Add(HttpRequestHeader.Authorization, GetAuthHeader());

            try
            {
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(postData, 0, postData.Length);
                }
                var httpResponse = (HttpWebResponse)request.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var response = JsonConvert.DeserializeObject<G2APayPaymentResponse>(streamReader.ReadToEnd());
                    if (response.Status.Equals("ok", StringComparison.InvariantCultureIgnoreCase))
                        _httpContext.Response.Redirect(string.Format("{0}/index/gateway?token={1}", GetG2APayUrl(), response.Token));
                    else
                        throw new NopException(string.Format("G2APay transaction error: status is {0}", response.Status));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("G2APay transaction error", ex);
                _httpContext.Response.Redirect(storeLocation);
            }            
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            var result = this.CalculateAdditionalFee(_orderTotalCalculationService, cart,
                _g2apayPaymentSettings.AdditionalFee, _g2apayPaymentSettings.AdditionalFeePercentage);

            return result;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();

            //primary currency
            var currency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId);
            if (currency == null)
                throw new NopException("Currency could not be loaded");

            //settings
            var g2apayPaymentSettings = _settingService.LoadSetting<G2APayPaymentSettings>(refundPaymentRequest.Order.StoreId);

            //hash
            var stringToHash = string.Format("{0}{1}{2}{3}{4}",
                refundPaymentRequest.Order.CaptureTransactionId,
                refundPaymentRequest.Order.OrderGuid,
                refundPaymentRequest.Order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture),
                refundPaymentRequest.AmountToRefund.ToString("0.00", CultureInfo.InvariantCulture),
                g2apayPaymentSettings.SecretKey);

            //post parameters
            var parameters = HttpUtility.ParseQueryString(string.Empty);
            parameters.Add("action", "refund");
            parameters.Add("order_id", refundPaymentRequest.Order.OrderGuid.ToString());
            parameters.Add("amount", refundPaymentRequest.AmountToRefund.ToString("0.00", CultureInfo.InvariantCulture));
            parameters.Add("currency", currency.CurrencyCode);
            parameters.Add("hash", GetSHA256Hash(stringToHash));

            var postData = Encoding.Default.GetBytes(parameters.ToString());

            //post
            var request = (HttpWebRequest)WebRequest.Create(string.Format("{0}/rest/transactions/{1}",
                GetG2APayRestUrl(g2apayPaymentSettings), refundPaymentRequest.Order.CaptureTransactionId));
            request.Method = "PUT";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = postData.Length;
            request.Headers.Add(HttpRequestHeader.Authorization, GetAuthHeader(g2apayPaymentSettings));

            try
            {
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(postData, 0, postData.Length);
                }
                var httpResponse = (HttpWebResponse)request.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var response = JsonConvert.DeserializeObject<G2APayPaymentResponse>(streamReader.ReadToEnd());
                    if (!response.Status.Equals("ok", StringComparison.InvariantCultureIgnoreCase))
                        throw new NopException(string.Format("G2APay refund error: transaction status is {0}", response.Status));

                    //leaving payment status, we will change it later, upon receiving IPN
                    result.NewPaymentStatus = refundPaymentRequest.Order.PaymentStatus;
                    result.AddError(_localizationService.GetResource("Plugins.Payments.G2APay.Refund"));
                }
            }
            catch (WebException ex)
            {
                var error = "G2APay refund error. ";
                using (var streamReader = new StreamReader(ex.Response.GetResponseStream()))
                {
                    error += streamReader.ReadToEnd();
                }
                _logger.Error(error, ex);
                result.AddError(string.Format("{1}. {0}", error, ex.Message));
            }

            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException("order");
            
            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return false;

            return true;
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "PaymentG2APay";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.G2APay.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a route for payment info
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetPaymentInfoRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PaymentInfo";
            controllerName = "PaymentG2APay";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.G2APay.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Get type of the controller
        /// </summary>
        /// <returns>Controller type</returns>
        public Type GetControllerType()
        {
            return typeof(PaymentG2APayController);
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new G2APayPaymentSettings
            {
                UseSandbox = true
            });

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFee", "Additional fee");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.APIHash", "API Hash");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.APIHash.Hint", "Specify your G2APay API hash.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.MerchantEmail", "Merchant email");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.MerchantEmail.Hint", "Specify your merchant email (G2A account name).");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.SecretKey", "Secret");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.SecretKey.Hint", "Specify your G2APay secret.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.UseSandbox", "Use Sandbox");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Fields.UseSandbox.Hint", "Check to enable sandbox (testing environment).");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.RedirectionTip", "You will be redirected to G2APay site to complete the order.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.Refund", "Refund will happen later, after receiving successful IPN by G2APay service.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.G2APay.SpecialItem", "Additional charges (delivery, payment fee, taxes, discounts, etc)");

            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<G2APayPaymentSettings>();

            //locales
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFee");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFee.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFeePercentage");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.AdditionalFeePercentage.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.APIHash");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.APIHash.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.MerchantEmail");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.MerchantEmail.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.SecretKey");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.SecretKey.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.UseSandbox");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Fields.UseSandbox.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.RedirectionTip");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.Refund");
            this.DeletePluginLocaleResource("Plugins.Payments.G2APay.SpecialItem");

            base.Uninstall();
        }

        #endregion

        #region Properies

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        #endregion
    }
}
