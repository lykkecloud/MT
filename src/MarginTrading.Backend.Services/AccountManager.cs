// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Common;
using Common.Log;
using MarginTrading.AccountsManagement.Contracts;
using MarginTrading.AccountsManagement.Contracts.Models;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Mappers;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Core.Repositories;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Core.Trading;
using MarginTrading.Backend.Services.Notifications;
using MarginTrading.Common.Services;
using MarginTrading.Contract.RabbitMqMessageModels;
using MoreLinq;

namespace MarginTrading.Backend.Services
{
    public class AccountManager : TimerPeriod
    {
        private readonly AccountsCacheService _accountsCacheService;
        private readonly MarginTradingSettings _marginSettings;
        private readonly IRabbitMqNotifyService _rabbitMqNotifyService;
        private readonly ILog _log;
        private readonly OrdersCache _ordersCache;
        private readonly ITradingEngine _tradingEngine;
        private readonly IAccountsApi _accountsApi;
        private readonly IConvertService _convertService;
        private readonly IDateService _dateService;

        private readonly IAccountMarginFreezingRepository _accountMarginFreezingRepository;
        private readonly IAccountMarginUnconfirmedRepository _accountMarginUnconfirmedRepository;

        public AccountManager(
            AccountsCacheService accountsCacheService,
            MarginTradingSettings marginSettings,
            IRabbitMqNotifyService rabbitMqNotifyService,
            ILog log,
            OrdersCache ordersCache,
            ITradingEngine tradingEngine,
            IAccountsApi accountsApi,
            IConvertService convertService,
            IDateService dateService,
            IAccountMarginFreezingRepository accountMarginFreezingRepository,
            IAccountMarginUnconfirmedRepository accountMarginUnconfirmedRepository)
            : base(nameof(AccountManager), 60000, log)
        {
            _accountsCacheService = accountsCacheService;
            _marginSettings = marginSettings;
            _rabbitMqNotifyService = rabbitMqNotifyService;
            _log = log;
            _ordersCache = ordersCache;
            _tradingEngine = tradingEngine;
            _accountsApi = accountsApi;
            _convertService = convertService;
            _dateService = dateService;
            _accountMarginFreezingRepository = accountMarginFreezingRepository;
            _accountMarginUnconfirmedRepository = accountMarginUnconfirmedRepository;
        }

        public override Task Execute()
        {
            //TODO: to think if we need this process, at the current moment it is not used and only increases load on RabbitMq
            //            var accounts = GetAccountsToWriteStats();
            //            var accountsStatsMessages = GenerateAccountsStatsUpdateMessages(accounts);
            //            var tasks = accountsStatsMessages.Select(m => _rabbitMqNotifyService.UpdateAccountStats(m));
            //
            //            return Task.WhenAll(tasks);
            return Task.CompletedTask;
        }

        public override void Start()
        {
            _log.WriteInfo(nameof(Start), nameof(AccountManager), "Starting InitAccountsCache");

            var accounts = _accountsApi.List().GetAwaiter().GetResult()
                .Select(Convert).ToDictionary(x => x.Id);

            _accountsCacheService.InitAccountsCache(accounts);
            _log.WriteInfo(nameof(Start), nameof(AccountManager), $"Finished InitAccountsCache. Count: {accounts.Count}");

            base.Start();
        }

        private IReadOnlyList<IMarginTradingAccount> GetAccountsToWriteStats()
        {
            var accountsIdsToWrite = Enumerable.ToHashSet(_ordersCache.GetPositions().Select(a => a.AccountId).Distinct());
            return _accountsCacheService.GetAll().Where(a => accountsIdsToWrite.Contains(a.Id)).ToList();
        }

        // todo: extract this to a cqrs process
        private IEnumerable<AccountStatsUpdateMessage> GenerateAccountsStatsUpdateMessages(
            IEnumerable<IMarginTradingAccount> accounts)
        {
            return accounts.Select(a => a.ToRabbitMqContract()).Batch(100)
                .Select(ch => new AccountStatsUpdateMessage { Accounts = ch.ToArray() });
        }

        private MarginTradingAccount Convert(AccountContract accountContract)
        {
            var retVal = _convertService.Convert<AccountContract, MarginTradingAccount>(accountContract,
                o => o.ConfigureMap(MemberList.Source)
                    .ForSourceMember(x => x.ModificationTimestamp, c => c.Ignore()));
            // The line below is related to LT-1786 ticket.
            // After restarting core we cannot have LastBalanceChangeTime less than in donut's cache to avoid infinite account reloading
            retVal.LastBalanceChangeTime = _dateService.Now();
            return retVal;
        }
    }
}