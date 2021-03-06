﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MarginTrading.Backend.Core
{
    public class AccountFpl
    {
        public AccountFpl()
        {
            ActualHash = 1;
        }
        
        public decimal PnL { get; set; }
        public decimal UnrealizedDailyPnl { get; set; }
        
        /// <summary>
        /// Currently used margin for maintaining of positions
        /// </summary>
        public decimal UsedMargin { get; set; }
        
        /// <summary>
        /// Margin used for initial open of positions
        /// </summary>
        public decimal InitiallyUsedMargin { get; set; }
        
        /// <summary>
        /// Currently used margin for opening of positions
        /// </summary>
        public decimal MarginInit { get; set; }
        public int OpenPositionsCount { get; set; }
        public int ActiveOrdersCount { get; set; }
        public decimal MarginCall1Level { get; set; }
        public decimal MarginCall2Level { get; set; }
        public decimal StopOutLevel { get; set; }
        public decimal WithdrawalFrozenMargin { get; set; }

        public ConcurrentDictionary<string, decimal> WithdrawalFrozenMarginData { get; set; } =
            new ConcurrentDictionary<string, decimal>();
        public decimal UnconfirmedMargin { get; set; }

        public ConcurrentDictionary<string, decimal> UnconfirmedMarginData { get; set; } =
            new ConcurrentDictionary<string, decimal>();

        public int CalculatedHash { get; set; }
        public int ActualHash { get; set; }
    }
}