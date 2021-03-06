﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace MarginTrading.Contract.ClientContracts
{
    public class InitAccountInstrumentsClientResponse
    {
        public Dictionary<string, AccountAssetPairClientContract[]> TradingConditions { get; set; }
    }
}
