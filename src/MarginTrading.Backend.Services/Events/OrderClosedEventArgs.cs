﻿using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Orders;

namespace MarginTrading.Backend.Services.Events
{
    public class OrderClosedEventArgs: OrderUpdateBaseEventArgs
    {
        public OrderClosedEventArgs(Position order):base(order)
        {
        }

        public override OrderUpdateType UpdateType => OrderUpdateType.Close;
    }
}