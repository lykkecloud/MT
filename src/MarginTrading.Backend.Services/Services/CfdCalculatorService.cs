using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Services;

namespace MarginTrading.Backend.Services
{
    public class CfdCalculatorService : ICfdCalculatorService
    {
        private readonly IAssetPairsCache _assetPairsCache;
        private readonly IFxRateCacheService _fxRateCacheService;

        public CfdCalculatorService(
            IAssetPairsCache assetPairsCache,
            IFxRateCacheService fxRateCacheService)
        {
            _assetPairsCache = assetPairsCache;
            _fxRateCacheService = fxRateCacheService;
        }

        public decimal GetQuoteRateForBaseAsset(string accountAssetId, string assetPairId, string legalEntity, 
            bool metricIsPositive = true)
        {
            var assetPair = _assetPairsCache.GetAssetPairById(assetPairId);
            
            if (accountAssetId == assetPair.BaseAssetId)
                return 1;

            var assetPairSubst = _assetPairsCache.FindAssetPair(assetPair.BaseAssetId, accountAssetId, legalEntity);

            var rate = metricIsPositive
                ? assetPairSubst.BaseAssetId == assetPair.BaseAssetId
                    ? _fxRateCacheService.GetQuote(assetPairSubst.Id).Ask
                    : 1 / _fxRateCacheService.GetQuote(assetPairSubst.Id).Bid
                : assetPairSubst.BaseAssetId == assetPair.BaseAssetId
                    ? _fxRateCacheService.GetQuote(assetPairSubst.Id).Bid
                    : 1 / _fxRateCacheService.GetQuote(assetPairSubst.Id).Ask;
            
            return rate;
        }

        public decimal GetQuoteRateForQuoteAsset(string accountAssetId, string assetPairId, string legalEntity, 
            bool metricIsPositive = true)
        {
            var assetPair = _assetPairsCache.GetAssetPairById(assetPairId);
            
            if (accountAssetId == assetPair.QuoteAssetId)
                return 1;

            var assetPairSubst = _assetPairsCache.FindAssetPair(assetPair.QuoteAssetId, accountAssetId, legalEntity);
           
            var rate = metricIsPositive
                ? assetPairSubst.BaseAssetId == assetPair.QuoteAssetId
                    ? _fxRateCacheService.GetQuote(assetPairSubst.Id).Ask
                    : 1 / _fxRateCacheService.GetQuote(assetPairSubst.Id).Bid
                : assetPairSubst.BaseAssetId == assetPair.QuoteAssetId
                    ? _fxRateCacheService.GetQuote(assetPairSubst.Id).Bid
                    : 1 / _fxRateCacheService.GetQuote(assetPairSubst.Id).Ask;
            
            return rate;
        }
    }
}