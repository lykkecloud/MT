﻿// Copyright (c) 2019 Lykke Corp.

using MarginTrading.BrokerBase.Settings;

namespace MarginTrading.ExternalOrderBroker.Settings
{
    public class AppSettings : BrokerSettingsBase
    {
        public DbSettings Db { get; set; }
        public RabbitMqQueuesSettings RabbitMqQueues { get; set; }
    }
}