﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Common.Log;
using JetBrains.Annotations;
using Lykke.Common;
using Lykke.Common.Log;
using MarginTrading.Backend.Contracts.Activities;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Exceptions;
using MarginTrading.Backend.Core.MatchedOrders;
using MarginTrading.Backend.Core.MatchingEngines;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Core.Repositories;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Core.Trading;
using MarginTrading.Backend.Services.AssetPairs;
using MarginTrading.Backend.Services.Events;
using MarginTrading.Backend.Services.Helpers;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Backend.Services.Workflow.Liquidation.Commands;
using MarginTrading.Backend.Services.Workflow.SpecialLiquidation.Commands;
using MarginTrading.Common.Extensions;
using MarginTrading.Common.Services;
using MoreLinq;

namespace MarginTrading.Backend.Services
{
    public sealed class TradingEngine : ITradingEngine,
        IEventConsumer<BestPriceChangeEventArgs>,
        IEventConsumer<FxBestPriceChangeEventArgs>
    {
        private readonly IEventChannel<MarginCallEventArgs> _marginCallEventChannel;
        private readonly IEventChannel<OrderPlacedEventArgs> _orderPlacedEventChannel;
        private readonly IEventChannel<OrderExecutedEventArgs> _orderExecutedEventChannel;
        private readonly IEventChannel<OrderCancelledEventArgs> _orderCancelledEventChannel;
        private readonly IEventChannel<OrderChangedEventArgs> _orderChangedEventChannel;
        private readonly IEventChannel<OrderExecutionStartedEventArgs> _orderExecutionStartedEvenChannel;
        private readonly IEventChannel<OrderActivatedEventArgs> _orderActivatedEventChannel;
        private readonly IEventChannel<OrderRejectedEventArgs> _orderRejectedEventChannel;

        private readonly IValidateOrderService _validateOrderService;
        private readonly IAccountsCacheService _accountsCacheService;
        private readonly OrdersCache _ordersCache;
        private readonly IMatchingEngineRouter _meRouter;
        private readonly IThreadSwitcher _threadSwitcher;
        private readonly IAssetPairDayOffService _assetPairDayOffService;
        private readonly ILog _log;
        private readonly IDateService _dateService;
        private readonly ICfdCalculatorService _cfdCalculatorService;
        private readonly IIdentityGenerator _identityGenerator;
        private readonly IAssetPairsCache _assetPairsCache;
        private readonly ICqrsSender _cqrsSender;
        private readonly IEventChannel<StopOutEventArgs> _stopOutEventChannel;
        private readonly IQuoteCacheService _quoteCacheService;
        private readonly MarginTradingSettings _marginTradingSettings;
        private readonly LiquidationHelper _liquidationHelper;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _accountSemaphores =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        public TradingEngine(
            IEventChannel<MarginCallEventArgs> marginCallEventChannel,
            IEventChannel<OrderPlacedEventArgs> orderPlacedEventChannel,
            IEventChannel<OrderExecutedEventArgs> orderClosedEventChannel,
            IEventChannel<OrderCancelledEventArgs> orderCancelledEventChannel, 
            IEventChannel<OrderChangedEventArgs> orderChangedEventChannel,
            IEventChannel<OrderExecutionStartedEventArgs> orderExecutionStartedEventChannel,
            IEventChannel<OrderActivatedEventArgs> orderActivatedEventChannel, 
            IEventChannel<OrderRejectedEventArgs> orderRejectedEventChannel,
            IValidateOrderService validateOrderService,
            IAccountsCacheService accountsCacheService,
            OrdersCache ordersCache,
            IMatchingEngineRouter meRouter,
            IThreadSwitcher threadSwitcher,
            IAssetPairDayOffService assetPairDayOffService,
            ILog log,
            IDateService dateService,
            ICfdCalculatorService cfdCalculatorService,
            IIdentityGenerator identityGenerator,
            IAssetPairsCache assetPairsCache,
            ICqrsSender cqrsSender,
            IEventChannel<StopOutEventArgs> stopOutEventChannel,
            IQuoteCacheService quoteCacheService,
            MarginTradingSettings marginTradingSettings,
            LiquidationHelper liquidationHelper)
        {
            _marginCallEventChannel = marginCallEventChannel;
            _orderPlacedEventChannel = orderPlacedEventChannel;
            _orderExecutedEventChannel = orderClosedEventChannel;
            _orderCancelledEventChannel = orderCancelledEventChannel;
            _orderActivatedEventChannel = orderActivatedEventChannel;
            _orderExecutionStartedEvenChannel = orderExecutionStartedEventChannel;
            _orderChangedEventChannel = orderChangedEventChannel;
            _orderRejectedEventChannel = orderRejectedEventChannel;

            _validateOrderService = validateOrderService;
            _accountsCacheService = accountsCacheService;
            _ordersCache = ordersCache;
            _meRouter = meRouter;
            _threadSwitcher = threadSwitcher;
            _assetPairDayOffService = assetPairDayOffService;
            _log = log;
            _dateService = dateService;
            _cfdCalculatorService = cfdCalculatorService;
            _identityGenerator = identityGenerator;
            _assetPairsCache = assetPairsCache;
            _cqrsSender = cqrsSender;
            _stopOutEventChannel = stopOutEventChannel;
            _quoteCacheService = quoteCacheService;
            _marginTradingSettings = marginTradingSettings;
            _liquidationHelper = liquidationHelper;
        }

        public async Task<Order> PlaceOrderAsync(Order order)
        {
            _orderPlacedEventChannel.SendEvent(this, new OrderPlacedEventArgs(order));
            
            try
            {
                if (order.OrderType != OrderType.Market)
                {
                    await PlacePendingOrder(order);
                    return order;
                }

                return await PlaceOrderByMarketPrice(order);
            }
            catch (ValidateOrderException ex)
            {
                RejectOrder(order, ex.RejectReason, ex.Message, ex.Comment);
                return order;
            }
            catch (Exception ex)
            {
                RejectOrder(order, OrderRejectReason.TechnicalError, ex.Message);
                _log.WriteError(nameof(TradingEngine), nameof(PlaceOrderAsync), ex);
                return order;
            }
        }
        
        private async Task<Order> PlaceOrderByMarketPrice(Order order)
        {
            try
            {
                var me = _meRouter.GetMatchingEngineForExecution(order);

                foreach (var positionId in order.PositionsToBeClosed)
                {
                    if (!_ordersCache.Positions.TryGetPositionById(positionId, out var position))
                    {
                        RejectOrder(order, OrderRejectReason.ParentPositionDoesNotExist, positionId);
                        return order;
                    }

                    if (position.Status != PositionStatus.Active)
                    {
                        RejectOrder(order, OrderRejectReason.ParentPositionIsNotActive, positionId);
                        return order;
                    }

                    position.StartClosing(_dateService.Now(), order.OrderType.GetCloseReason(), order.Originator, "");
                }

                return await ExecuteOrderByMatchingEngineAsync(order, me, true);
            }
            catch (Exception ex)
            {
                var reason = ex is QuoteNotFoundException
                    ? OrderRejectReason.NoLiquidity
                    : OrderRejectReason.TechnicalError;
                RejectOrder(order, reason, ex.Message);
                _log.WriteError(nameof(TradingEngine), nameof(PlaceOrderByMarketPrice), ex);
                return order;
            }
        }

        private async Task<Order> ExecutePendingOrder(Order order)
        {
            await PlaceOrderByMarketPrice(order);

            if (order.IsExecutionNotStarted)
            {
                foreach (var positionId in order.PositionsToBeClosed)
                {
                    if (_ordersCache.Positions.TryGetPositionById(positionId, out var position)
                        && position.Status == PositionStatus.Closing)
                    {
                        position.CancelClosing(_dateService.Now());
                    }
                }
            }

            return order;
        }

        private async Task PlacePendingOrder(Order order)
        {
            if (order.IsBasicPendingOrder() || !string.IsNullOrEmpty(order.ParentPositionId))
            {
                Position parentPosition = null;
                
                if (!string.IsNullOrEmpty(order.ParentPositionId))
                {
                    parentPosition = _ordersCache.Positions.GetPositionById(order.ParentPositionId);
                    parentPosition.AddRelatedOrder(order);
                }

                order.Activate(_dateService.Now(), false, parentPosition?.ClosePrice);
                _ordersCache.Active.Add(order);
                _orderActivatedEventChannel.SendEvent(this, new OrderActivatedEventArgs(order));
            }
            else if (!string.IsNullOrEmpty(order.ParentOrderId))
            {
                if (_ordersCache.TryGetOrderById(order.ParentOrderId, out var parentOrder))
                {
                    parentOrder.AddRelatedOrder(order);
                    order.MakeInactive(_dateService.Now());
                    _ordersCache.Inactive.Add(order);
                    return;
                }

                //may be it was market and now it is position
                if (_ordersCache.Positions.TryGetPositionById(order.ParentOrderId, out var parentPosition))
                {
                    parentPosition.AddRelatedOrder(order);
                    if (parentPosition.Volume != -order.Volume)
                    {
                        order.ChangeVolume(-parentPosition.Volume, _dateService.Now(), OriginatorType.System);
                    }

                    order.Activate(_dateService.Now(), true, parentPosition.ClosePrice);
                    _ordersCache.Active.Add(order);
                    _orderActivatedEventChannel.SendEvent(this, new OrderActivatedEventArgs(order));
                }
                else
                {
                    order.MakeInactive(_dateService.Now());
                    _ordersCache.Inactive.Add(order);
                    CancelPendingOrder(order.Id, order.AdditionalInfo,
                        _identityGenerator.GenerateAlphanumericId(),
                        $"Parent order closed the position, so {order.OrderType.ToString()} order is cancelled");
                }
            }
            else
            {
                throw new ValidateOrderException(OrderRejectReason.InvalidParent, "Order parent is not valid");
            }

            await ExecutePendingOrderIfNeededAsync(order);
        }

        private async Task<Order> ExecuteOrderByMatchingEngineAsync(Order order, IMatchingEngineBase matchingEngine,
            bool checkStopout, OrderModality modality = OrderModality.Regular)
        {
            var semaphore = _accountSemaphores.GetOrAdd(order.AccountId, new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync();

            try
            {
                var now = _dateService.Now();

                //just in case )
                if (CheckIfOrderIsExpired(order, now))
                {
                    return order;
                }

                order.StartExecution(_dateService.Now(), matchingEngine.Id);

                _orderExecutionStartedEvenChannel.SendEvent(this, new OrderExecutionStartedEventArgs(order));

                ChangeOrderVolumeIfNeeded(order);

                var equivalentRate = _cfdCalculatorService.GetQuoteRateForQuoteAsset(order.EquivalentAsset,
                    order.AssetPairId, order.LegalEntity);
                var fxRate = _cfdCalculatorService.GetQuoteRateForQuoteAsset(order.AccountAssetId,
                    order.AssetPairId, order.LegalEntity);

                order.SetRates(equivalentRate, fxRate);

                var matchOnPositionsResult = MatchOnExistingPositions(order);

                if (modality == OrderModality.Regular && order.Originator != OriginatorType.System)
                {
                    try
                    {
                        _validateOrderService.MakePreTradeValidation(
                            order,
                            matchOnPositionsResult.WillOpenPosition,
                            matchingEngine, 
                            matchOnPositionsResult.ReleasedMargin);
                    }
                    catch (ValidateOrderException ex)
                    {
                        RejectOrder(order, ex.RejectReason, ex.Message, ex.Comment);
                        return order;
                    }
                }

                MatchedOrderCollection matchedOrders;
                try
                {
                    matchedOrders = await matchingEngine.MatchOrderAsync(order, matchOnPositionsResult.WillOpenPosition, modality);
                }
                catch (OrderExecutionTechnicalException)
                {
                    RejectOrder(order, OrderRejectReason.TechnicalError, $"Unexpected reject (Order ID: {order.Id})");
                    return order;
                }

                if (!matchedOrders.Any())
                {
                    RejectOrder(order, OrderRejectReason.NoLiquidity, "No orders to match", "");
                    return order;
                }

                if (matchedOrders.SummaryVolume < Math.Abs(order.Volume))
                {
                    if (order.FillType == OrderFillType.FillOrKill)
                    {
                        RejectOrder(order, OrderRejectReason.NoLiquidity, "Not fully matched", "");
                        return order;
                    }
                    else
                    {
                        order.PartiallyExecute(_dateService.Now(), matchedOrders);
                        _ordersCache.InProgress.Add(order);
                        return order;
                    }
                }

                if (order.Status == OrderStatus.ExecutionStarted)
                {
                    var accuracy = _assetPairsCache.GetAssetPairByIdOrDefault(order.AssetPairId)?.Accuracy ??
                                   AssetPairsCache.DefaultAssetPairAccuracy;

                    order.Execute(_dateService.Now(), matchedOrders, accuracy);

                    _orderExecutedEventChannel.SendEvent(this, new OrderExecutedEventArgs(order));

                    if (checkStopout)
                    {
                        CheckStopout(order);
                    }
                }

                return order;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private bool CheckIfOrderIsExpired(Order order, DateTime now)
        {
            if (order.OrderType != OrderType.Market &&
                order.Validity.HasValue &&
                now.Date > order.Validity.Value.Date)
            {
                order.Expire(now);
                _orderCancelledEventChannel.SendEvent(this,
                    new OrderCancelledEventArgs(order,
                        new OrderCancelledMetadata {Reason = OrderCancellationReasonContract.Expired}));
                return true;
            }

            return false;
        }
        
        private void ChangeOrderVolumeIfNeeded(Order order)
        {
            if (order.PositionsToBeClosed.Any())
            {
                var netVolume = 0M;
                var rejectReason = default(OrderRejectReason?);
                foreach (var positionId in order.PositionsToBeClosed)
                {
                    if (!_ordersCache.Positions.TryGetPositionById(positionId, out var position))
                    {
                        rejectReason = OrderRejectReason.ParentPositionDoesNotExist;
                        continue;
                    }

                    if (position.Status != PositionStatus.Closing)
                    {
                        rejectReason = OrderRejectReason.TechnicalError;
                        continue;
                    }

                    netVolume += position.Volume;
                }

                if (netVolume == 0M && rejectReason.HasValue)
                {
                    order.Reject(rejectReason.Value,
                        rejectReason.Value == OrderRejectReason.ParentPositionDoesNotExist
                            ? "Related position does not exist"
                            : "Related position is not in closing state", "", _dateService.Now());
                    _orderRejectedEventChannel.SendEvent(this, new OrderRejectedEventArgs(order));
                    return;
                }

                // there is no any global lock of positions / orders, that's why it is possible to have concurrency 
                // in position close process
                // since orders, that have not empty PositionsToBeClosed should close positions and not open new ones
                // volume of executed order should be equal to position volume, but should have opposite sign
                if (order.Volume != -netVolume)
                {
                    var metadata = new OrderChangedMetadata
                    {
                        OldValue = order.Volume.ToString("F2"),
                        UpdatedProperty = OrderChangedProperty.Volume
                    };
                    order.ChangeVolume(-netVolume, _dateService.Now(), order.Originator);
                    _orderChangedEventChannel.SendEvent(this, new OrderChangedEventArgs(order, metadata));
                }
            }
        }
        
        private void CheckStopout(Order order)
        {
            var account = _accountsCacheService.Get(order.AccountId);
            var accountLevel = account.GetAccountLevel();

            if (accountLevel == AccountLevel.StopOut)
            {
                CommitStopOut(account, null);
            }
            else if (accountLevel > AccountLevel.None)
            {
                _marginCallEventChannel.SendEvent(this, new MarginCallEventArgs(account, accountLevel));
            }
        }

        public (bool WillOpenPosition, decimal ReleasedMargin) MatchOnExistingPositions(Order order)
        {
            if (order.ForceOpen)
                return (true, 0);

            var existingPositions =
                _ordersCache.Positions.GetPositionsByInstrumentAndAccount(order.AssetPairId, order.AccountId);
            
            if (order.PositionsToBeClosed.Any())
            {
                var targetPositionsMargin = existingPositions.Where(p => order.PositionsToBeClosed.Contains(p.Id))
                    .Sum(p => p.GetMarginMaintenance());

                return (false, targetPositionsMargin);
            }

            var oppositeDirectionPositions =
                existingPositions.Where(p =>
                        p.Status == PositionStatus.Active && p.Direction == order.Direction.GetClosePositionDirection())
                    .ToArray();
            
            var oppositeDirectionVolume = 0m;
            var oppositeDirectionMargin = 0m;

            foreach (var position in oppositeDirectionPositions)
            {
                oppositeDirectionVolume += position.Volume;
                oppositeDirectionMargin += position.GetMarginMaintenance();
            }

            if (Math.Abs(oppositeDirectionVolume) < Math.Abs(order.Volume))
            {
                return (true, oppositeDirectionMargin);
            }

            var wightedMargin = oppositeDirectionMargin / Math.Abs(oppositeDirectionVolume) * Math.Abs(order.Volume);

            return (false, wightedMargin);
        }

        private void RejectOrder(Order order, OrderRejectReason reason, string message, string comment = null)
        {
            if (order.OrderType == OrderType.Market 
                || reason != OrderRejectReason.NoLiquidity
                || order.PendingOrderRetriesCount >= _marginTradingSettings.PendingOrderRetriesThreshold)
            {
                order.Reject(reason, message, comment, _dateService.Now());

                _log.WriteWarning(
                    nameof(TradingEngine), 
                    nameof(RejectOrder), 
                    new
                    {
                        order,
                        reason,
                        message,
                        comment
                    }.ToJson());

                _orderRejectedEventChannel.SendEvent(this, new OrderRejectedEventArgs(order));
            }
            //TODO: think how to avoid infinite loop
            else if (!_ordersCache.TryGetOrderById(order.Id, out _)) // all pending orders should be returned to active state if there is no liquidity
            {
                order.CancelExecution(_dateService.Now());
                
                _ordersCache.Active.Add(order);

                var initialAdditionalInfo = order.AdditionalInfo;
                //to evade additional OnBehalf fee for this event
                order.AdditionalInfo = initialAdditionalInfo.MakeNonOnBehalf();
                
                _orderChangedEventChannel.SendEvent(this,
                    new OrderChangedEventArgs(order,
                        new OrderChangedMetadata {UpdatedProperty = OrderChangedProperty.None}));

                order.AdditionalInfo = initialAdditionalInfo;
            }
        }

        #region Orders waiting for execution

        private void ProcessOrdersWaitingForExecution(InstrumentBidAskPair quote)
        {
            //TODO: MTC-155
            //ProcessPendingOrdersMarginRecalc(instrument);

            var orders = GetPendingOrdersToBeExecuted(quote).GetSortedForExecution();
            
            if (!orders.Any())
                return;

            foreach (var order in orders)
            {
                _threadSwitcher.SwitchThread(async () =>
                {
                    await ExecutePendingOrder(order);
                });
            }
        }

        private IEnumerable<Order> GetPendingOrdersToBeExecuted(InstrumentBidAskPair quote)
        {
            var pendingOrders = _ordersCache.Active.GetOrdersByInstrument(quote.Instrument);

            foreach (var order in pendingOrders)
            {
                var price = quote.GetPriceForOrderDirection(order.Direction);

                if (order.IsSuitablePriceForPendingOrder(price) &&
                    _validateOrderService.CheckIfPendingOrderExecutionPossible(order.AssetPairId, order.OrderType,
                        MatchOnExistingPositions(order).WillOpenPosition))
                {
                    if (quote.GetVolumeForOrderDirection(order.Direction) >= Math.Abs(order.Volume))
                    {
                        _ordersCache.Active.Remove(order);
                        yield return order;
                    }
                    else //let's validate one more time, considering orderbook depth
                    {
                        var me = _meRouter.GetMatchingEngineForExecution(order);
                        var executionPriceInfo = me.GetBestPriceForOpen(order.AssetPairId, order.Volume);
                        
                        if (executionPriceInfo.price.HasValue && order.IsSuitablePriceForPendingOrder(executionPriceInfo.price.Value))
                        {
                            _ordersCache.Active.Remove(order);
                            yield return order;
                        }
                    }
                }

            }
        }

        public void ProcessExpiredOrders(DateTime operationIntervalEnd)
        {
            var pendingOrders = _ordersCache.Active.GetAllOrders();
            var now = _dateService.Now();

            foreach (var order in pendingOrders)
            {
                if (order.Validity.HasValue && operationIntervalEnd.Date > order.Validity.Value.Date)
                {
                    _ordersCache.Active.Remove(order);
                    order.Expire(now);
                    _orderCancelledEventChannel.SendEvent(
                        this,
                        new OrderCancelledEventArgs(
                            order,
                            new OrderCancelledMetadata {Reason = OrderCancellationReasonContract.Expired}));
                }
            }
        }

//        private void ProcessPendingOrdersMarginRecalc(string instrument)
//        {
//            var pendingOrders = _ordersCache.GetPendingForMarginRecalc(instrument);
//
//            foreach (var pendingOrder in pendingOrders)
//            {
//                pendingOrder.UpdatePendingOrderMargin();
//            }
//        }

        #endregion

        
        #region Positions

        private void UpdatePositionsFxRates(InstrumentBidAskPair quote)
        {
            foreach (var position in _ordersCache.GetPositionsByFxAssetPairId(quote.Instrument))
            {
                var fxPrice = _cfdCalculatorService.GetPrice(quote, position.FxToAssetPairDirection,
                    position.Volume * (position.ClosePrice - position.OpenPrice) > 0);

                position.UpdateCloseFxPrice(fxPrice);
            }
        }

        private void ProcessPositions(InstrumentBidAskPair quote, bool allowCommitStopOut)
        {
            var stopoutAccounts = UpdateClosePriceAndDetectStopout(quote);
            
            if(allowCommitStopOut)
            {
                foreach (var account in stopoutAccounts)
                    CommitStopOut(account, quote);
            }
        }

        private List<MarginTradingAccount> UpdateClosePriceAndDetectStopout(InstrumentBidAskPair quote)
        {
            var positionsByAccounts = _ordersCache.Positions.GetPositionsByInstrument(quote.Instrument)
                .GroupBy(x => x.AccountId).ToDictionary(x => x.Key, x => x.ToArray());

            var accountsWithStopout = new List<MarginTradingAccount>();

            foreach (var accountPositions in positionsByAccounts)
            {
                var account = _accountsCacheService.Get(accountPositions.Key);
                var oldAccountLevel = account.GetAccountLevel();

                foreach (var position in accountPositions.Value)
                {
                    var closeOrderDirection = position.Volume.GetClosePositionOrderDirection();
                    var closePrice = quote.GetPriceForOrderDirection(closeOrderDirection);

                    if (quote.GetVolumeForOrderDirection(closeOrderDirection) < Math.Abs(position.Volume))
                    {
                        var defaultMatchingEngine = _meRouter.GetMatchingEngineForClose(position.OpenMatchingEngineId);

                        var orderbookPrice = defaultMatchingEngine.GetPriceForClose(position.AssetPairId, position.Volume,
                            position.ExternalProviderId);

                        if (orderbookPrice.HasValue)
                            closePrice = orderbookPrice.Value;
                    }
                    
                    if (closePrice != 0)
                    {
                        position.UpdateClosePriceWithoutAccountUpdate(closePrice);

                        UpdateTrailingStops(position);
                    }
                }
                
                account.CacheNeedsToBeUpdated();

                var newAccountLevel = account.GetAccountLevel();

                if (newAccountLevel == AccountLevel.StopOut)
                    accountsWithStopout.Add(account);

                if (oldAccountLevel != newAccountLevel)
                {
                    _marginCallEventChannel.SendEvent(this, new MarginCallEventArgs(account, newAccountLevel));
                }
            }
            
            return accountsWithStopout;
        }

        private void UpdateTrailingStops(Position position)
        {
            var trailingOrderIds = position.RelatedOrders.Where(o => o.Type == OrderType.TrailingStop)
                .Select(o => o.Id);

            foreach (var trailingOrderId in trailingOrderIds)
            {
                if (_ordersCache.TryGetOrderById(trailingOrderId, out var trailingOrder)
                    && trailingOrder.Price.HasValue)
                {
                    if (trailingOrder.TrailingDistance.HasValue)
                    {
                        var currentDistance = trailingOrder.Price.Value - position.ClosePrice;
                        
                        if (Math.Abs(currentDistance) > Math.Abs(trailingOrder.TrailingDistance.Value)
                        && Math.Sign(currentDistance) == Math.Sign(trailingOrder.TrailingDistance.Value))
                        {
                            var newPrice = position.ClosePrice + trailingOrder.TrailingDistance.Value;
                            var oldPrice = trailingOrder.Price;
                            trailingOrder.ChangePrice(newPrice,
                                _dateService.Now(),
                                trailingOrder.Originator,
                                null,
                                _identityGenerator.GenerateGuid()); //todo in fact price change correlationId must be used

                            _log.WriteInfoAsync(nameof(TradingEngine), nameof(UpdateTrailingStops),
                                $"Price for trailing stop order {trailingOrder.Id} changed. " +
                                $"Old price: {oldPrice}. " +
                                $"New price: {trailingOrder.Price}");
                        }
                    }
                    else
                    {
                        trailingOrder.SetTrailingDistance(position.ClosePrice);
                    }
                }
            }
        }

        private void CommitStopOut(MarginTradingAccount account, InstrumentBidAskPair quote)
        {
            if (account.IsInLiquidation())
            {
                return;
            }

            var liquidationType = account.GetUsedMargin() == account.GetCurrentlyUsedMargin()
                ? LiquidationType.Normal
                : LiquidationType.Mco;

            _cqrsSender.SendCommandToSelf(new StartLiquidationInternalCommand
            {
                OperationId = _identityGenerator.GenerateGuid(),//TODO: use quote correlationId
                AccountId = account.Id,
                CreationTime = _dateService.Now(),
                QuoteInfo = quote?.ToJson(),
                LiquidationType = liquidationType,
                OriginatorType = OriginatorType.System,
            });

            _stopOutEventChannel.SendEvent(this, new StopOutEventArgs(account));
        }

        public async Task<(PositionCloseResult, Order)> ClosePositionsAsync(PositionsCloseData closeData, bool specialLiquidationEnabled)
        {
            var me = closeData.MatchingEngine ??
                     _meRouter.GetMatchingEngineForClose(closeData.OpenMatchingEngineId);

            var initialParameters = await _validateOrderService.GetOrderInitialParameters(closeData.AssetPairId, 
                closeData.AccountId);

            var account = _accountsCacheService.Get(closeData.AccountId);
            
            var positionIds = new List<string>();
            var now = _dateService.Now();
            var volume = 0M;

            var positions = closeData.Positions;

            if (closeData.Modality != OrderModality.Liquidation_MarginCall && closeData.Modality != OrderModality.Liquidation_CorporateAction)
            {
                positions = positions.Where(p => p.Status == PositionStatus.Active).ToList();
            }
            
            foreach (var position in positions)
            {
                if (position.TryStartClosing(now, PositionCloseReason.Close, closeData.Originator, "") 
                    ||
                    closeData.Modality == OrderModality.Liquidation_MarginCall
                    ||
                    closeData.Modality == OrderModality.Liquidation_CorporateAction)
                {
                    positionIds.Add(position.Id);
                    volume += position.Volume;
                }
            }

            if (!positionIds.Any())
            {
                if (closeData.Positions.Any(p => p.Status == PositionStatus.Closing))
                {
                    return (PositionCloseResult.ClosingIsInProgress, null);
                }
                
                throw new Exception("No active positions to close");
            }
            
            var order = new Order(initialParameters.Id,
                initialParameters.Code,
                closeData.AssetPairId,
                -volume,
                initialParameters.Now,
                initialParameters.Now,
                null,
                account.Id,
                account.TradingConditionId,
                account.BaseAssetId,
                null,
                closeData.EquivalentAsset,
                OrderFillType.FillOrKill,
                $"Close positions: {string.Join(",", positionIds)}. {closeData.Comment}",
                account.LegalEntity,
                false,
                OrderType.Market,
                null,
                null,
                closeData.Originator,
                initialParameters.EquivalentPrice,
                initialParameters.FxPrice,
                initialParameters.FxAssetPairId,
                initialParameters.FxToAssetPairDirection,
                OrderStatus.Placed,
                closeData.AdditionalInfo,
                closeData.CorrelationId,
                positionIds,
                closeData.ExternalProviderId);
            
            _orderPlacedEventChannel.SendEvent(this, new OrderPlacedEventArgs(order));

            order = await ExecuteOrderByMatchingEngineAsync(order, me, true, closeData.Modality);
            
            if (order.IsExecutionNotStarted)
            {
                if (specialLiquidationEnabled && order.RejectReason == OrderRejectReason.NoLiquidity)
                {
                    var command = new StartSpecialLiquidationInternalCommand
                    {
                        OperationId = Guid.NewGuid().ToString(),
                        CreationTime = _dateService.Now(),
                        AccountId = order.AccountId,
                        PositionIds = order.PositionsToBeClosed.ToArray(),
                        AdditionalInfo = order.AdditionalInfo,
                        OriginatorType = order.Originator
                    };
                    
                    _cqrsSender.SendCommandToSelf(command);

                    return (PositionCloseResult.ClosingStarted, null);
                }
                else
                {
                    foreach (var position in closeData.Positions)
                    {
                        if (position.Status == PositionStatus.Closing)
                            position.CancelClosing(_dateService.Now());    
                    }
                    
                    _log.WriteWarning(nameof(ClosePositionsAsync), order,
                        $"Order {order.Id} was not executed. Closing of positions canceled");
                    
                    throw new Exception($"Positions were not closed. Reason: {order.RejectReasonText}");
                }
            }

            return (PositionCloseResult.Closed, order);
        }

        [ItemNotNull]
        public async Task<Dictionary<string, (PositionCloseResult, Order)>> ClosePositionsGroupAsync(string accountId, 
        string assetPairId, PositionDirection? direction, OriginatorType originator, string additionalInfo, string correlationId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                throw new ArgumentNullException(nameof(accountId), "AccountId must be set.");
            }
            
            var operationId = string.IsNullOrWhiteSpace(correlationId)
                ? _identityGenerator.GenerateGuid()
                : correlationId;
            
            if (string.IsNullOrEmpty(assetPairId))//close all
            {
                return _liquidationHelper.StartLiquidation(accountId, originator, additionalInfo, operationId);
            }
            
            var result = new Dictionary<string, (PositionCloseResult, Order)>();

            var positions = _ordersCache.Positions.GetPositionsByInstrumentAndAccount(assetPairId, accountId);

            var positionGroups = positions
                .Where(p => direction == null || p.Direction == direction)
                .GroupBy(p => (p.AssetPairId, p.AccountId, p.Direction, p
                    .OpenMatchingEngineId, p.ExternalProviderId, p.EquivalentAsset))
                .Select(gr => new PositionsCloseData(
                    gr.ToList(),
                    gr.Key.AccountId,
                    gr.Key.AssetPairId,
                    gr.Key.OpenMatchingEngineId,
                    gr.Key.ExternalProviderId,
                    originator,
                    additionalInfo,
                    operationId,
                    gr.Key.EquivalentAsset,
                    string.Empty));

            foreach (var positionGroup in positionGroups)
            {
                try
                {
                    var closeResult = await ClosePositionsAsync(positionGroup, true);

                    foreach (var position in positionGroup.Positions)
                    {
                        result.Add(position.Id, closeResult);
                    }
                }
                catch (Exception ex)
                {
                    await _log.WriteWarningAsync(nameof(ClosePositionsAsync),
                        positionGroup.ToJson(),
                        $"Failed to close positions {string.Join(",", positionGroup.Positions.Select(p => p.Id))}",
                        ex);

                    foreach (var position in positionGroup.Positions)
                    {
                        result.Add(position.Id, (PositionCloseResult.FailedToClose, null));
                    }
                }
            }

            return result;
        }

        public async Task<(PositionCloseResult, Order)[]> LiquidatePositionsUsingSpecialWorkflowAsync(
            IMatchingEngineBase me, string[] positionIds, string correlationId, string additionalInfo,
            OriginatorType originator, OrderModality modality)
        {
            var positionsToClose = _ordersCache.Positions.GetAllPositions()
                .Where(x => positionIds.Contains(x.Id)).ToList();
            
            var positionGroups = positionsToClose
                .GroupBy(p => (p.AssetPairId, p.AccountId, p.Direction, p
                    .OpenMatchingEngineId, p.ExternalProviderId, p.EquivalentAsset))
                .Select(gr => new PositionsCloseData(
                    gr.ToList(),
                    gr.Key.AccountId,
                    gr.Key.AssetPairId,
                    gr.Key.OpenMatchingEngineId,
                    gr.Key.ExternalProviderId,
                    originator,
                    additionalInfo,
                    correlationId,
                    gr.Key.EquivalentAsset,
                    "Special Liquidation",
                    me,
                    modality));
            
            var failedPositionIds = new List<string>();

            var closeOrderList = (await Task.WhenAll(positionGroups
                .Select(async group =>
                {
                    try
                    {
                        return await ClosePositionsAsync(group, false);
                    }
                    catch (Exception)
                    {
                        failedPositionIds.AddRange(group.Positions.Select(p => p.Id));
                        return default;
                    }
                }))).Where(x => x != default).ToArray();
            
            if (failedPositionIds.Any())
            {
                throw new Exception($"Special liquidation #{correlationId} failed to close these positions: {string.Join(", ", failedPositionIds)}");
            }

            return closeOrderList;
        }

        public Order CancelPendingOrder(string orderId, string additionalInfo,
            string correlationId, string comment = null, OrderCancellationReason reason = OrderCancellationReason.None)
        {
            var order = _ordersCache.GetOrderById(orderId);

            if (order.Status == OrderStatus.Inactive)
            {
                _ordersCache.Inactive.Remove(order);
            }
            else if (order.Status == OrderStatus.Active)
            {
                _ordersCache.Active.Remove(order);
            }
            else
            {
                throw new InvalidOperationException($"Order in state {order.Status} can not be cancelled");
            }

            order.Cancel(_dateService.Now(), additionalInfo, correlationId);

            var metadata = new OrderCancelledMetadata { Reason = reason.ToType<OrderCancellationReasonContract>() };
            _orderCancelledEventChannel.SendEvent(this, new OrderCancelledEventArgs(order, metadata));

            return order;
        }

        #endregion


        public async Task ChangeOrderAsync(string orderId, decimal price, DateTime? validity, OriginatorType originator,
            string additionalInfo, string correlationId, bool? forceOpen = null)
        {
            var order = _ordersCache.GetOrderById(orderId);

            var assetPair = _validateOrderService.GetAssetPairIfAvailableForTrading(order.AssetPairId, order.OrderType,
                order.ForceOpen, false);
            price = Math.Round(price, assetPair.Accuracy);

            _validateOrderService.ValidateOrderPriceChange(order, price);
            _validateOrderService.ValidateValidity(validity, order.OrderType);
            _validateOrderService.ValidateForceOpenChange(order, forceOpen);

            if (order.Price != price)
            {
                var oldPrice = order.Price;
            
                order.ChangePrice(price, _dateService.Now(), originator, additionalInfo, correlationId, true);

                var metadata = new OrderChangedMetadata
                {
                    UpdatedProperty = OrderChangedProperty.Price,
                    OldValue = oldPrice.HasValue ? oldPrice.Value.ToString("F5") : string.Empty
                };
            
                _orderChangedEventChannel.SendEvent(this, new OrderChangedEventArgs(order, metadata));    
            }
            
            if (order.Validity != validity)
            {
                var oldValidity = order.Validity;
            
                order.ChangeValidity(validity, _dateService.Now(), originator, additionalInfo, correlationId);

                var metadata = new OrderChangedMetadata
                {
                    UpdatedProperty = OrderChangedProperty.Validity,
                    OldValue = oldValidity.HasValue ? oldValidity.Value.ToString("g") : "GTC"
                };
            
                _orderChangedEventChannel.SendEvent(this, new OrderChangedEventArgs(order, metadata));    
            }

            if (forceOpen.HasValue && forceOpen.Value != order.ForceOpen)
            {
                var oldForceOpen = order.ForceOpen;
                
                order.ChangeForceOpen(forceOpen.Value, _dateService.Now(), originator, additionalInfo, correlationId);

                var metadata = new OrderChangedMetadata
                {
                    UpdatedProperty = OrderChangedProperty.ForceOpen,
                    OldValue = oldForceOpen.ToString(),
                };

                _orderChangedEventChannel.SendEvent(this, new OrderChangedEventArgs(order, metadata));
            }

            await ExecutePendingOrderIfNeededAsync(order);
        }

        private async Task ExecutePendingOrderIfNeededAsync(Order order)
        {
            if (order.Status == OrderStatus.Active &&
                   _quoteCacheService.TryGetQuoteById(order.AssetPairId, out var pair))
            {
                var price = pair.GetPriceForOrderDirection(order.Direction);

                if (!_assetPairDayOffService.IsDayOff(order.AssetPairId) //!_assetPairDayOffService.ArePendingOrdersDisabled(order.AssetPairId))
                    && order.IsSuitablePriceForPendingOrder(price))
                {
                    _ordersCache.Active.Remove(order);
                    await ExecutePendingOrder(order);
                }
            }
        }

        int IEventConsumer.ConsumerRank => 101;

        void IEventConsumer<BestPriceChangeEventArgs>.ConsumeEvent(object sender, BestPriceChangeEventArgs ea)
        {
            ProcessPositions(ea.BidAskPair, !ea.IsEod);
            ProcessOrdersWaitingForExecution(ea.BidAskPair);
        }

        void IEventConsumer<FxBestPriceChangeEventArgs>.ConsumeEvent(object sender, FxBestPriceChangeEventArgs ea)
        {
            UpdatePositionsFxRates(ea.BidAskPair);
        }
    }
}
