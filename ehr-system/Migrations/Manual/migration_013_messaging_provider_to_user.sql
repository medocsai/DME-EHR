-- Migration 013: Change Patient Messaging from Provider-based to User-based
-- This allows ALL staff roles (Admin, FrontDesk, MA, Nurse, etc.) to message patients
-- Also deletes all existing test conversations/messages since system is not in production
-- Run BEFORE deploying the updated code

-- ============================================
-- Step 1: Delete all existing test data
-- ============================================
DELETE FROM PatientMessages;
DELETE FROM PatientConversations;

-- ============================================
-- Step 2: PatientConversations — ProviderId → UserId
-- ============================================

-- Drop existing FK and indexes that reference ProviderId
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientConversations_ProviderId')
    ALTER TABLE PatientConversations DROP CONSTRAINT FK_PatientConversations_ProviderId;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientConversations_UniqueParticipants' AND object_id = OBJECT_ID('PatientConversations'))
    DROP INDEX IX_PatientConversations_UniqueParticipants ON PatientConversations;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientConversations_TenantId_ProviderId' AND object_id = OBJECT_ID('PatientConversations'))
    DROP INDEX IX_PatientConversations_TenantId_ProviderId ON PatientConversations;

-- Rename column
EXEC sp_rename 'PatientConversations.ProviderId', 'UserId', 'COLUMN';

-- Add new FK to Users table
ALTER TABLE PatientConversations
    ADD CONSTRAINT FK_PatientConversations_UserId
    FOREIGN KEY (UserId) REFERENCES Users(UserId);

-- Recreate unique index (one conversation per patient-user pair per tenant)
CREATE UNIQUE INDEX IX_PatientConversations_UniqueParticipants
    ON PatientConversations (TenantId, PatientId, UserId);

-- Index for finding conversations by user
CREATE INDEX IX_PatientConversations_TenantId_UserId
    ON PatientConversations (TenantId, UserId);

-- ============================================
-- Step 3: PatientMessages — SenderProviderId → SenderUserId
-- ============================================

-- Drop existing FK
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientMessages_SenderProviderId')
    ALTER TABLE PatientMessages DROP CONSTRAINT FK_PatientMessages_SenderProviderId;

-- Rename column
EXEC sp_rename 'PatientMessages.SenderProviderId', 'SenderUserId', 'COLUMN';

-- Add new FK to Users table
ALTER TABLE PatientMessages
    ADD CONSTRAINT FK_PatientMessages_SenderUserId
    FOREIGN KEY (SenderUserId) REFERENCES Users(UserId);

PRINT 'Migration 013 complete: Patient messaging changed from Provider-based to User-based';
