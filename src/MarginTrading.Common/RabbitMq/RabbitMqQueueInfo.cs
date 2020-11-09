// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;
using Lykke.SettingsReader.Attributes;

namespace MarginTrading.Common.RabbitMq
{
    public class RabbitMqQueueInfo
    {
        public string ExchangeName { get; set; }
        
        [Optional]
        public TimeSpan? PublisherConfirmationTimeout { get; set; } 
    }
}