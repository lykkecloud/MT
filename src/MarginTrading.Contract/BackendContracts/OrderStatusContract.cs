﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace MarginTrading.Contract.BackendContracts
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