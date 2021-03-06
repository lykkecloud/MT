// Copyright (c) 2019 Lykke Corp.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarginTrading.Backend.Services.TradingConditions
{
    /// <summary>
    /// Container for the current margin parameter state.
    /// </summary>
    public interface IOvernightMarginParameterContainer
    {
        /// <summary>
        /// Get state for the intraday margin parameter. 
        /// </summary>
        bool GetOvernightMarginParameterState();

        /// <summary>
        /// Set multiplier for the intraday margin parameter to be active at night. 
        /// </summary>
        void SetOvernightMarginParameterState(bool isOn);

        /// <summary>
        /// Get overnight margin parameter values, which depends on state and asset pair's multiplier.
        /// </summary>
        /// <param name="onlyNotEqualToOne"></param>
        Dictionary<(string, string), decimal> GetOvernightMarginParameterValues(bool onlyNotEqualToOne = false);
    }
}