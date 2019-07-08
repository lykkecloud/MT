﻿// Copyright (c) 2019 Lykke Corp.

using System.Collections.Generic;

namespace MarginTrading.Backend.Core.MatchingEngines
{
    public interface IMatchingEngineRepository
    {
        IMatchingEngineBase GetMatchingEngineById(string id);
        ICollection<IMatchingEngineBase> GetMatchingEngines();
    }
}
