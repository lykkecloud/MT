﻿// Copyright (c) 2019 Lykke Corp.

namespace MarginTrading.Backend.Contracts.TradeMonitoring
{
    public enum OrderStatusContract
    {
        WaitingForExecution,
        Active,
        Closed,
        Rejected,
        Closing
    }
}
