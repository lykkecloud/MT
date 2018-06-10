using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common;
using Common.Log;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Core.Settings;

namespace MarginTrading.Backend.Services
{
    public class OrderCacheManager : TimerPeriod
    {
        private readonly OrdersCache _orderCache;
        private readonly IMarginTradingBlobRepository _marginTradingBlobRepository;
        private readonly ILog _log;
        private readonly IAccountsCacheService _accountsCacheService;
        private const string BlobName= "orders";

        public OrderCacheManager(OrdersCache orderCache,
            IMarginTradingBlobRepository marginTradingBlobRepository,
            MarginTradingSettings marginTradingSettings,
            ILog log, IAccountsCacheService accountsCacheService) 
            : base(nameof(OrderCacheManager), marginTradingSettings.BlobPersistence.OrdersDumpPeriodMilliseconds, log)
        {
            _orderCache = orderCache;
            _marginTradingBlobRepository = marginTradingBlobRepository;
            _log = log;
            _accountsCacheService = accountsCacheService;
        }

        public override void Start()
        {
            var orders = _marginTradingBlobRepository.Read<List<Position>>(LykkeConstants.StateBlobContainer, BlobName) ?? new List<Position>();
            
            orders.ForEach(o =>
            {
                // migrate orders to add LegalEntity field
                // todo: can be removed once published to prod
                if (o.LegalEntity == null)
                    o.LegalEntity = _accountsCacheService.Get(o.AccountId).LegalEntity;
            });

            _orderCache.InitOrders(orders);

            base.Start();
        }

        public override async Task Execute()
        {
            await DumpToRepository();
        }

        public override void Stop()
        {
            DumpToRepository().Wait();
            base.Stop();
        }

        private async Task DumpToRepository()
        {

            try
            {
                var orders = _orderCache.GetAll();

                if (orders != null)
                {
                    await _marginTradingBlobRepository.Write(LykkeConstants.StateBlobContainer, BlobName, orders);
                }
            }
            catch (Exception ex)
            {
                await _log.WriteErrorAsync(nameof(OrdersCache), "Save orders", "", ex);
            }
        }
    }
}