using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Orderbooks;

namespace MarginTrading.Backend.Services.Caches
{
    public class ExternalOrderbookCache : IExternalOrderbookCache
    {
        private readonly IDictionary<(string, string), ExternalOrderBook> _dictionary =
            new Dictionary<(string, string), ExternalOrderBook>();

        private readonly ConcurrentDictionary<(string, string), ReaderWriterLockSlim> _locks =
            new ConcurrentDictionary<(string, string), ReaderWriterLockSlim>();

        private void EnsureLockKey((string, string) key)
        {
            if (!_locks.ContainsKey(key))
            {
                _locks.TryAdd(key, new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion));    
            }
        }

        public void Add((string, string) key, ExternalOrderBook value)
        {
            EnsureLockKey(key);
            
            _locks[key].EnterWriteLock();
            try
            {
                _dictionary.Add(key, value);
            }
            finally
            {
                _locks[key].ExitWriteLock();
            }
        }

        private ExternalOrderBook TryReadValue((string, string) key)
        {
            _locks[key].EnterReadLock();
            try
            {
                var success = _dictionary.TryGetValue(key, out var value);
                return value;
            }
            finally
            {
                _locks[key].ExitReadLock();    
            }
        }

        public TResult TryReadValue<TResult>(string assetPair, Func<bool, string, 
            Dictionary<string, ExternalOrderBook>, TResult> readFunc)
        {
            var keys = _dictionary.Keys.Where(x => x.Item1 == assetPair).ToList();
            var orderbooks = keys.Select(TryReadValue).Where(x => x != null).ToDictionary(x => x.ExchangeName, x => x);
            return readFunc(orderbooks.Any(), assetPair, orderbooks);
        }

        public ExternalOrderBook GetOrAdd((string, string) key, Func<(string, string), ExternalOrderBook> valueFactory)
        {
            EnsureLockKey(key);
            
            _locks[key].EnterUpgradeableReadLock();
            try
            {
                if (!_dictionary.TryGetValue(key, out var value))
                {
                    value = valueFactory(key);
                    Add(key, value);
                }

                return value;
            }
            finally
            {
                _locks[key].ExitUpgradeableReadLock();
            }
        }

        public ExternalOrderBook AddOrUpdate((string, string) key, Func<(string, string), ExternalOrderBook> valueFactory,
            Func<(string, string), ExternalOrderBook, ExternalOrderBook> updateValueFactory)
        {
            EnsureLockKey(key);
            
            _locks[key].EnterUpgradeableReadLock();
            try
            {
                var value = !_dictionary.TryGetValue(key, out var oldValue)
                    ? valueFactory(key)
                    : updateValueFactory(key, oldValue);
                
                _dictionary[key] = value;
                return value;
            }
            finally
            {
                _locks[key].ExitUpgradeableReadLock();
            }
        }
    }
}