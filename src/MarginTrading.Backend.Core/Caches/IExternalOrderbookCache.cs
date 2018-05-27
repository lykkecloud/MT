using System;
using System.Collections.Generic;
using MarginTrading.Backend.Core.Orderbooks;

namespace MarginTrading.Backend.Core
{
    public interface IExternalOrderbookCache
    {
        TResult TryReadValue<TResult>(string assetPair, Func<bool, string ,
            Dictionary<string, ExternalOrderBook>, TResult> readFunc);

        ExternalOrderBook AddOrUpdate((string, string) key, Func<(string, string), ExternalOrderBook> valueFactory,
            Func<(string, string), ExternalOrderBook, ExternalOrderBook> updateValueFactory);
    }
}