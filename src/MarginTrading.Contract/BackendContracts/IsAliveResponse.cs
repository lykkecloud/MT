﻿using System;

namespace MarginTrading.Contract.BackendContracts
{
    public class IsAliveResponse
    {
        public string Version { get; set; }
        public string Env { get; set; }
        public DateTime ServerTime { get; set; }
    }
}
