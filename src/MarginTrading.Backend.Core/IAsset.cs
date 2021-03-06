﻿// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

namespace MarginTrading.Backend.Core
{
    public interface IAsset
    {
        string Id { get; }
        string Name { get; }
        int Accuracy { get; }
    }

    public class Asset : IAsset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Accuracy { get; set; }
    }
}