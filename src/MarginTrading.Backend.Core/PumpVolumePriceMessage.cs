using Newtonsoft.Json;

namespace MarginTrading.Backend.Core
{
    public class PumpVolumePriceMessage
    {
        [JsonProperty("volume")]
        public decimal Volume { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }
    }
}