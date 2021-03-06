﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;
using MarginTrading.Backend.Contracts.Events;

namespace MarginTrading.AccountMarginEventsBroker.Repositories.Models
{
    internal interface IAccountMarginEvent
    {
        string Id { get; }
        string AccountId { get; }
        decimal Balance { get; }
        string BaseAssetId { get; }
        string EventId { get; }
        DateTime EventTime { get; }
        decimal FreeMargin { get; }
        bool IsEventStopout { get; }
        MarginEventTypeContract EventType { get; }
        decimal MarginAvailable { get; }
        decimal MarginCall { get; }
        decimal MarginInit { get; }
        decimal MarginUsageLevel { get; }
        decimal OpenPositionsCount { get; }
        decimal PnL { get; }
        decimal StopOut { get; }
        decimal TotalCapital { get; }
        string TradingConditionId { get; }
        decimal UsedMargin { get; }
        decimal WithdrawTransferLimit { get; }
    }
}
