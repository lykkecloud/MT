﻿// Copyright (c) 2019 Lykke Corp.

namespace MarginTrading.Contract.ClientContracts
{
    public class SetActiveAccountClientRequest
    {
        public string Token { get; set; }
        public string AccountId { get; set; }
    }
}
