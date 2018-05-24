using System;
using System.Collections.Generic;
using System.Linq;
using MarginTrading.Backend.Core.Orderbooks;
using Newtonsoft.Json;

namespace MarginTrading.Backend.Core
{
    public class PumpQuoteMessage
    {
        [JsonProperty("source")]
        public string ExchangeName { get; set; }

        [JsonProperty("asset")]
        public string AssetPairId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("asks")]
        public List<PumpVolumePriceMessage> Asks { get; set; }

        [JsonProperty("bids")]
        public List<PumpVolumePriceMessage> Bids { get; set; }
    }
}