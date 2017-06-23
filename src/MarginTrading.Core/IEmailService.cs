﻿using System.Threading.Tasks;

namespace MarginTrading.Core
{
    public interface IEmailService
    {
        Task SendMarginCallEmailAsync(string email, string baseAssetId, string accountId);
        Task SendStopOutEmailAsync(string email, string baseAssetId, string accountId);
    }
}
