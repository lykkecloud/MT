﻿// Copyright (c) 2019 Lykke Corp.

using System.Threading.Tasks;

namespace MarginTrading.Backend.Core
{
	public interface IMarginTradingIndividualValuesAtRiskRepository
	{
		Task InsertOrUpdateAsync(string counterPartyId, string assetId, decimal value);
	}
}
