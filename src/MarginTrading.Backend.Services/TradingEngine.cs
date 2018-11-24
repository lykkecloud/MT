﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Common;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Exceptions;
using MarginTrading.Backend.Core.MatchingEngines;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Core.Repositories;
using MarginTrading.Backend.Core.Trading;
using MarginTrading.Backend.Services.AssetPairs;
using MarginTrading.Backend.Services.Events;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Backend.Services.Workflow.Liquidation.Commands;
using MarginTrading.Common.Services;

namespace MarginTrading.Backend.Services
{
    public sealed class TradingEngine : ITradingEngine, IEventConsumer<BestPriceChangeEventArgs>
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
        private readonly IEventChannel<StopOutEventArgs> _stopoutEventChannel;
        private readonly IQuoteCacheService _quoteCacheService;

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
            IEventChannel<StopOutEventArgs> stopoutEventChannel,
            IQuoteCacheService quoteCacheService)
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
            _stopoutEventChannel = stopoutEventChannel;
            _quoteCacheService = quoteCacheService;
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
                _log.WriteError(nameof(TradingEngine), nameof(PlaceOrderByMarketPrice), ex);
                return order;
            }
        }
        
        private async Task<Order> PlaceOrderByMarketPrice(Order order)
        {
            try
            {
                var me = _meRouter.GetMatchingEngineForExecution(order);

                return await ExecuteOrderByMatchingEngineAsync(order, me, true);
            }
            catch (QuoteNotFoundException ex)
            {
                RejectOrder(order, OrderRejectReason.NoLiquidity, ex.Message);
                return order;
            }
            catch (Exception ex)
            {
                RejectOrder(order, OrderRejectReason.TechnicalError, ex.Message);
                _log.WriteError(nameof(TradingEngine), nameof(PlaceOrderByMarketPrice), ex);
                return order;
            }
        }

        private async Task PlacePendingOrder(Order order)
        {
            if (order.IsBasicPendingOrder() || !string.IsNullOrEmpty(order.ParentPositionId))
            {
                order.Activate(_dateService.Now(), false);
                _ordersCache.Active.Add(order);
                _orderActivatedEventChannel.SendEvent(this, new OrderActivatedEventArgs(order));

                if (!string.IsNullOrEmpty(order.ParentPositionId))
                {
                    var position = _ordersCache.Positions.GetPositionById(order.ParentPositionId);
                    position.AddRelatedOrder(order);
                }
            }
            else if (!string.IsNullOrEmpty(order.ParentOrderId))
            {
                if (_ordersCache.TryGetOrderById(order.ParentOrderId, out var parentOrder))
                {
                    parentOrder.AddRelatedOrder(order);
                    order.MakeInactive(_dateService.Now());
                    _ordersCache.Inactive.Add(order);
                }

                //may be it was market and now it is position
                else if (_ordersCache.Positions.TryGetPositionById(order.ParentOrderId, out var parentPosition))
                {
                    parentPosition.AddRelatedOrder(order);
                    if (parentPosition.Volume != -order.Volume)
                    {
                        order.ChangeVolume(-parentPosition.Volume, _dateService.Now(), OriginatorType.System);
                    }

                    order.Activate(_dateService.Now(), true);
                    _ordersCache.Active.Add(order);
                    _orderActivatedEventChannel.SendEvent(this, new OrderActivatedEventArgs(order));
                }
            }
            else
            {
                throw new ValidateOrderException(OrderRejectReason.InvalidParent, "Order parent is not valid");
            }

            if (_quoteCacheService.TryGetQuoteById(order.AssetPairId, out var pair))
            {
                var price = pair.GetPriceForOrderDirection(order.Direction);

                if (order.IsSuitablePriceForPendingOrder(price) &&
                    !_assetPairDayOffService.ArePendingOrdersDisabled(order.AssetPairId))
                {
                    _ordersCache.Active.Remove(order);
                    await PlaceOrderByMarketPrice(order);
                }
            }
        }

        private async Task<Order> ExecuteOrderByMatchingEngineAsync(Order order, IMatchingEngineBase matchingEngine,
            bool checkStopout, OrderModality modality = OrderModality.Regular)
        {
            //TODO: think how not to execute one order twice!!!
            
            order.StartExecution(_dateService.Now(), matchingEngine.Id);

            _orderExecutionStartedEvenChannel.SendEvent(this, new OrderExecutionStartedEventArgs(order));

            if (!string.IsNullOrEmpty(order.ParentPositionId))
            {
                if (!_ordersCache.Positions.TryGetPositionById(order.ParentPositionId, out var position))
                {
                    order.Reject(OrderRejectReason.ParentPositionDoesNotExist, "Parent position does not exist", "", _dateService.Now());
                    _orderRejectedEventChannel.SendEvent(this, new OrderRejectedEventArgs(order));
                    return order;
                }
                if (position.Status != PositionStatus.Active)
                {
                    order.Reject(OrderRejectReason.ParentPositionIsNotActive, "Parent position is not active", "", _dateService.Now());
                    _orderRejectedEventChannel.SendEvent(this, new OrderRejectedEventArgs(order));
                    return order;
                }

                position.StartClosing(_dateService.Now(), order.OrderType.GetCloseReason(), order.Originator, "");
            }
            
            var equivalentRate = _cfdCalculatorService.GetQuoteRateForQuoteAsset(order.EquivalentAsset,
                order.AssetPairId, order.LegalEntity);
            var fxRate = _cfdCalculatorService.GetQuoteRateForQuoteAsset(order.AccountAssetId,
                order.AssetPairId, order.LegalEntity);

            order.SetRates(equivalentRate, fxRate);

            var shouldOpenNewPosition = ShouldOpenNewPosition(order);

            if (modality == OrderModality.Regular)
            {
                try
                {
                    _validateOrderService.MakePreTradeValidation(order, shouldOpenNewPosition);
                }
                catch (ValidateOrderException ex)
                {
                    RejectOrder(order, ex.RejectReason, ex.Message, ex.Comment);
                    return order;
                }
            }

            var matchedOrders = await matchingEngine.MatchOrderAsync(order, shouldOpenNewPosition, modality);

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
                    var account = _accountsCacheService.Get(order.AccountId);
                    var accountLevel = account.GetAccountLevel();

                    if (accountLevel == AccountLevel.StopOut)
                    {
                        CommitStopOut(account, null);
                    }
                }
            }

            return order;
        }

        public bool ShouldOpenNewPosition(Order order)
        {
            var shouldOpenNewPosition = order.ForceOpen;

            if (string.IsNullOrEmpty(order.ParentPositionId) && !shouldOpenNewPosition)
            {
                var existingPositions =
                    _ordersCache.Positions.GetPositionsByInstrumentAndAccount(order.AssetPairId, order.AccountId);
                var netVolume = existingPositions.Where(p => p.Status == PositionStatus.Active).Sum(p => p.Volume);
                var newNetVolume = netVolume + order.Volume;

                shouldOpenNewPosition = (Math.Sign(netVolume) != Math.Sign(newNetVolume) && newNetVolume != 0) ||
                                        Math.Abs(netVolume) < Math.Abs(newNetVolume);
            }

            return shouldOpenNewPosition;
        }

        private void RejectOrder(Order order, OrderRejectReason reason, string message, string comment = null)
        {
            if (order.OrderType == OrderType.Market || reason != OrderRejectReason.NoLiquidity)
            {
                order.Reject(reason, message, comment, _dateService.Now());
            
                _orderRejectedEventChannel.SendEvent(this, new OrderRejectedEventArgs(order));
            }
            //TODO: think how to avoid infinite loop
            else if (!_ordersCache.TryGetOrderById(order.Id, out _)) // all pending orders should be returned to active state if there is no liquidity
            {
                order.CancelExecution(_dateService.Now());
                _ordersCache.Active.Add(order);   
                _orderChangedEventChannel.SendEvent(this, new OrderChangedEventArgs(order));
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
                    await PlaceOrderByMarketPrice(order);
                });
            }
        }

        private IEnumerable<Order> GetPendingOrdersToBeExecuted(InstrumentBidAskPair quote)
        {
            var pendingOrders = _ordersCache.Active.GetOrdersByInstrument(quote.Instrument);

            var now = _dateService.Now();

            foreach (var order in pendingOrders)
            {
                if (order.Validity.HasValue && now >= order.Validity.Value)
                {
                    _ordersCache.Active.Remove(order);
                    order.Expire(now);
                    _orderCancelledEventChannel.SendEvent(this, new OrderCancelledEventArgs(order));
                    continue;
                }

                var price = quote.GetPriceForOrderDirection(order.Direction);

                if (order.IsSuitablePriceForPendingOrder(price) &&
                        !_assetPairDayOffService.ArePendingOrdersDisabled(order.AssetPairId))
                {
                    //TODO: inspect one more time in MTC-248
                    // if order is removed from Active, execution should be started immediately
                    // and/or placed to InProgress

                    _ordersCache.Active.Remove(order);
                    yield return order;
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

        private void ProcessPositions(InstrumentBidAskPair quote)
        {
            var stopoutAccounts = UpdateClosePriceAndDetectStopout(quote).ToArray();
            
            foreach (var account in stopoutAccounts)
                CommitStopOut(account, quote);
        }

        //TODO: in MTC-192 split method and change conditions
        private IEnumerable<MarginTradingAccount> UpdateClosePriceAndDetectStopout(InstrumentBidAskPair quote)
        {
            var positionsByAccounts = _ordersCache.Positions.GetPositionsByInstrument(quote.Instrument)
                .GroupBy(x => x.AccountId).ToDictionary(x => x.Key, x => x.ToArray());

            foreach (var accountPositions in positionsByAccounts)
            {
                var account = _accountsCacheService.Get(accountPositions.Key);
                var oldAccountLevel = account.GetAccountLevel();

                foreach (var position in accountPositions.Value)
                {
                    var defaultMatchingEngine = _meRouter.GetMatchingEngineForClose(position);

                    var closePrice = defaultMatchingEngine.GetPriceForClose(position.AssetPairId, position.Volume,
                        position.ExternalProviderId);

                    if (!closePrice.HasValue)
                    {
                        var closeOrderDirection = position.Volume.GetClosePositionOrderDirection();
                        closePrice = quote.GetPriceForOrderDirection(closeOrderDirection);
                    }
                    
                    if (closePrice != 0)
                    {
                        position.UpdateClosePrice(closePrice.Value);

                        UpdateTrailingStops(position);
                    }
                }

                var newAccountLevel = account.GetAccountLevel();
                
                if (newAccountLevel == AccountLevel.StopOut)
                    yield return account;

                if (oldAccountLevel != newAccountLevel)
                {
                    _marginCallEventChannel.SendEvent(this, new MarginCallEventArgs(account, newAccountLevel));
                }
            }
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
                        if (Math.Abs(trailingOrder.Price.Value - position.ClosePrice) >
                            Math.Abs(trailingOrder.TrailingDistance.Value))
                        {
                            var newPrice = position.ClosePrice + trailingOrder.TrailingDistance.Value;
                            trailingOrder.ChangePrice(newPrice,
                                _dateService.Now(),
                                trailingOrder.Originator,
                                null,
                                _identityGenerator.GenerateGuid()); //todo in fact price change correlationId must be used
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

            PositionDirection? direction = null;
            var liquidationType = LiquidationType.Normal;

            if (account.GetMcoMarginUsageLevelLong() != 0 &&
                account.GetMcoMarginUsageLevelLong() <= MtServiceLocator.McoRules?.LongMcoLevels.StopOut)
            {
                direction = PositionDirection.Long;
                liquidationType = LiquidationType.Mco;
            }
            else if (account.GetMcoMarginUsageLevelShort() != 0 &&
                     account.GetMcoMarginUsageLevelShort() >= MtServiceLocator.McoRules?.ShortMcoLevels.StopOut)
            {
                direction = PositionDirection.Short;
                liquidationType = LiquidationType.Mco;
            }

            _cqrsSender.SendCommandToSelf(new StartLiquidationInternalCommand
            {
                OperationId = _identityGenerator.GenerateGuid(),//TODO: use quote correlationId
                AccountId = account.Id,
                CreationTime = _dateService.Now(),
                QuoteInfo = quote?.ToJson(),
                Direction = direction,
                LiquidationType = liquidationType
            });

            _stopoutEventChannel.SendEvent(this, new StopOutEventArgs(account));
        }

        public async Task<Order> ClosePositionAsync(string positionId, OriginatorType originator, string additionalInfo,
            string correlationId, string comment = null, IMatchingEngineBase me = null, 
            OrderModality modality = OrderModality.Regular)
        {
            var position = _ordersCache.Positions.GetPositionById(positionId);

            me = me ?? _meRouter.GetMatchingEngineForClose(position);

            var id = _identityGenerator.GenerateAlphanumericId();
            var code = _identityGenerator.GenerateIdAsync(nameof(Order)).GetAwaiter().GetResult();
            var now = _dateService.Now();

            var order = new Order(id, code, position.AssetPairId, -position.Volume, now, now, null, position.AccountId,
                position.TradingConditionId, position.AccountAssetId, null, position.EquivalentAsset,
                OrderFillType.FillOrKill, $"Close position. {comment}", position.LegalEntity, false, OrderType.Market, null,
                position.Id,
                originator, 0, 0, OrderStatus.Placed, additionalInfo, correlationId);
            
            _orderPlacedEventChannel.SendEvent(this, new OrderPlacedEventArgs(order));
              
            order = await ExecuteOrderByMatchingEngineAsync(order, me, true, modality);
            
            if (order.Status != OrderStatus.Executed && order.Status != OrderStatus.ExecutionStarted)
            {
                position.CancelClosing(_dateService.Now());
                _log.WriteWarning(nameof(ClosePositionAsync), order, "Order was not executed. Closing canceled");
            }

            return order;
        }

        public async Task<Order[]> LiquidatePositionsAsync(IMatchingEngineBase me, string[] positionIds,
            string correlationId)
        {
            var positionsToClose = _ordersCache.Positions.GetAllPositions()
                .Where(x => positionIds.Contains(x.Id)).ToList();
            var failedPositionIds = new List<string>();
            
            var closeOrderList = await Task.WhenAll(positionsToClose
                .Select(async x =>
                {
                    try
                    {
                        return await ClosePositionAsync(x.Id, OriginatorType.System, string.Empty, correlationId,
                            "Special Liquidation", me, OrderModality.Liquidation);
                    }
                    catch (PositionNotFoundException)
                    {
                        return null; //swallow exception if position was already closed
                    }
                    catch (Exception)
                    {
                        failedPositionIds.Add(x.Id);
                        return null;
                    }
                }).Where(x => x != null));
            
            if (failedPositionIds.Any())
            {
                throw new Exception($"Liquidation #{correlationId} failed to close these positions: {string.Join(", ", failedPositionIds)}");
            }

            return closeOrderList;
        }

        public Order CancelPendingOrder(string orderId, OriginatorType originator, string additionalInfo, 
            string correlationId, string comment = null)
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
            
            order.Cancel(_dateService.Now(), originator, additionalInfo, correlationId);
            
            _orderCancelledEventChannel.SendEvent(this, new OrderCancelledEventArgs(order));
            
            return order;
        }

        #endregion


        public void ChangeOrderLimits(string orderId, decimal price, OriginatorType originator, string additionalInfo,
            string correlationId)
        {
            var order = _ordersCache.GetOrderById(orderId);

            _validateOrderService.ValidateOrderPriceChange(order, price);

            order.ChangePrice(price, _dateService.Now(), originator, additionalInfo, correlationId);

            _orderChangedEventChannel.SendEvent(this, new OrderChangedEventArgs(order));
        }

        int IEventConsumer.ConsumerRank => 101;

        void IEventConsumer<BestPriceChangeEventArgs>.ConsumeEvent(object sender, BestPriceChangeEventArgs ea)
        {
            ProcessPositions(ea.BidAskPair);
            ProcessOrdersWaitingForExecution(ea.BidAskPair);
        }
    }
}
