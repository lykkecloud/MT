﻿// Copyright (c) 2019 Lykke Corp.

namespace MarginTrading.Contract.ClientContracts
{
    public class OrderBookLevelClientContract
    {
        public decimal Price { get; set; }
        public decimal Volume { get; set; }
    }
}