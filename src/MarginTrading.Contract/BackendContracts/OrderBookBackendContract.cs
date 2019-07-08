﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace MarginTrading.Contract.BackendContracts
{
    public class OrderBookBackendContract
    {
        public Dictionary<decimal, LimitOrderBackendContract[]> Buy { get; set; }
        public Dictionary<decimal, LimitOrderBackendContract[]> Sell { get; set; }
    }
}
