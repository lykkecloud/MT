﻿using System;

namespace MarginTrading.Backend.Core
{
    public static class OrderExtensions
    {
        public static bool IsSuitablePriceForPendingOrder(this IOrder order, decimal price)
        {
            return order.ExpectedOpenPrice.HasValue && (order.GetOrderType() == OrderDirection.Buy && price <= order.ExpectedOpenPrice
                                                        || order.GetOrderType() == OrderDirection.Sell && price >= order.ExpectedOpenPrice);
        }

        public static bool IsStopLoss(this IOrder order)
        {
            return order.GetOrderType() == OrderDirection.Buy
                ? order.StopLoss.HasValue && order.StopLoss.Value > 0 && order.ClosePrice <= order.StopLoss
                : order.StopLoss.HasValue && order.StopLoss.Value > 0 && order.ClosePrice >= order.StopLoss;
        }

        public static bool IsTakeProfit(this IOrder order)
        {
            return order.GetOrderType() == OrderDirection.Buy
                ? order.TakeProfit.HasValue && order.TakeProfit > 0 && order.ClosePrice >= order.TakeProfit
                : order.TakeProfit.HasValue && order.TakeProfit > 0 && order.ClosePrice <= order.TakeProfit;
        }

        public static decimal GetTotalFpl(this IOrder order, decimal swaps)
        {
            return order.GetFpl() - order.GetOpenCommission() - order.GetCloseCommission() - swaps;
        }

        public static decimal GetTotalFpl(this IOrder order)
        {
            return Math.Round(GetTotalFpl(order, order.GetSwaps()), order.CalculateFplData().AccountBaseAssetAccuracy);
        }

        public static decimal GetMatchedVolume(this IOrder order)
        {
            return order.MatchedOrders.SummaryVolume;
        }

        public static decimal GetMatchedCloseVolume(this IOrder order)
        {
            return order.MatchedCloseOrders.SummaryVolume;
        }

        public static decimal GetRemainingCloseVolume(this IOrder order)
        {
            return order.GetMatchedVolume() - order.GetMatchedCloseVolume();
        }

        public static bool GetIsCloseFullfilled(this IOrder order)
        {
            return Math.Round(order.GetRemainingCloseVolume(), MarginTradingHelpers.VolumeAccuracy) == 0;
        }

        private static FplData CalculateFplData(this IOrder order)
        {
            if (order is Order orderInstance)
            {
                if (orderInstance.FplData.ActualHash != orderInstance.FplData.CalculatedHash)
                {
                    MtServiceLocator.FplService.UpdateOrderFpl(orderInstance, orderInstance.FplData);
                }

                return orderInstance.FplData;
            }

            var fplData = new FplData();
            MtServiceLocator.FplService.UpdateOrderFpl(order, fplData);

            return fplData;
        }

        public static decimal GetFpl(this IOrder order)
        {
            return order.CalculateFplData().Fpl;
        }

        public static decimal GetFplRate(this IOrder order)
        {
            return order.CalculateFplData().FplRate;
        }

        public static decimal GetMarginRate(this IOrder order)
        {
            return order.CalculateFplData().MarginRate;
        }

        public static decimal GetMarginMaintenance(this IOrder order)
        {
            return order.CalculateFplData().MarginMaintenance;
        }

        public static decimal GetMarginInit(this IOrder order)
        {
            return order.CalculateFplData().MarginInit;
        }

        public static void UpdateClosePrice(this IOrder order, decimal closePrice)
        {
            if (order is Order orderInstance)
            {
                orderInstance.ClosePrice = closePrice;
                orderInstance.FplData.ActualHash++;
                var account = MtServiceLocator.AccountsCacheService.Get(order.AccountId);
                account.CacheNeedsToBeUpdated();
            }
        }

        public static void UpdatePendingOrderMargin(this IOrder order)
        {
            if (order is Order orderInstance)
            {
                orderInstance.FplData.ActualHash++;
            }
        }

        public static decimal GetSwaps(this IOrder order)
        {
            return MtServiceLocator.SwapCommissionService.GetSwaps(order);
        }

        public static decimal GetOpenCommission(this IOrder order)
        {
            return Math.Abs(order.Volume) * order.OpenCommission;
        }

        public static decimal GetCloseCommission(this IOrder order)
        {
            return Math.Abs(order.GetMatchedCloseVolume()) * order.CloseCommission;
        }

        public static bool IsOpened(this IOrder order)
        {
            return order.Status == OrderStatus.Active
                   && order.OpenDate.HasValue
                   && order.OpenPrice > 0
                   && order.MatchedOrders.SummaryVolume > 0;
        }

        public static bool IsClosed(this IOrder order)
        {
            return order.Status == OrderStatus.Closed
                   && (order.CloseReason == OrderCloseReason.Close
                       || order.CloseReason == OrderCloseReason.ClosedByBroker
                       || order.CloseReason == OrderCloseReason.StopLoss
                       || order.CloseReason == OrderCloseReason.StopOut
                       || order.CloseReason == OrderCloseReason.TakeProfit)
                   && order.CloseDate.HasValue
                   && order.ClosePrice > 0
                   && order.MatchedCloseOrders.SummaryVolume > 0;
        }
    }
}