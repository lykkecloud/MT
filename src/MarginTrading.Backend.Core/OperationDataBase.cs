// Copyright (c) 2019 Lykke Corp.

using System;

namespace MarginTrading.Backend.Core
{
    public class OperationDataBase<TState>
        where TState : struct, IConvertible
    {
        public TState State { get; set; }
    }
}