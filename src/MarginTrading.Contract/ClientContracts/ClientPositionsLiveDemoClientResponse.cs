﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace MarginTrading.Contract.ClientContracts
{
    public class ClientPositionsLiveDemoClientResponse
    {
        public ClientOrdersClientResponse Live { get; set; }
        public ClientOrdersClientResponse Demo { get; set; }
    }
}