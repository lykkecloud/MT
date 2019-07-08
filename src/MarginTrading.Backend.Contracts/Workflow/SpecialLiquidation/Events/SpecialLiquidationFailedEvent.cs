// Copyright (c) 2019 Lykke Corp.

using System;
using MessagePack;

namespace MarginTrading.Backend.Contracts.Workflow.SpecialLiquidation.Events
{
    /// <summary>
    /// The special liquidation flow failed.
    /// </summary>
    [MessagePackObject]
    public class SpecialLiquidationFailedEvent
    {
        /// <summary>
        /// Operation Id
        /// </summary>
        [Key(0)]
        public string OperationId { get; set; }
        
        /// <summary>
        /// Event creation time
        /// </summary>
        [Key(1)]
        public DateTime CreationTime { get; set; }
        
        /// <summary>
        /// Reason of failure
        /// </summary>
        [Key(2)]
        public string Reason { get; set; }
    }
}