﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System;
using MarginTrading.Backend.Core.MatchingEngines;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Common.Extensions;
using MarginTrading.Contract.BackendContracts;

namespace MarginTrading.Backend.Core.Mappers
{
    public class DomainObjectsFactory
    {
        public static IMatchingEngineRoute CreateRoute(NewMatchingEngineRouteRequest request, string id = null)
        {
            return new MatchingEngineRoute
            {
                Id = id ?? Guid.NewGuid().ToString().ToUpper(),
                Rank = request.Rank,
                TradingConditionId = request.TradingConditionId,
                ClientId = request.ClientId,
                Instrument = request.Instrument,
                Type = request.Type?.ToType<OrderDirection>(),
                MatchingEngineId = request.MatchingEngineId,
                Asset = request.Asset
            };
        }
    }
}