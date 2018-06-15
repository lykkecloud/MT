﻿using System.Threading.Tasks;
using Lykke.Service.ExchangeConnector.Client.Models;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Contract.RabbitMqMessageModels;

namespace MarginTrading.Backend.Services.Notifications
{
	public interface IRabbitMqNotifyService
	{
		Task AccountHistory(string transactionId, string accountId, string clientId, decimal amount, decimal balance, 
			decimal withdrawTransferLimit, AccountHistoryType type, string comment = null, string eventSourceId = null, 
			string auditLog = null);
		Task OrderHistory(IOrder order, OrderUpdateType orderUpdateType);
		Task OrderBookPrice(InstrumentBidAskPair quote);
		Task OrderChanged(IOrder order);
		Task AccountUpdated(IMarginTradingAccount account);
		Task AccountStopout(string clientId, string accountId, int positionsCount, decimal totalPnl);
		Task UserUpdates(bool updateAccountAssets, bool updateAccounts, string[] clientIds);
		void Stop();
	    Task AccountCreated(IMarginTradingAccount account);
	    Task AccountDeleted(IMarginTradingAccount account);
	    Task AccountMarginEvent(AccountMarginEventMessage eventMessage);
		Task UpdateAccountStats(AccountStatsUpdateMessage message);
		Task NewTrade(TradeContract trade);
		Task ExternalOrder(ExecutionReport trade);
	}
} 