using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MarginTrading.Backend.Core
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SpecialLiquidationOperationState
    {
        Initiated = 0,
        PriceRequested = 2,
        PriceReceived = 3,
        InternalOrderExecutionStarted = 5,
        Finished = 7,
        OnTheWayToFail = 8,
        Failed = 9,
    }
}