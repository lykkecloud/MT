﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;

namespace MarginTrading.Contract.ClientContracts
{
    public class BidAskClientContract
    {
        public string Id { get; set; }
        public DateTime Date { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
    }
}
