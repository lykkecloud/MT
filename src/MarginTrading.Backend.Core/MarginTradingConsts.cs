﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace MarginTrading.Backend.Core
{
    public static class MarginTradingHelpers
    {
        // TODO: need to use different accuracy for different asset pairs
        public const int VolumeAccuracy = 10;
    }

    public static class MatchingEngineConstants
    {
        public const string Reject = "REJECT";
        public const string DefaultMm = "MM";
        public const string LykkeCyStp = "LYKKECY_STP";
        public const string DefaultStp = "STP";
        public const string DefaultSpecialLiquidation = "SPECIAL_LIQUIDATION";
    }
}
