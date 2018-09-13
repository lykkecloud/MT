﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Exceptions;
using MarginTrading.Backend.Core.Helpers;
using MarginTrading.Backend.Core.Messages;
using MarginTrading.Backend.Core.Repositories;
using MarginTrading.Common.Services;

namespace MarginTrading.Backend.Services
{
    public class AccountsCacheService : IAccountsCacheService
    {
        private Dictionary<string, MarginTradingAccount> _accounts = new Dictionary<string, MarginTradingAccount>();
        private readonly ReaderWriterLockSlim _lockSlim = new ReaderWriterLockSlim();

        private readonly IAccountMarginFreezingRepository _accountMarginFreezingRepository;
        private readonly IDateService _dateService;

        public AccountsCacheService(
            IAccountMarginFreezingRepository accountMarginFreezingRepository,
            IDateService dateService)
        {
            _accountMarginFreezingRepository = accountMarginFreezingRepository;
            _dateService = dateService;
        }
        
        public IReadOnlyList<MarginTradingAccount> GetAll()
        {
            return _accounts.Values.ToArray();
        }

        public PaginatedResponse<MarginTradingAccount> GetAllByPages(int? skip = null, int? take = null)
        {
            var accounts = _accounts.Values.OrderBy(x => x.Id).ToList();//todo think again about ordering
            var data = (!take.HasValue ? accounts : accounts.Skip(skip.Value))
                .Take(PaginationHelper.GetTake(take)).ToList();
            return new PaginatedResponse<MarginTradingAccount>(
                contents: data,
                start: skip ?? 0,
                size: data.Count,
                totalSize: accounts.Count
            );
        }

        public MarginTradingAccount Get(string accountId)
        {
            return TryGetAccount(accountId) ??
                throw new AccountNotFoundException(accountId, string.Format(MtMessages.AccountByIdNotFound, accountId));
        }

        public void Update(MarginTradingAccount newValue)
        {
            _lockSlim.EnterWriteLock();
            try
            {
                newValue.LastUpdateTime = _dateService.Now();
                _accounts[newValue.Id] = newValue;
            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }
        }

        public MarginTradingAccount TryGet(string accountId)
        {
            return TryGetAccount(accountId);
        }

        public IEnumerable<string> GetClientIdsByTradingConditionId(string tradingConditionId, string accountId = null)
        {
            _lockSlim.EnterReadLock();
            try
            {
                foreach (var account in _accounts.Values)
                {
                    if (account.TradingConditionId == tradingConditionId &&
                        (string.IsNullOrEmpty(accountId) || account.Id == accountId))
                        yield return account.ClientId;
                }
            }
            finally
            {
                _lockSlim.ExitReadLock();
            }
        }

        private MarginTradingAccount TryGetAccount(string accountId)
        {
            _lockSlim.EnterReadLock();
            try
            {
                _accounts.TryGetValue(accountId, out var result);

                return result;
            }
            finally
            {
                _lockSlim.ExitReadLock();
            }
        }

        internal void InitAccountsCache(Dictionary<string, MarginTradingAccount> accounts)
        {
            _lockSlim.EnterWriteLock();
            try
            {
                _accounts = accounts;

                var marginFreezings = _accountMarginFreezingRepository.GetAllAsync().GetAwaiter().GetResult()
                    .GroupBy(x => x.AccountId)
                    .ToDictionary(x => x.Key, x => x.ToDictionary(z => z.OperationId, z => z.Amount));
                foreach (var account in accounts.Select(x => x.Value))
                {
                    account.AccountFpl.WithdrawalFrozenMarginData = marginFreezings.TryGetValue(account.Id, out var freezings)
                        ? freezings
                        : new Dictionary<string, decimal>();
                    account.AccountFpl.WithdrawalFrozenMargin = account.AccountFpl.WithdrawalFrozenMarginData.Sum(x => x.Value);
                }
            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }
        }

        public async Task FreezeWithdrawalMargin(string operationId, string clientId, string accountId, decimal amount)
        {
            await _accountMarginFreezingRepository.TryInsertAsync(new AccountMarginFreezing(operationId,
                clientId, accountId, amount));
        }

        public async Task UnfreezeWithdrawalMargin(string operationId)
        {
            await _accountMarginFreezingRepository.DeleteAsync(operationId);
        }

        public void UpdateAccountChanges(string accountId, string updatedTradingConditionId,
            decimal updatedWithdrawTransferLimit, bool isDisabled)
        {
            _lockSlim.EnterWriteLock();
            try
            {
                var account = _accounts[accountId];
                account.TradingConditionId = updatedTradingConditionId;
                account.WithdrawTransferLimit = updatedWithdrawTransferLimit;
                account.IsDisabled = isDisabled;
                account.LastUpdateTime = _dateService.Now();
            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }
        }

        public void UpdateAccountBalance(string accountId, decimal accountBalance)
        {
            _lockSlim.EnterWriteLock();
            try
            {
                var account = _accounts[accountId];
                account.Balance = accountBalance;
                account.LastUpdateTime = _dateService.Now();
            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }
        }

        public void TryAddNew(MarginTradingAccount account)
        {
            _lockSlim.EnterWriteLock();
            try
            {
                account.LastUpdateTime = _dateService.Now();
                _accounts.TryAdd(account.Id, account);
            }
            finally
            {
                _lockSlim.ExitWriteLock();
            }
        }

        public bool CheckEventTimeNewer(string accountId, DateTime eventTime)
        {
            _lockSlim.EnterReadLock();
            try
            {
                var account = _accounts[accountId];
                return account.LastUpdateTime < eventTime;
            }
            finally
            {
                _lockSlim.ExitReadLock();
            }
        }
    }
}