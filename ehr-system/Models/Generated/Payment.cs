using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Payment
{
    public int PaymentId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? AppointmentId { get; set; }

    public int? ClaimId { get; set; }

    public int Type { get; set; }

    public int Method { get; set; }

    public decimal Amount { get; set; }

    public string TransactionId { get; set; }

    public string CheckNumber { get; set; }

    public string PayerName { get; set; }

    public int? Status { get; set; }

    public DateOnly PaymentDate { get; set; }

    public string Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public bool? IsRefund { get; set; }

    public int? RefundOfPaymentId { get; set; }

    /// <summary>
    /// Stripe PaymentIntent ID for online payments via patient portal.
    /// </summary>
    public string StripePaymentIntentId { get; set; }

    /// <summary>
    /// Links to InstallmentDetail if this payment is part of an installment plan.
    /// </summary>
    public int? InstallmentDetailId { get; set; }

    /// <summary>Check expiry/date for check payments.</summary>
    public DateOnly? CheckDate { get; set; }

    /// <summary>Card last 4 digits or authorization reference for card payments at POS.</summary>
    public string CardReference { get; set; }

    /// <summary>Reference number for any payment type.</summary>
    public string ReferenceNumber { get; set; }

    /// <summary>
    /// Location where the payment was processed. Required for card payments
    /// to determine which Stripe Connect account to charge.
    /// </summary>
    public int? LocationId { get; set; }

    /// <summary>
    /// Snapshot of which Stripe Connect account processed this payment.
    /// </summary>
    public int? StripeConnectAccountId { get; set; }

    /// <summary>
    /// 0=Online, 1=CardPresent (Phase 2 Tap to Pay), 2=ACH (future)
    /// </summary>
    public int? PaymentMethodType { get; set; }

    /// <summary>
    /// Total fee charged to clinic in cents (e.g., 3.9% + $0.50 of payment amount).
    /// This is what the clinic sees on their contract.
    /// </summary>
    public int? ClinicTotalFeeCents { get; set; }

    /// <summary>
    /// Stripe's processing fee in cents, pulled from balance_transaction.
    /// </summary>
    public int? StripeProcessingFeeCents { get; set; }

    /// <summary>
    /// Platform application fee in cents (MEDOCS profit).
    /// Calculated as ClinicTotalFeeCents - StripeProcessingFeeCents.
    /// </summary>
    public int? ApplicationFeeCents { get; set; }

    /// <summary>
    /// Net amount the clinic received in cents (Amount - Stripe fee - Application fee).
    /// </summary>
    public int? NetToClinicCents { get; set; }

    /// <summary>
    /// Stripe Charge ID (ch_xxx). Different from PaymentIntentId — a single PI can have one charge.
    /// </summary>
    public string StripeChargeId { get; set; }

    /// <summary>
    /// True if this payment has an open dispute (set by charge.dispute.created webhook).
    /// </summary>
    public bool HasOpenDispute { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual BillingClaim Claim { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual InstallmentDetail InstallmentDetail { get; set; }

    public virtual Location Location { get; set; }

    public virtual StripeConnectAccount StripeConnectAccount { get; set; }

    public virtual ICollection<PatientLedger> PatientLedgers { get; set; } = new List<PatientLedger>();

    public virtual ICollection<PaymentRefund> Refunds { get; set; } = new List<PaymentRefund>();

    public virtual Tenant Tenant { get; set; }
}
