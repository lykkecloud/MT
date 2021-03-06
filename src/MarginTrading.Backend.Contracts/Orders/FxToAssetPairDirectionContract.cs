// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace MarginTrading.Backend.Contracts.Orders
{
    /// <summary>
    /// Shows if account asset id is directly related on asset pair quote asset.
    /// </summary>
    public enum FxToAssetPairDirectionContract
    {
        /// <summary>
        /// AssetPair is {BaseId, QuoteId} and FxAssetPair is {QuoteId, AccountAssetId}
        /// </summary>
        Straight,
        
        /// <summary>
        /// AssetPair is {BaseId, QuoteId} and FxAssetPair is {AccountAssetId, QuoteId}
        /// </summary>
        Reverse,
    }
}