﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace MarginTrading.Contract.ClientContracts
{
    public class SetActiveAccountClientRequest
    {
        public string Token { get; set; }
        public string AccountId { get; set; }
    }
}
