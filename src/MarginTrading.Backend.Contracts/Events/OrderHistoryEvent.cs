using System;
using JetBrains.Annotations;
using MarginTrading.Backend.Contracts.Orders;

namespace MarginTrading.Backend.Contracts.Events
{
    public class OrderHistoryEvent : TraceableMessageBase
    {
        public OrderContract OrderSnapshot { get; }
        
        public OrderHistoryTypeContract Type { get; }

        public OrderHistoryEvent([NotNull] string correlationId, [CanBeNull] string causationId, 
            DateTime eventTimestamp, OrderContract orderSnapshot, OrderHistoryTypeContract type) 
            : base(correlationId, causationId, eventTimestamp)
        {
            OrderSnapshot = orderSnapshot;
            Type = type;
        }
    }
}