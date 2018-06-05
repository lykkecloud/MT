﻿using Lykke.SettingsReader.Attributes;

namespace MarginTrading.Backend.Core.Settings
{
    public class Db
    {
        public string LogsConnString { get; set; }
        public string MarginTradingConnString { get; set; }
        public string HistoryConnString { get; set; }
        public string StateConnString { get; set; }
    }
}