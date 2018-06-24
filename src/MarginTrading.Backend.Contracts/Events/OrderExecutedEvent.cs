using System;
using JetBrains.Annotations;
using MarginTrading.Backend.Contracts.Orders;
using MessagePack;

namespace MarginTrading.Backend.Contracts.Events
{
    /// <summary>
    /// Order was executed
    /// </summary>
    [MessagePackObject]
    public class OrderExecutedEvent
    {
        public OrderExecutedEvent([NotNull] string operationId, [NotNull] OrderContract order)
        {
            OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
            Order = order ?? throw new ArgumentNullException(nameof(order));
        }

        [NotNull]
        [Key(0)]
        public string OperationId { get; }
        
        [NotNull]
        [Key(1)]
        public OrderContract Order { get; }
    }
}