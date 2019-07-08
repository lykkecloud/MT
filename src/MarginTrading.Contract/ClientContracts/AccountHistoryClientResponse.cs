﻿// Copyright (c) 2019 Lykke Corp.

namespace MarginTrading.Contract.ClientContracts
{
    public class AccountHistoryClientResponse
    {
        public AccountHistoryClientContract[] Account { get; set; }
        public OrderHistoryClientContract[] PositionsHistory { get; set; }
        public OrderHistoryClientContract[] OpenPositions { get; set; }
    }
}
