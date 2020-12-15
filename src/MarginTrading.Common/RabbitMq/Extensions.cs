// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;

namespace MarginTrading.Common.RabbitMq
{
    public static class Extensions
    {
        public static RabbitMqSettings ToDomain(this RabbitMqQueueInfo src, string connectionString, bool isDurable = true)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            
            if (src == null)
                throw new ArgumentNullException(nameof(src));
            
            return new RabbitMqSettings
            {
                ConnectionString = connectionString,
                ExchangeName = src.ExchangeName,
                IsDurable = isDurable,
                PublisherConfirmationTimeout = src.PublisherConfirmationTimeout 
            };
        }
    }
}