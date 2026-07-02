-- Migration 014: Rename Provider-named columns to User-named columns
-- PatientConversations: ProviderUnreadCount → UserUnreadCount
-- PatientMessages: IsReadByProvider → IsReadByUser, ReadByProviderAt → ReadByUserAt
-- Also update filtered index that references the old column name

-- ============================================
-- Step 1: PatientConversations — ProviderUnreadCount → UserUnreadCount
-- ============================================
EXEC sp_rename 'PatientConversations.ProviderUnreadCount', 'UserUnreadCount', 'COLUMN';

-- ============================================
-- Step 2: PatientMessages — IsReadByProvider → IsReadByUser
-- ============================================

-- Drop the old filtered index first (it references the old column name in filter)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientMessages_UnreadByProvider' AND object_id = OBJECT_ID('PatientMessages'))
    DROP INDEX IX_PatientMessages_UnreadByProvider ON PatientMessages;

EXEC sp_rename 'PatientMessages.IsReadByProvider', 'IsReadByUser', 'COLUMN';

-- Recreate index with new column name
CREATE INDEX IX_PatientMessages_UnreadByUser
    ON PatientMessages (TenantId, IsReadByUser)
    WHERE [IsReadByUser] = 0 AND [SenderType] = 'Patient';

-- ============================================
-- Step 3: PatientMessages — ReadByProviderAt → ReadByUserAt
-- ============================================
EXEC sp_rename 'PatientMessages.ReadByProviderAt', 'ReadByUserAt', 'COLUMN';

PRINT 'Migration 014 complete: Renamed Provider columns to User columns';
