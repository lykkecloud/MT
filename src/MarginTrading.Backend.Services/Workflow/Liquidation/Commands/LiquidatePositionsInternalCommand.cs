using System;
using MessagePack;

namespace MarginTrading.Backend.Services.Workflow.Liquidation.Commands
{
    [MessagePackObject]
    public class LiquidatePositionsInternalCommand
    {
        [Key(0)]
        public string OperationId { get; set; }
        
        [Key(1)]
        public DateTime CreationTime { get; set; }
    }
}