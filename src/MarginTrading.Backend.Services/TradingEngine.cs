﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using MarginTrading.Common.Services;

namespace MarginTrading.Backend.Services
{
    public sealed class TradingEngine : ITradingEngine, IEventConsumer<BestPriceChangeEventArgs>
    {
        private readonly IEventChannel<MarginCallEventArgs> _marginCallEventChannel;
        private readonly IEventChannel<StopOutEventArgs> _stopoutEventChannel;
        private readonly IEventChannel<OrderPlacedEventArgs> _orderPlacedEventChannel;
        private readonly IEventChannel<OrderExecutedEventArgs> _orderExecutedEventChannel;
        private readonly IEventChannel<OrderCancelledEventArgs> _orderCancelledEventChannel;
        private readonly IEventChannel<OrderChangedEventArgs> _orderChangedEventChannel;
        private readonly IEventChannel<OrderExecutionStartedEventArgs> _orderExecutionStartedEvenChannel;
        private readonly IEventChannel<OrderActivatedEventArgs> _orderActivatedEventChannel;
        private readonly IEventChannel<OrderRejectedEventArgs> _orderRejectedEventChannel;

        private readonly IQuoteCacheService _quoteCashService;
        private readonly IValidateOrderService _validateOrderService;
        private readonly IAccountsCacheService _accountsCacheService;
        private readonly OrdersCache _ordersCache;
        private readonly IMatchingEngineRouter _meRouter;
        private readonly IThreadSwitcher _threadSwitcher;
        private readonly IContextFactory _contextFactory;
        private readonly IAssetPairDayOffService _assetPairDayOffService;
        private readonly ILog _log;
        private readonly IDateService _dateService;
        private readonly ICfdCalculatorService _cfdCalculatorService;
        private readonly IIdentityGenerator _identityGenerator;
        private readonly IAssetPairsCache _assetPairsCache;

        public TradingEngine(
            IEventChannel<MarginCallEventArgs> marginCallEventChannel,
            IEventChannel<StopOutEventArgs> stopoutEventChannel,
            IEventChannel<OrderPlacedEventArgs> orderPlacedEventChannel,
            IEventChannel<OrderExecutedEventArgs> orderClosedEventChannel,
            IEventChannel<OrderCancelledEventArgs> orderCancelledEventChannel, 
            IEventChannel<OrderChangedEventArgs> orderChangedEventChannel,
            IEventChannel<OrderExecutionStartedEventArgs> orderExecutionStartedEventChannel,
            IEventChannel<OrderActivatedEventArgs> orderActivatedEventChannel, 
            IEventChannel<OrderRejectedEventArgs> orderRejectedEventChannel,
            IValidateOrderService validateOrderService,
            IQuoteCacheService quoteCashService,
            IAccountsCacheService accountsCacheService,
            OrdersCache ordersCache,
            IMatchingEngineRouter meRouter,
            IThreadSwitcher threadSwitcher,
            IContextFactory contextFactory,
            IAssetPairDayOffService assetPairDayOffService,
            ILog log,
            IDateService dateService,
            ICfdCalculatorService cfdCalculatorService,
            IIdentityGenerator identityGenerator,
            IAssetPairsCache assetPairsCache)
        {
            _marginCallEventChannel = marginCallEventChannel;
            _stopoutEventChannel = stopoutEventChannel;
            _orderPlacedEventChannel = orderPlacedEventChannel;
            _orderExecutedEventChannel = orderClosedEventChannel;
            _orderCancelledEventChannel = orderCancelledEventChannel;
            _orderActivatedEventChannel = orderActivatedEventChannel;
            _orderExecutionStartedEvenChannel = orderExecutionStartedEventChannel;
            _orderChangedEventChannel = orderChangedEventChannel;
            _orderRejectedEventChannel = orderRejectedEventChannel;

            _quoteCashService = quoteCashService;
            _validateOrderService = validateOrderService;
            _accountsCacheService = accountsCacheService;
            _ordersCache = ordersCache;
            _meRouter = meRouter;
            _threadSwitcher = threadSwitcher;
            _contextFactory = contextFactory;
            _assetPairDayOffService = assetPairDayOffService;
            _log = log;
            _dateService = dateService;
            _cfdCalculatorService = cfdCalculatorService;
            _identityGenerator = identityGenerator;
            _assetPairsCache = assetPairsCache;
        }

        public async Task<Order> PlaceOrderAsync(Order order)
        {
            _orderPlacedEventChannel.SendEvent(this, new OrderPlacedEventArgs(order));
            
            try
            {
                if (order.OrderType != OrderType.Market)
                {
                    PlacePendingOrder(order);
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

        private void PlacePendingOrder(Order order)
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
        }

        private async Task<Order> ExecuteOrderByMatchingEngineAsync(Order order, IMatchingEngineBase matchingEngine, bool checkStopout)
        {
            //TODO: think how not to execute one order twice!!!
            
            order.StartExecution(_dateService.Now(), matchingEngine.Id);

            _orderExecutionStartedEvenChannel.SendEvent(this, new OrderExecutionStartedEventArgs(order));

            if (!string.IsNullOrEmpty(order.ParentPositionId))
            {
                if (!_ordersCache.Positions.TryGetPositionById(order.ParentPositionId, out var position) ||
                    position.Status != PositionStatus.Active)
                {
                    order.Cancel(_dateService.Now(), OriginatorType.System, null, order.CorrelationId);
                    _orderCancelledEventChannel.SendEvent(this, new OrderCancelledEventArgs(order));
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

            try
            {
                _validateOrderService.MakePreTradeValidation(order, shouldOpenNewPosition);
            }
            catch (ValidateOrderException ex)
            {
                RejectOrder(order, ex.RejectReason, ex.Message, ex.Comment);
                return order;
            }

            var matchedOrders = await matchingEngine.MatchOrderAsync(order, shouldOpenNewPosition);

            if (!matchedOrders.Any())
            {
                RejectOrder(order, OrderRejectReason.NoLiquidity, "No orders to match", "");
            } 
            else if (matchedOrders.SummaryVolume < Math.Abs(order.Volume))
            {
                if (order.FillType == OrderFillType.FillOrKill)
                {
                    RejectOrder(order, OrderRejectReason.NoLiquidity, "Not fully matched", "");
                }
                else
                {
                    order.PartiallyExecute(_dateService.Now(), matchedOrders);
                    _ordersCache.InProgress.Add(order);
                    return order;
                }
            }

            if (order.Status != OrderStatus.Rejected)
            {
                var accuracy = _assetPairsCache.GetAssetPairByIdOrDefault(order.AssetPairId)?.Accuracy ??
                               AssetPairsCache.DefaultAssetPairAccuracy;
                
                order.Execute(_dateService.Now(), matchedOrders, accuracy);
                
                _orderExecutedEventChannel.SendEvent(this, new OrderExecutedEventArgs(order));

                if (checkStopout)
                {
                    var account = _accountsCacheService.Get(order.AccountId);
                    var accountLevel = account.GetAccountLevel();

                    if (accountLevel == AccountLevel.StopOUt)
                    {
                        CommitStopout(account);
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
            order.Reject(reason, message, comment, _dateService.Now());
            
            _orderRejectedEventChannel.SendEvent(this, new OrderRejectedEventArgs(order));
        }

        #region Orders waiting for execution

        private void ProcessOrdersWaitingForExecution(string instrument)
        {
            //ProcessPendingOrdersMarginRecalc(instrument);

            var orders = GetPendingOrdersToBeExecuted(instrument).ToArray();

            if (orders.Length == 0)
                return;


            //TODO: think how to make sure that we don't loose orders
            // + change logic according validation and execution rules

            foreach (var order in orders)
            {
                _threadSwitcher.SwitchThread(async () =>
                {
                    _ordersCache.Active.Remove(order);
                    await PlaceOrderByMarketPrice(order);
                });
            }
        }

        private IEnumerable<Order> GetPendingOrdersToBeExecuted(string instrument)
        {
            var pendingOrders = _ordersCache.Active.GetOrdersByInstrument(instrument)
                .OrderBy(item => item.Created);

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
                
                if (_quoteCashService.TryGetQuoteById(order.AssetPairId, out var pair))
                {
                    var price = pair.GetPriceForOrderDirection(order.Direction);

                    if (order.IsSuitablePriceForPendingOrder(price) &&
                        !_assetPairDayOffService.ArePendingOrdersDisabled(order.AssetPairId))
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

        private void ProcessPositions(string instrument)
        {
            var stopoutAccounts = UpdateClosePriceAndDetectStopout(instrument).ToArray();
            foreach (var account in stopoutAccounts)
                CommitStopout(account);
        }

        private IEnumerable<MarginTradingAccount> UpdateClosePriceAndDetectStopout(string instrument)
        {
            var openPositions = _ordersCache.Positions.GetPositionsByInstrument(instrument)
                .GroupBy(x => x.AccountId).ToDictionary(x => x.Key, x => x.ToArray());

            foreach (var accountPositions in openPositions)
            {
                var anyPosition = accountPositions.Value.FirstOrDefault();
                if (null == anyPosition)
                    continue;

                var account = _accountsCacheService.Get(anyPosition.AccountId);
                var oldAccountLevel = account.GetAccountLevel();

                foreach (var position in accountPositions.Value)
                {
                    var defaultMatchingEngine = _meRouter.GetMatchingEngineForClose(position);

                    var closePrice = defaultMatchingEngine.GetPriceForClose(position);

                    if (closePrice.HasValue)
                    {
                        position.UpdateClosePrice(closePrice.Value);

                        var trailingOrderIds = position.RelatedOrders.Where(o => o.Type == OrderType.TrailingStop)
                            .Select(o => o.Id);

                        foreach (var trailingOrderId in trailingOrderIds)
                        {
                            if (_ordersCache.TryGetOrderById(trailingOrderId, out var trailingOrder)
                                && trailingOrder.Price.HasValue)
                            {
                                if (trailingOrder.TrailingDistance.HasValue)
                                {
                                    if (Math.Abs(trailingOrder.Price.Value - closePrice.Value) >
                                        Math.Abs(trailingOrder.TrailingDistance.Value))
                                    {
                                        var newPrice = closePrice.Value + trailingOrder.TrailingDistance.Value;
                                        trailingOrder.ChangePrice(newPrice,
                                            _dateService.Now(),
                                            trailingOrder.Originator,
                                            null,
                                            _identityGenerator.GenerateGuid());//todo in fact price change correlationId must be used
                                    }
                                }
                                else
                                {
                                    trailingOrder.SetTrailingDistance(closePrice.Value);
                                }
                            }
                        }
                    }
                }

                var newAccountLevel = account.GetAccountLevel();

                if (oldAccountLevel != newAccountLevel)
                {
                    NotifyAccountLevelChanged(account, newAccountLevel);

                    if (newAccountLevel == AccountLevel.StopOUt)
                        yield return account;
                }
            }
        }

        //TODO: change liquidation flow in MTC-98 (use Saga)
        private void CommitStopout(MarginTradingAccount account)
        {
            //var pendingOrders = _ordersCache.Active.GetOrdersByAccountIds(account.Id);

            //var cancelledPendingOrders = new List<Order>();
            
            //foreach (var pendingOrder in pendingOrders)
            //{
            //    cancelledPendingOrders.Add(pendingOrder);
            //    CancelPendingOrder(pendingOrder.Id, PositionCloseReason.CanceledBySystem, "Stop out");
            //}
            
            var positions = _ordersCache.Positions.GetPositionsByAccountIds(account.Id);
            
            var positionsToClose = new List<Position>();
            var newAccountUsedMargin = account.GetUsedMargin();

            foreach (var order in positions.OrderBy(o => o.GetTotalFpl()))
            {
                if (newAccountUsedMargin <= 0 ||
                    account.GetTotalCapital() / newAccountUsedMargin > account.GetMarginCall1Level())
                    break;
                
                positionsToClose.Add(order);
                newAccountUsedMargin -= order.GetMarginMaintenance();
            }

            if (!positionsToClose.Any())
                return;

            _stopoutEventChannel.SendEvent(this,
                new StopOutEventArgs(account, positionsToClose/*.Concat(cancelledPendingOrders)*/.ToArray()));

            foreach (var position in positionsToClose)
                StartClosingPosition(position, PositionCloseReason.StopOut);
        }

        private void StartClosingPosition(Position position, PositionCloseReason reason)
        {
            //position.StartClosing(_dateService.Now(), reason, OriginatorType.Investor, "");
            
            var id = _identityGenerator.GenerateAlphanumericId();
            var code = _identityGenerator.GenerateIdAsync(nameof(Order)).GetAwaiter().GetResult();
            var now = _dateService.Now();

            var order = new Order(id, code, position.AssetPairId, -position.Volume, now, now, null, position.AccountId,
                position.TradingConditionId, position.AccountAssetId, null, position.EquivalentAsset,
                OrderFillType.FillOrKill, "Stop out", position.LegalEntity, false, OrderType.Market, null, position.Id,
                OriginatorType.System, 0, 0, OrderStatus.Placed, "", _identityGenerator.GenerateGuid());//todo in fact price change correlationId must be used
            
            var me = _meRouter.GetMatchingEngineForExecution(order);

            ExecuteOrderByMatchingEngineAsync(order, me, false).GetAwaiter().GetResult();
        }

        private void NotifyAccountLevelChanged(MarginTradingAccount account, AccountLevel newAccountLevel)
        {
            switch (newAccountLevel)
            {
                case AccountLevel.MarginCall1:
                    _marginCallEventChannel.SendEvent(this, new MarginCallEventArgs(account, newAccountLevel));
                    break;
                
                case AccountLevel.MarginCall2:
                    _marginCallEventChannel.SendEvent(this, new MarginCallEventArgs(account, newAccountLevel));
                    break;
            }
        }

        public Task<Order> ClosePositionAsync(string positionId, OriginatorType originator, string additionalInfo,
            string correlationId, string comment = null, IMatchingEngineBase me = null)
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
                
            return ExecuteOrderByMatchingEngineAsync(order, me, true);
        }

        public async Task<Order[]> LiquidatePositionsAsync(IMatchingEngineBase me, string[] positionIds,
            string correlationId)
        {
            //any position may be already closed
            return await Task.WhenAll(_ordersCache.Positions.GetAllPositions().Where(x => positionIds.Contains(x.Id))
                .Select(async x =>
                {
                    try
                    {
                        return await ClosePositionAsync(x.Id, OriginatorType.System, string.Empty, correlationId, 
                            "Special Liquidation", me);
                    }
                    catch (Exception ex)
                    {
                        await _log.WriteWarningAsync(nameof(TradingEngine), nameof(LiquidatePositionsAsync),
                            $"Failed to close position {x.Id} on special liquidation operation #{correlationId}", ex);
                        return null;
                    }
                }).Where(x => x != null));
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

        public bool PingLock()
        {
            using (_contextFactory.GetReadSyncContext($"{nameof(TradingEngine)}.{nameof(PingLock)}"))
            {
                return true;
            }
        }

        int IEventConsumer.ConsumerRank => 100;

        void IEventConsumer<BestPriceChangeEventArgs>.ConsumeEvent(object sender, BestPriceChangeEventArgs ea)
        {
            ProcessPositions(ea.BidAskPair.Instrument);
            ProcessOrdersWaitingForExecution(ea.BidAskPair.Instrument);
        }
    }
}
