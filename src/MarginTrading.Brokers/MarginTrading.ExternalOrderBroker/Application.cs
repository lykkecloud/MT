﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Common.Log;
using Lykke.SlackNotifications;
using MarginTrading.BrokerBase;
using MarginTrading.BrokerBase.Settings;
using MarginTrading.ExternalOrderBroker.Models;
using MarginTrading.ExternalOrderBroker.Repositories;

namespace MarginTrading.ExternalOrderBroker
{
    public class Application : BrokerApplicationBase<MarginTrading.Backend.Core.ExchangeConnector.ExecutionReport>
    {
        private readonly IExternalOrderReportRepository _externalOrderReportRepository;
        private readonly Settings.AppSettings _appSettings;

        public Application(IExternalOrderReportRepository externalOrderReportRepository,
            ILog logger,
            Settings.AppSettings appSettings, 
            CurrentApplicationInfo applicationInfo,
            ISlackNotificationsSender slackNotificationsSender) 
            : base(logger, slackNotificationsSender, applicationInfo)
        {
            _externalOrderReportRepository = externalOrderReportRepository;
            _appSettings = appSettings;
        }

        protected override BrokerSettingsBase Settings => _appSettings;
        protected override string ExchangeName => _appSettings.RabbitMqQueues.ExternalOrder.ExchangeName;

        protected override Task HandleMessage(MarginTrading.Backend.Core.ExchangeConnector.ExecutionReport order)
        {
            var externalOrder = ExternalOrderReport.Create(order);
            return _externalOrderReportRepository.InsertOrReplaceAsync(externalOrder);
        }
    }
}