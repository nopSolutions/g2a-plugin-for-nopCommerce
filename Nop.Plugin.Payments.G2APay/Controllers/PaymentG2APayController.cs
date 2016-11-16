using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.G2APay.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Stores;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Payments.G2APay.Controllers
{
    public class PaymentG2APayController : BasePaymentController
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly ISettingService _settingService;
        private readonly IStoreService _storeService;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public PaymentG2APayController(ILocalizationService localizationService,
            ILogger logger,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            ISettingService settingService,
            IStoreService storeService,
            IWebHelper webHelper,
            IWorkContext workContext)
        {
            this._localizationService = localizationService;
            this._logger = logger;
            this._orderProcessingService = orderProcessingService;
            this._orderService = orderService;
            this._settingService = settingService;
            this._storeService = storeService;
            this._webHelper = webHelper;
            this._workContext = workContext;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Validate IPN callback
        /// </summary>
        /// <param name="form">Form parameters</param>
        /// <param name="storeId">Store identifier</param>
        /// <param name="order">Order</param>
        /// <returns>true if there are no errors; otherwise false</returns>
        protected bool ValidateIPN(FormCollection form, int storeId, out Order order)
        {
            //validate order guid
            order = null;
            Guid orderGuid;
            if (!Guid.TryParse(form["userOrderId"], out orderGuid))
                return false;

            //check that order exists
            order = _orderService.GetOrderByGuid(orderGuid);
            if (order == null)
            {
                _logger.Error(string.Format("G2A Pay IPN error: Order with guid {0} is not found", orderGuid));
                return false;
            }

            //validate order total
            decimal orderTotal;
            if (!decimal.TryParse(form["amount"], out orderTotal) || Math.Round(order.OrderTotal, 2) != Math.Round(orderTotal, 2))
            {
                _logger.Error("G2A Pay IPN error: order totals not match");
                return false;
            }

            //validate hash
            var g2apayPaymentSettings = _settingService.LoadSetting<G2APayPaymentSettings>(storeId);
            var stringToHash = string.Format("{0}{1}{2}{3}", form["transactionId"], form["userOrderId"], form["amount"], g2apayPaymentSettings.SecretKey);
            var hash = new SHA256Managed().ComputeHash(Encoding.Default.GetBytes(stringToHash))
                .Aggregate(string.Empty, (current, next) => string.Format("{0}{1}", current, next.ToString("x2")));
            if (!hash.Equals(form["hash"]))
            {
                _logger.Error("G2A Pay IPN error: hashes not match");
                return false;
            }

            return true;
        }

        #endregion

        #region Methods

        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure()
        {
            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var g2apayPaymentSettings = _settingService.LoadSetting<G2APayPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                IpnUrl = string.Format("{0}Plugins/PaymentG2APay/IPNHandler/{1}", _webHelper.GetStoreLocation(), storeScope),
                ApiHash = g2apayPaymentSettings.ApiHash,
                SecretKey = g2apayPaymentSettings.SecretKey,
                MerchantEmail = g2apayPaymentSettings.MerchantEmail,
                UseSandbox = g2apayPaymentSettings.UseSandbox,
                AdditionalFee = g2apayPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = g2apayPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.ApiHash_OverrideForStore = _settingService.SettingExists(g2apayPaymentSettings, x => x.ApiHash, storeScope);
                model.SecretKey_OverrideForStore = _settingService.SettingExists(g2apayPaymentSettings, x => x.SecretKey, storeScope);
                model.UseSandbox_OverrideForStore = _settingService.SettingExists(g2apayPaymentSettings, x => x.UseSandbox, storeScope);
                model.AdditionalFee_OverrideForStore = _settingService.SettingExists(g2apayPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentage_OverrideForStore = _settingService.SettingExists(g2apayPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.G2APay/Views/PaymentG2APay/Configure.cshtml", model);
        }

        [HttpPost]
        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure(ConfigurationModel model)
        {
            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var g2apayPaymentSettings = _settingService.LoadSetting<G2APayPaymentSettings>(storeScope);

            //save settings
            g2apayPaymentSettings.ApiHash = model.ApiHash;
            g2apayPaymentSettings.SecretKey = model.SecretKey;
            g2apayPaymentSettings.MerchantEmail = model.MerchantEmail;
            g2apayPaymentSettings.UseSandbox = model.UseSandbox;
            g2apayPaymentSettings.AdditionalFee = model.AdditionalFee;
            g2apayPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(g2apayPaymentSettings, x => x.ApiHash, model.ApiHash_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2apayPaymentSettings, x => x.SecretKey, model.SecretKey_OverrideForStore, storeScope, false);
            _settingService.SaveSetting(g2apayPaymentSettings, x => x.MerchantEmail, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2apayPaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2apayPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2apayPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        [ChildActionOnly]
        public ActionResult PaymentInfo()
        {
            return View("~/Plugins/Payments.G2APay/Views/PaymentG2APay/PaymentInfo.cshtml");
        }

        [NonAction]
        public override IList<string> ValidatePaymentForm(FormCollection form)
        {
            return new List<string>();
        }

        [NonAction]
        public override ProcessPaymentRequest GetPaymentInfo(FormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        [ValidateInput(false)]
        public ActionResult IPNHandler(FormCollection form, int storeId)
        {
            Order order;
            if (!ValidateIPN(form, storeId, out order))
                return new HttpStatusCodeResult(HttpStatusCode.OK);

            //order note
            var note = new StringBuilder();
            foreach (string key in form.Keys)
            {
                note.AppendFormat("{0}: {1}{2}", key, form[key], Environment.NewLine);
            }

            order.OrderNotes.Add(new OrderNote()
            {
                Note = note.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });
            _orderService.UpdateOrder(order);

            //change order status
            switch (form["status"].ToLowerInvariant())
            {
                case "complete":
                    //paid order
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        order.CaptureTransactionId = form["transactionId"];
                        _orderService.UpdateOrder(order);
                        _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    break;
                case "partial_refunded":
                    //partially refund order
                    decimal amount;
                    if (decimal.TryParse(form["refundedAmount"], out amount) && _orderProcessingService.CanPartiallyRefund(order, amount))
                        _orderProcessingService.PartiallyRefundOffline(order, amount);
                    break;
                case "refunded":
                    //refund order
                    if (_orderProcessingService.CanRefund(order))
                        _orderProcessingService.RefundOffline(order);
                    break;
                case "pending":
                    //do not logging for pending status
                    break;
                default:
                    _logger.Error(string.Format("G2A Pay IPN error: transaction is {0}", form["status"]));
                    break;
            }

            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        #endregion
    }
}