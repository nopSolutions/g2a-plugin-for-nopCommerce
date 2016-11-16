using Newtonsoft.Json;

namespace Nop.Plugin.Payments.G2APay
{
    /// <summary>
    /// Represents G2A Pay payment response
    /// </summary>
    public class G2APayPaymentResponse
    {
        /// <summary>
        /// Gets or sets transaction status
        /// </summary>
        [JsonProperty(PropertyName = "status")]
        public string Status { get; set; }

        /// <summary>
        /// Gets os sets transaction token
        /// </summary>
        [JsonProperty(PropertyName = "token")]
        public string Token { get; set; }

        /// <summary>
        /// Gets os sets transaction ID
        /// </summary>
        [JsonProperty(PropertyName = "transactionId")]
        public string TransactionId { get; set; }
    }

    /// <summary>
    /// Represents order item
    /// </summary>
    public class G2APayPaymentItem
    {
        /// <summary>
        /// Gets or sets SKU of the item
        /// </summary>
        [JsonProperty(PropertyName = "sku")]
        public string Sku { get; set; }

        /// <summary>
        /// Gets or sets item name
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets amount (total item price (quantity x price))
        /// </summary>
        [JsonProperty(PropertyName = "amount")]
        public string Amount { get; set; }

        /// <summary>
        /// Gets or sets item quantity
        /// </summary>
        [JsonProperty(PropertyName = "qty")]
        public int Quantity { get; set; }

        /// <summary>
        /// Gets or sets item ID
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets item price
        /// </summary>
        [JsonProperty(PropertyName = "price")]
        public string Price { get; set; }

        /// <summary>
        /// Gets or sets item url
        /// </summary>
        [JsonProperty(PropertyName = "url")]
        public string Url { get; set; }
    } 
}

