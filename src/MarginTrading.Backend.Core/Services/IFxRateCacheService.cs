using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarginTrading.Backend.Core.Services
{
    public interface IFxRateCacheService
    {
        InstrumentBidAskPair GetQuote(string instrument);
        Dictionary<string, InstrumentBidAskPair> GetAllQuotes();
        bool TryGetQuoteById(string instrument, out InstrumentBidAskPair result); 
        void RemoveQuote(string assetPair);
        Task SetQuote(PumpQuoteMessage quote);
        void SetQuote(InstrumentBidAskPair bidAskPair);
    }
}