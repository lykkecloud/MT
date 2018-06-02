﻿using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using MarginTrading.Backend.Contracts.Orders;
using Refit;

namespace MarginTrading.Backend.Contracts
{
    /// <summary>
    /// API for performing operations with orders and reading current state
    /// </summary>
    [PublicAPI]
    public interface IOrdersApi
    {
        /// <summary>
        /// Place new order
        /// </summary>
        [Post("/api/orders")]
        Task PlaceAsync([Body][NotNull] OrderPlaceRequest request);

        /// <summary>
        /// Change existing order
        /// </summary>
        [Put("/api/orders/{orderId}")]
        Task ChangeAsync([NotNull] string orderId, [Body][NotNull] OrderChangeRequest request);

        /// <summary>
        /// Close existing order 
        /// </summary>
        [Delete("/api/orders/{orderId}")]
        Task CancelAsync([NotNull] string orderId);

        /// <summary>
        /// Get order by id 
        /// </summary>
        [Get("/api/orders/{orderId}"), ItemCanBeNull]
        Task<OrderContract> GetAsync([NotNull] string orderId);

        /// <summary>
        /// Get open orders with optional filtering
        /// </summary>
        [Get("/api/orders")]
        Task<List<OrderContract>> ListAsync([Query, CanBeNull] string accountId = null,
            [Query, CanBeNull] string assetPairId = null, [Query, CanBeNull] string parentPositionId = null,
            [Query, CanBeNull] string parentOrderId = null);
    }
}