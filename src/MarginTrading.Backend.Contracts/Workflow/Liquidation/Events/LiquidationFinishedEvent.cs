using System;
using System.Collections.Generic;
using MarginTrading.Backend.Contracts.Account;
using MarginTrading.Backend.Contracts.Positions;
using MessagePack;

namespace MarginTrading.Backend.Contracts.Workflow.Liquidation.Events
{
    /// <summary>
    /// Event of successful liquidation finish
    /// </summary>
    [MessagePackObject]
    public class LiquidationFinishedEvent
    {
        /// <summary>
        /// Operation id
        /// </summary>
        [Key(0)]
        public string OperationId { get; set; }
        
        /// <summary>
        /// Event creation time
        /// </summary>
        [Key(1)]
        public DateTime CreationTime { get; set; }
        
        /// <summary>
        /// Type of liquidation
        /// </summary>
        [Key(2)]
        public LiquidationTypeContract LiquidationType { get; set; }
        
        /// <summary>
        /// Account id
        /// </summary>
        [Key(3)]
        public string AccountId { get; set; }
        
        /// <summary>
        /// Asset pair id
        /// </summary>
        [Key(4)]
        public string AssetPairId { get; set; }
        
        /// <summary>
        /// Position direction
        /// </summary>
        [Key(5)]
        public PositionDirectionContract? Direction { get; set; }
        
        /// <summary>
        /// If liquidation was caused by the quote, here serialized quote state is stored
        /// </summary>
        [Key(6)]
        public string QuoteInfo { get; set; }
        
        /// <summary>
        /// Processed position ids
        /// </summary>
        [Key(7)]
        public List<string> ProcessedPositionIds { get; set; }
        
        /// <summary>
        /// Liquidated position ids
        /// </summary>
        [Key(8)]
        public List<string> LiquidatedPositionIds { get; set; }
        
        /// <summary>
        /// Number of positions remaining on account at the end of liquidation
        /// </summary>
        [Key(9)]
        public int OpenPositionsRemainingOnAccount { get; set; }
        
        /// <summary>
        /// Total capital at the end of liquidation
        /// </summary>
        [Key(10)]
        public decimal CurrentTotalCapital { get; set; }
        
        /// <summary>
        /// Account level at the end of liquidation
        /// </summary>
        [Key(11)]
        public AccountLevelContract AccountLevel { get; set; }
    }
}