﻿using System;
using System.Collections.Generic;
using Autofac;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Core.Services;
using MarginTrading.Backend.Services;
using MarginTrading.Backend.Services.Events;
using MarginTradingTests.Helpers;
using NUnit.Framework;

namespace MarginTradingTests
{
    [TestFixture]
    public class FplServiceTests : BaseTests
    {
        private IEventChannel<BestPriceChangeEventArgs> _bestPriceConsumer;
        private IFxRateCacheService _fxRateCacheService;
        private IAccountsCacheService _accountsCacheService;
        private OrdersCache _ordersCache;

        [OneTimeSetUp]
        public void SetUp()
        {
            RegisterDependencies();
            _bestPriceConsumer = Container.Resolve<IEventChannel<BestPriceChangeEventArgs>>();
            _fxRateCacheService = Container.Resolve<IFxRateCacheService>();
            _accountsCacheService = Container.Resolve<IAccountsCacheService>();
            _ordersCache = Container.Resolve<OrdersCache>();
        }

        [Test]
        public void Is_Fpl_Buy_Correct()
        {
            const string instrument = "BTCUSD";
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument  = instrument, Ask = 800, Bid = 790 }));
            
            var position = TestObjectsFactory.CreateOpenedPosition(instrument, Accounts[0],
                MarginTradingTestsUtils.TradingConditionId, 10, 790);

            position.UpdateClosePrice(800);

            Assert.AreEqual(100, position.GetFpl());
        }

        [Test]
        public void Is_Fpl_Sell_Correct()
        {
            const string instrument = "BTCUSD";
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = instrument, Ask = 800, Bid = 790 }));

            var position = TestObjectsFactory.CreateOpenedPosition(instrument, Accounts[0],
                MarginTradingTestsUtils.TradingConditionId, -10, 790);
            
            position.UpdateClosePrice(800);

            Assert.AreEqual(-100, position.GetFpl());
        }

        [Test]
        public void Is_Fpl_Correct_With_Commission()
        {
            const string instrument = "BTCUSD";
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = instrument, Ask = 800, Bid = 790 }));

            var position = TestObjectsFactory.CreateOpenedPosition(instrument, Accounts[0],
                MarginTradingTestsUtils.TradingConditionId, 10, 790);

            position.SetCommissionRates(0, 2, 0, 10);
            
            position.UpdateClosePrice(800);

            Assert.AreEqual(80, position.GetTotalFpl());
        }

        [Test]
        public void Is_Fpl_Buy_Cross_Correct()
        {
            const string instrument = "BTCCHF";

            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "USDCHF", Ask = 1.072030M, Bid = 1.071940M }));
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "BTCUSD", Ask = 1001M, Bid = 1000M }));
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "BTCCHF", Ask = 901M, Bid = 900M }));
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair { Instrument = "USDCHF", Ask = 1.072030M, Bid = 1.071940M });
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair { Instrument = "BTCUSD", Ask = 1001M, Bid = 1000M });
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair { Instrument = "BTCCHF", Ask = 901M, Bid = 900M });
            
            var position = TestObjectsFactory.CreateOpenedPosition(instrument, Accounts[0],
                MarginTradingTestsUtils.TradingConditionId, 1000, 935.461M);
            
            position.UpdateClosePrice(935.61M);

            Assert.AreEqual(139m, Math.Round(position.GetFpl(), 3));
        }

        [Test]
        public void Is_Fpl_Sell_Cross_Correct()
        {
            const string instrument = "BTCCHF";

            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "USDCHF", Ask = 1.072030M, Bid = 1.071940M }));
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "BTCUSD", Ask = 1001M, Bid = 1000M }));
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "BTCCHF", Ask = 901M, Bid = 900M }));
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair { Instrument = "USDCHF", Ask = 1.072030M, Bid = 1.071940M });
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair { Instrument = "BTCUSD", Ask = 1001M, Bid = 1000M });
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair {Instrument = "BTCCHF", Ask = 901M, Bid = 900M});

            var position = TestObjectsFactory.CreateOpenedPosition(instrument, Accounts[0],
                MarginTradingTestsUtils.TradingConditionId, -1000, 935.461M);
            
            position.UpdateClosePrice(935.61M);
            var quoteRate = position.GetFplRate();

            Assert.AreEqual(0.9328097161460033767711724485m, quoteRate);
            Assert.AreEqual(-138.989, Math.Round(position.GetFpl(), 3));
        }

        [Test]
        public void Check_Calculations_As_In_Excel_Document()
        {
            Accounts[0].Balance = 50000;
            _accountsCacheService.Update(Accounts[0]);

            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "EURUSD", Ask = 1.061M, Bid = 1.06M }));
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "BTCEUR", Ask = 1092M, Bid = 1091M }));
            _bestPriceConsumer.SendEvent(this, new BestPriceChangeEventArgs(new InstrumentBidAskPair { Instrument = "BTCUSD", Ask = 1001M, Bid = 1000M }));
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair {Instrument = "EURUSD", Ask = 1.061M, Bid = 1.06M});
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair { Instrument = "BTCEUR", Ask = 1092M, Bid = 1091M });
            _fxRateCacheService.SetQuote(new InstrumentBidAskPair { Instrument = "BTCUSD", Ask = 1001M, Bid = 1000M });
            
            var positions = new List<Position>
            {
                TestObjectsFactory.CreateOpenedPosition("EURUSD", Accounts[0],
                MarginTradingTestsUtils.TradingConditionId, 100000, 1.05M),
                
                TestObjectsFactory.CreateOpenedPosition("EURUSD", Accounts[0],
                    MarginTradingTestsUtils.TradingConditionId, -200000, 1.04M),
                
                TestObjectsFactory.CreateOpenedPosition("EURUSD", Accounts[0],
                    MarginTradingTestsUtils.TradingConditionId, 50000, 1.061M),
                
                TestObjectsFactory.CreateOpenedPosition("BTCEUR", Accounts[0],
                    MarginTradingTestsUtils.TradingConditionId, 100, 1120)
            };

            foreach (var position in positions)
            {
                _ordersCache.Positions.Add(position);
            }

            positions[0].UpdateClosePrice(1.06M);
            positions[1].UpdateClosePrice(1.061M);
            positions[2].UpdateClosePrice(1.06M);
            positions[3].UpdateClosePrice(1091M);

            var account = Accounts[0];

            Assert.AreEqual(50000, account.Balance);
            Assert.AreEqual(43676.000, Math.Round(account.GetTotalCapital(), 5));
            Assert.AreEqual(33491.6, Math.Round(account.GetFreeMargin(), 1));
            Assert.AreEqual(28399.4, Math.Round(account.GetMarginAvailable(), 1));
            Assert.AreEqual(-6324.000, Math.Round(account.GetPnl(), 5));
            Assert.AreEqual(10184.4, Math.Round(account.GetUsedMargin(), 1));
            Assert.AreEqual(15276.6, Math.Round(account.GetMarginInit(), 1));

        }
    }
}
