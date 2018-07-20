using System;
using JetBrains.Annotations;
using MessagePack;

namespace MarginTrading.Backend.Contracts
{
    /// <summary>
    /// Use this class as base on any message to enable traceability
    /// </summary>
    [MessagePackObject]
    public class TraceableMessageBase
    {
        /// <summary>
        /// Unique operation Id, can be used for deduplication.
        /// Each message should have its own unique identifier. Whenever a new message is created a new unique
        /// identifier should be assigned so that that instance of the message can be tracked.
        /// </summary>
        [NotNull]
        [Key(0)]
        public string Id { get; }
         
        /// <summary>
        /// The correlation identifier.
        /// In every operation that results in the creation of a new message the correlationId should be copied from
        /// the inbound message to the outbound message. This facilitates tracking of an operation through the system.
        /// If there is no inbound identifier then one should be created eg. on the service layer boundary (API).  
        /// </summary>
        [NotNull]
        [Key(1)]
        public string CorrelationId { get; }
        
        /// <summary>
        /// The causation identifier.
        /// In every operation that results in the creation of a new message the causationId should be copied from the
        /// unique message identifier of the inbound message. This facilitates tracking the cause of each linked
        /// operation throughout the system.
        /// If there is no inbound message then the causationId should be left blank (read: null).
        /// </summary>
        [CanBeNull]
        [Key(2)]
        public string CausationId { get; }
        
        /// <summary>
        /// Event creation time stamp in UTC time.
        /// </summary>
        [Key(3)]
        public DateTime EventTimestamp { get; }

        public TraceableMessageBase([NotNull] string correlationId, [CanBeNull] string causationId,
            DateTime eventTimestamp) : this (Guid.NewGuid().ToString("N"), correlationId, causationId, eventTimestamp)
        {
        }
        
        public TraceableMessageBase([NotNull] string id, [NotNull] string correlationId, [CanBeNull] string causationId,
            DateTime eventTimestamp)
        {
            Id = id;
            CorrelationId = correlationId;
            CausationId = causationId;
            EventTimestamp = eventTimestamp;
        }
    }
}