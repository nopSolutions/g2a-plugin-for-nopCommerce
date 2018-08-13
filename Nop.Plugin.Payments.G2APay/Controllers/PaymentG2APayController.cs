using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.G2APay.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

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
        private readonly IWebHelper _webHelper;
        private readonly IStoreContext _storeContext;
        private readonly IPermissionService _permissionService;

        #endregion

        #region Ctor

        public PaymentG2APayController(ILocalizationService localizationService,
            ILogger logger,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            ISettingService settingService,
            IWebHelper webHelper,
            IStoreContext storeContext,
            IPermissionService permissionService)
        {
            this._localizationService = localizationService;
            this._logger = logger;
            this._orderProcessingService = orderProcessingService;
            this._orderService = orderService;
            this._settingService = settingService;
            this._webHelper = webHelper;
            this._storeContext = storeContext;
            this._permissionService = permissionService;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Validate IPN callback
        /// </summary>
        /// <param name="form">Form parameters</param>
        /// <param name="storeId">Store identifier; pass null to use "all stores" identifier</param>
        /// <param name="order">Order</param>
        /// <returns>true if there are no errors; otherwise false</returns>
        protected bool ValidateIPN(IFormCollection form, int? storeId, out Order order)
        {
            //validate order guid
            order = null;
            if (!Guid.TryParse(form["userOrderId"], out Guid orderGuid))
                return false;

            //check that order exists
            order = _orderService.GetOrderByGuid(orderGuid);
            if (order == null)
            {
                _logger.Error($"G2A Pay IPN error: Order with guid {orderGuid} is not found");
                return false;
            }

            //validate order total
            if (!decimal.TryParse(form["amount"], out decimal orderTotal) || Math.Round(order.OrderTotal, 2) != Math.Round(orderTotal, 2))
            {
                _logger.Error("G2A Pay IPN error: order totals not match");
                return false;
            }

            //validate hash
            var g2APayPaymentSettings = _settingService.LoadSetting<G2APayPaymentSettings>(storeId ?? 0);
            var stringToHash = $"{form["transactionId"]}{form["userOrderId"]}{form["amount"]}{g2APayPaymentSettings.SecretKey}";
            var hash = new SHA256Managed().ComputeHash(Encoding.Default.GetBytes(stringToHash))
                .Aggregate(string.Empty, (current, next) => $"{current}{next:x2}");
            if (!hash.Equals(form["hash"]))
            {
                _logger.Error("G2A Pay IPN error: hashes not match");
                return false;
            }

            return true;
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var g2APayPaymentSettings = _settingService.LoadSetting<G2APayPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                IpnUrl = $"{_webHelper.GetStoreLocation()}Plugins/PaymentG2APay/IPNHandler/{(storeScope > 0 ? storeScope.ToString() : string.Empty)}",
                ApiHash = g2APayPaymentSettings.ApiHash,
                SecretKey = g2APayPaymentSettings.SecretKey,
                MerchantEmail = g2APayPaymentSettings.MerchantEmail,
                UseSandbox = g2APayPaymentSettings.UseSandbox,
                AdditionalFee = g2APayPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = g2APayPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.ApiHash_OverrideForStore = _settingService.SettingExists(g2APayPaymentSettings, x => x.ApiHash, storeScope);
                model.SecretKey_OverrideForStore = _settingService.SettingExists(g2APayPaymentSettings, x => x.SecretKey, storeScope);
                model.UseSandbox_OverrideForStore = _settingService.SettingExists(g2APayPaymentSettings, x => x.UseSandbox, storeScope);
                model.AdditionalFee_OverrideForStore = _settingService.SettingExists(g2APayPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentage_OverrideForStore = _settingService.SettingExists(g2APayPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.G2APay/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var g2APayPaymentSettings = _settingService.LoadSetting<G2APayPaymentSettings>(storeScope);

            //save settings
            g2APayPaymentSettings.ApiHash = model.ApiHash;
            g2APayPaymentSettings.SecretKey = model.SecretKey;
            g2APayPaymentSettings.MerchantEmail = model.MerchantEmail;
            g2APayPaymentSettings.UseSandbox = model.UseSandbox;
            g2APayPaymentSettings.AdditionalFee = model.AdditionalFee;
            g2APayPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(g2APayPaymentSettings, x => x.ApiHash, model.ApiHash_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2APayPaymentSettings, x => x.SecretKey, model.SecretKey_OverrideForStore, storeScope, false);
            _settingService.SaveSetting(g2APayPaymentSettings, x => x.MerchantEmail, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2APayPaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2APayPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(g2APayPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
        
        public IActionResult IPNHandler(int? storeId, IpnModel model)
        {
            var form = model.Form;

            if (!ValidateIPN(form, storeId, out Order order))
                return new StatusCodeResult((int)HttpStatusCode.OK);

            //order note
            var note = new StringBuilder();
            foreach (var key in form.Keys)
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
            switch (form["status"].ToString().ToLowerInvariant())
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
                    _logger.Error($"G2A Pay IPN error: transaction is {form["status"]}");
                    break;
            }

            return new StatusCodeResult((int)HttpStatusCode.OK);
        }

        #endregion
    }
}