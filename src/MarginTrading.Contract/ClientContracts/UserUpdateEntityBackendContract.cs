﻿// Copyright (c) 2019 Lykke Corp.

namespace MarginTrading.Contract.ClientContracts
{
    public class UserUpdateEntityBackendContract
    {
        public string[] ClientIds { get; set; }
        public bool UpdateAccountAssetPairs { get; set; }
        public bool UpdateAccounts { get; set; }
    }
}