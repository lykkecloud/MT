// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MarginTrading.Backend.Core.Orders
{
    [JsonConverter(typeof (StringEnumConverter))]
    public enum OrderModality
    {
        [EnumMember(Value = "Unspecified")] Unspecified = 0,
        [EnumMember(Value = "Liquidation_CorporateAction")] Liquidation_CorporateAction = 76, // 0x4C
        [EnumMember(Value = "Regular")] Regular = 82, // 0x52
        [EnumMember(Value = "Liquidation_MarginCall")] Liquidation_MarginCall = 108
    }
}