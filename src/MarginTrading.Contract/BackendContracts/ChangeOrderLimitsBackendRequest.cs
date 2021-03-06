﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace MarginTrading.Contract.BackendContracts
{
    public class ChangeOrderLimitsBackendRequest
    {
        public string ClientId { get; set; }
        public string OrderId { get; set; }
        public string AccountId { get; set; }
        public decimal? TakeProfit { get; set; }
        public decimal? StopLoss { get; set; }
        public decimal? ExpectedOpenPrice { get; set; }
    }
}
