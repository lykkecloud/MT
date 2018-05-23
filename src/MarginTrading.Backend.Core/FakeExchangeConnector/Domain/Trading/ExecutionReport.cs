﻿using System;
using Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MarginTrading.Backend.Core.FakeExchangeConnector.Domain.Trading
{
    public class ExecutionReport
    {
        /// <summary>
        /// A client assigned ID of the order
        /// </summary>
        public string ClientOrderId { get;  set; }

        /// <summary>
        /// An exchange assigned ID of the order
        /// </summary>
        public string ExchangeOrderId { get;  set; }

        /// <summary>
        /// An instrument description
        /// </summary>
        public Instrument Instrument { get;  set; }

        /// <summary>
        /// A trade direction
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public TradeType Type { get;  set; }

        /// <summary>
        /// Transaction time
        /// </summary>
        public DateTime Time { get;  set; }

        /// <summary>
        /// An actual price of the execution or order
        /// </summary>
        public decimal Price { get;  set; }

        /// <summary>
        /// Trade volume
        /// </summary>
        public decimal Volume { get;  set; }

        /// <summary>
        /// Execution fee
        /// </summary>
        public decimal Fee { get;  set; }

        /// <summary>
        /// Fee currency
        /// </summary>
        public string FeeCurrency { get;  set; }

        /// <summary>
        /// Indicates that operation was successful
        /// </summary>
        public bool Success { get;  set; }

        /// <summary>
        /// Current status of the order
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public OrderExecutionStatus ExecutionStatus { get;  set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public OrderStatusUpdateFailureType FailureType { get;  set; }

        /// <summary>
        /// An arbitrary message from the exchange related to the execution|order 
        /// </summary>
        public string Message { get;  set; }

        /// <summary>
        /// A type of the order
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public OrderType OrderType { get;  set; }

        /// <summary>
        /// A type of the execution. ExecType = Trade means it is an execution, otherwise it is an order
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ExecType ExecType { get;  set; }

        public ExecutionReport()
        {

        }

        [JsonConstructor]
        public ExecutionReport(Instrument instrument, DateTime time, decimal price,
            decimal volume, TradeType type, string orderId, OrderExecutionStatus executionStatus)
        {
            Instrument = instrument;
            Time = time;
            Price = price;
            Volume = volume;
            Type = type;
            Fee = 0; // TODO
            ExchangeOrderId = orderId;
            ExecutionStatus = executionStatus;
        }

        public override string ToString()
        {
            return this.ToJson();
        }
    }
}
