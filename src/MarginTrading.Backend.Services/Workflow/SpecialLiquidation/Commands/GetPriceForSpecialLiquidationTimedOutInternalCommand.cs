using System;
using MessagePack;

namespace MarginTrading.Backend.Services.Workflow.SpecialLiquidation.Commands
{
    [MessagePackObject]
    public class GetPriceForSpecialLiquidationTimedOutInternalCommand
    {
        [Key(0)]
        public string OperationId { get; set; }
        
        [Key(1)]
        public DateTime CreationTime { get; set; }
        
        [Key(2)]
        public int TimeoutMilliseconds { get; set; }
    }
}