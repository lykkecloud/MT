// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Lykke.RabbitMqBroker.Publisher;
using MarginTrading.Backend.Core;

namespace MarginTrading.Backend.Services.RabbitMq
{
    public class PublishingQueueRepository : IPublishingQueueRepository
    {
        private readonly IMarginTradingBlobRepository _blobRepository;

        private const string BlobContainer = "PublishingQueue";
        
        public PublishingQueueRepository(IMarginTradingBlobRepository blobRepository)
        {
            _blobRepository = blobRepository;
        }
        
        public async Task SaveAsync(IReadOnlyCollection<RawMessage> items, string exchangeName)
        {
            await _blobRepository.WriteAsync(BlobContainer, exchangeName, items);
        }

        public async Task<IReadOnlyCollection<RawMessage>> LoadAsync(string exchangeName)
        {
            return await _blobRepository.ReadAsync<List<RawMessage>>(BlobContainer, exchangeName);
        }
    }
}