// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Lykke.RabbitMqBroker.Publisher;
using Lykke.RabbitMqBroker.Subscriber;
using RabbitMQ.Client;

namespace MarginTrading.Backend.Services.RabbitMq
{
    public class FanoutPublishStrategyWithConfirms : IRabbitMqPublishStrategy
    {
        private readonly bool _durable;

        public FanoutPublishStrategyWithConfirms(RabbitMqSubscriptionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _durable = settings.IsDurable;
        }

        public void Configure(RabbitMqSubscriptionSettings settings, IModel channel)
        {
            channel.ExchangeDeclare(exchange: settings.ExchangeName, type: "fanout", durable: _durable);
            channel.ConfirmSelect();
        }

        public void Publish(RabbitMqSubscriptionSettings settings, IModel channel, RawMessage message)
        {
            channel.BasicPublish(
                exchange: settings.ExchangeName,
                // routingKey can't be null - I consider this as a bug in RabbitMQ.Client
                routingKey: string.Empty,
                basicProperties: null,
                body: message.Body);
            channel.WaitForConfirmsOrDie(new TimeSpan(0, 0, 5));
        }
    }
}