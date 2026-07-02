-- ============================================
-- STRIPE CONNECT MIGRATION 010
-- Cleanup test payment data — system not in production, clean slate
-- IMPORTANT: Run this only in dev/sandbox environments
-- This will WIPE all card-based payments, installment plans, and copay tokens
-- Cash/check payments are preserved
-- ============================================

-- 1. Cancel all installment plans and details (cascading)
DELETE FROM InstallmentPlanAuditLog;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' rows from InstallmentPlanAuditLog');

-- Break the Payments <-> InstallmentDetails circular FK before deleting
UPDATE Payments SET InstallmentDetailId = NULL WHERE InstallmentDetailId IS NOT NULL;
PRINT CONCAT('Cleared ', @@ROWCOUNT, ' Payment.InstallmentDetailId references');

UPDATE InstallmentDetails SET PaymentId = NULL WHERE PaymentId IS NOT NULL;
PRINT CONCAT('Cleared ', @@ROWCOUNT, ' InstallmentDetail.PaymentId references');

DELETE FROM InstallmentDetails;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' rows from InstallmentDetails');

DELETE FROM InstallmentPlans;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' rows from InstallmentPlans');

-- 2. Delete copay payment tokens
DELETE FROM CopayPaymentTokens;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' rows from CopayPaymentTokens');

-- 3. Delete payment refunds (synced from Stripe)
DELETE FROM PaymentRefunds;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' rows from PaymentRefunds');

-- 4. Delete patient ledger entries linked to Stripe payments
DELETE pl
FROM PatientLedgers pl
INNER JOIN Payments p ON pl.PaymentId = p.PaymentId
WHERE p.StripePaymentIntentId IS NOT NULL;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' Stripe-linked PatientLedger entries');

-- 5. Delete Stripe payment records
DELETE FROM Payments WHERE StripePaymentIntentId IS NOT NULL;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' Stripe Payment rows');

-- 6. Clear webhook event history (start fresh)
DELETE FROM StripeWebhookEvents;
PRINT CONCAT('Deleted ', @@ROWCOUNT, ' rows from StripeWebhookEvents');

-- 7. Reset Patients.StripeCustomerId (orphaned customer references)
UPDATE Patients SET StripeCustomerId = NULL WHERE StripeCustomerId IS NOT NULL;
PRINT CONCAT('Cleared ', @@ROWCOUNT, ' Patient.StripeCustomerId values');

PRINT 'Migration 010 complete — test payment data wiped';
