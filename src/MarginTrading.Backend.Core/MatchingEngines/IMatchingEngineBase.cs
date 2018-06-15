﻿using System;
using System.Threading.Tasks;
using MarginTrading.Backend.Core.MatchedOrders;
using MarginTrading.Backend.Core.Orderbooks;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Core.Trading;

namespace MarginTrading.Backend.Core.MatchingEngines
{
    public interface IMatchingEngineBase
    {
        string Id { get; }
        
        MatchingEngineMode Mode { get; }
        
        Task MatchMarketOrderForOpenAsync(Order order, Func<MatchedOrderCollection, bool> orderProcessed);
        
        //Task MatchMarketOrderForCloseAsync(Position order, Func<MatchedOrderCollection, bool> orderProcessed);
        
        decimal? GetPriceForClose(Position order);
        
        OrderBook GetOrderBook(string instrument);
    }
}
