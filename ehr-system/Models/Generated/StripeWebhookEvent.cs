using System;

namespace EHR.Models.Generated;

public partial class StripeWebhookEvent
{
    public long Id { get; set; }

    /// <summary>
    /// Stripe's event ID (evt_xxx). Used as idempotency key — if we've already
    /// processed this event ID, we skip it on retries.
    /// </summary>
    public string StripeEventId { get; set; }

    public string EventType { get; set; }

    /// <summary>
    /// Connected account ID (acct_xxx) for Connect events. Null for platform events.
    /// </summary>
    public string StripeAccountId { get; set; }

    /// <summary>
    /// Full JSON payload from Stripe for debugging.
    /// </summary>
    public string Payload { get; set; }

    /// <summary>
    /// 0=Received, 1=Processed, 2=Failed, 3=Ignored
    /// </summary>
    public int Status { get; set; }

    public string ErrorMessage { get; set; }

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}
