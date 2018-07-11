using System;
using JetBrains.Annotations;
using MarginTrading.Backend.Contracts.Orders;

namespace MarginTrading.Backend.Contracts.Events
{
    public class OrderHistoryEvent : TraceableMessageBase
    {
        public OrderContract OrderSnapshot { get; }
        
        public OrderHistoryTypeContract Type { get; }

        public OrderHistoryEvent(TraceableMessageBase baseMessage, OrderContract orderContract,
            OrderHistoryTypeContract type, DateTime time) 
            : base(baseMessage)
        {
        }

        public OrderHistoryEvent([NotNull] string id, [NotNull] string correlationId, [CanBeNull] string causationId, DateTime eventTimestamp) : base(id, correlationId, causationId, eventTimestamp)
        {
        }
    }
}