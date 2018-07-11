using System;
using JetBrains.Annotations;

namespace MarginTrading.Backend.Contracts
{
    /// <summary>
    /// Use this class as base on any message to enable traceability
    /// </summary>
    public class TraceableMessageBase
    {
        /// <summary>
        /// Unique operation Id, can be used for deduplication.
        /// Each message should have its own unique identifier. Whenever a new message is created a new unique
        /// identifier should be assigned so that that instance of the message can be tracked.
        /// </summary>
        [NotNull]
        public string Id { get; private set; }
         
        /// <summary>
        /// The correlation identifier.
        /// In every operation that results in the creation of a new message the correlationId should be copied from
        /// the inbound message to the outbound message. This facilitates tracking of an operation through the system.
        /// If there is no inbound identifier then one should be created eg. on the service layer boundary (API).  
        /// </summary>
        [NotNull]
        public string CorrelationId { get; private set; }
        
        /// <summary>
        /// The causation identifier.
        /// In every operation that results in the creation of a new message the causationId should be copied from the
        /// unique message identifier of the inbound message. This facilitates tracking the cause of each linked
        /// operation throughout the system.
        /// If there is no inbound message then the causationId should be left blank (read: null).
        /// </summary>
        [CanBeNull]
        public string CausationId { get; private set; }
        
        /// <summary>
        /// Event creation time stamp in UTC time.
        /// </summary>
        public DateTime EventTimestamp { get; private set; }

        public TraceableMessageBase(TraceableMessageBase baseMessage)
        {
            //todo fill
            Id = Guid.NewGuid().ToString("N");
            CorrelationId = baseMessage.CorrelationId;
            CausationId = baseMessage.Id;
            EventTimestamp = _systemClock.;
        }
        
        public TraceableMessageBase Create([NotNull] string id, [NotNull] string correlationId, [CanBeNull] string causationId,
            DateTime eventTimestamp)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
            CausationId = causationId;
            EventTimestamp = eventTimestamp;
        }
    }
}