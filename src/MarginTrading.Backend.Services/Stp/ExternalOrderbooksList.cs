using System;
using System.Collections.Generic;
using System.Linq;
using Common;
using Common.Log;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Orderbooks;
using MarginTrading.Backend.Services.Events;
using MarginTrading.Common.Extensions;
using MarginTrading.Common.Helpers;
using MarginTrading.Common.Services;

namespace MarginTrading.Backend.Services.Stp
{
    public class ExternalOrderBooksList
    {
        private readonly IEventChannel<BestPriceChangeEventArgs> _bestPriceChangeEventChannel;
        private readonly IDateService _dateService;
        private readonly ILog _log;
        private readonly IExternalOrderbookCache _orderbooks;

        public ExternalOrderBooksList(IEventChannel<BestPriceChangeEventArgs> bestPriceChangeEventChannel,
            IDateService dateService,
            IExternalOrderbookCache orderbooks,
            ILog log)
        {
            _bestPriceChangeEventChannel = bestPriceChangeEventChannel;
            _dateService = dateService;
            _orderbooks = orderbooks;
            _log = log;
        }

        /// <summary>
        /// External orderbooks cache (AssetPairId, (Source, Orderbook))
        /// </summary>
        /// <remarks>
        /// We assume that AssetPairId is unique in LegalEntity + STP mode. <br/>
        /// Note that it is unsafe to even read the inner dictionary without locking.
        /// Please use <see cref="ReadWriteLockedDictionary{TKey,TValue}.TryReadValue{TResult}"/> for this purpose.
        /// </remarks>

        public List<(string source, decimal? price)> GetPricesForOpen(IOrder order)
        {
            return _orderbooks.TryReadValue(order.Instrument, (dataExist, assetPairId, orderbooks)
                => dataExist
                    ? orderbooks.Select(p => (p.Key, MatchBestPriceForOrder(p.Value, order, true))).ToList()
                    : null);
        }

        public decimal? GetPriceForClose(IOrder order)
        {
            decimal? CalculatePriceForClose(Dictionary<string, ExternalOrderBook> orderbooks)
            {
                if (!orderbooks.TryGetValue(order.OpenExternalProviderId, out var orderBook))
                {
                    return null;
                }

                return MatchBestPriceForOrder(orderBook, order, false);
            }

            return _orderbooks.TryReadValue(order.Instrument, (dataExist, assetPairId, orderbooks)
                => dataExist ? CalculatePriceForClose(orderbooks) : null);
        }

        //TODO: understand which orderbook should be used (best price? aggregated?)
        public ExternalOrderBook GetOrderBook(string assetPairId)
        {
            return _orderbooks.TryReadValue(assetPairId,
                (exists, assetPair, orderbooks) => orderbooks.Values.FirstOrDefault());
        }

        private static decimal? MatchBestPriceForOrder(ExternalOrderBook externalOrderbook, IOrder order, bool isOpening)
        {
            var direction = isOpening ? order.GetOrderType() : order.GetCloseType();
            var volume = Math.Abs(order.Volume);

            return externalOrderbook.GetMatchedPrice(volume, direction);
        }

        public void SetOrderbook(ExternalOrderBook orderbook)
        {
            if (!ValidateOrderbook(orderbook))
                return;

            var bba = new InstrumentBidAskPair
            {
                Bid = 0,
                Ask = decimal.MaxValue,
                Date = orderbook.Timestamp,
                Instrument = orderbook.AssetPairId
            };

            ExternalOrderBook UpdateOrderbooksDictionary((string, string) key, ExternalOrderBook oldValue)
            {
                // guaranteed to be sorted best first
                var bestBid = orderbook.Bids.First().Price;
                var bestAsk = orderbook.Asks.First().Price;
                if (bestBid > bba.Bid)
                    bba.Bid = bestBid;

                if (bestAsk < bba.Ask)
                    bba.Ask = bestAsk;
            
                return orderbook;
            }

            _orderbooks.AddOrUpdate((orderbook.AssetPairId, orderbook.ExchangeName),
                k => UpdateOrderbooksDictionary(k, null),
                UpdateOrderbooksDictionary);

            _bestPriceChangeEventChannel.SendEvent(this, new BestPriceChangeEventArgs(bba));
        }
        
        //TODO: sort prices of uncomment validation
        private bool ValidateOrderbook(ExternalOrderBook orderbook)
        {
            try
            {
                orderbook.AssetPairId.RequiredNotNullOrWhiteSpace("orderbook.AssetPairId");
                orderbook.ExchangeName.RequiredNotNullOrWhiteSpace("orderbook.ExchangeName");
                orderbook.RequiredNotNull(nameof(orderbook));
                
                orderbook.Bids.RequiredNotNullOrEmpty("orderbook.Bids");
                orderbook.Bids.RemoveAll(e => e == null || e.Price <= 0 || e.Volume == 0);
                //ValidatePricesSorted(orderbook.Bids, false);
                
                orderbook.Asks.RequiredNotNullOrEmpty("orderbook.Asks");
                orderbook.Asks.RemoveAll(e => e == null || e.Price <= 0 || e.Volume == 0);
                //ValidatePricesSorted(orderbook.Asks, true);

                return true;
            }
            catch (Exception e)
            {
                _log.WriteError(nameof(ExternalOrderBooksList), orderbook.ToJson(), e);
                return false;
            }
        }

        private void ValidatePricesSorted(IEnumerable<VolumePrice> volumePrices, bool ascending)
        {
            decimal? previous = null;
            foreach (var current in volumePrices.Select(p => p.Price))
            {
                if (previous != null && ascending ? current < previous : current > previous)
                {
                    throw new Exception("Prices should be sorted best first");
                }
                
                previous = current;
            }
        }
    }
}