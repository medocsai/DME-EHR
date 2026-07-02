-- ============================================
-- STRIPE CONNECT MIGRATION 008
-- Creates InstallmentPlanAuditLog table
-- Forensic trail for installment plan state changes
-- "No issues tolerated" requires complete debugging history
-- ============================================

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('InstallmentPlanAuditLog') AND type = 'U')
BEGIN
    CREATE TABLE InstallmentPlanAuditLog (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        PlanId              INT NOT NULL,
        DetailId            INT NULL,
        TenantId            INT NOT NULL,
        Action              NVARCHAR(50) NOT NULL,  -- Created, ChargeAttempted, ChargeSucceeded, ChargeFailed, Retried, Defaulted, Completed, Cancelled, Disconnected
        OldStatus           INT NULL,
        NewStatus           INT NULL,
        Details             NVARCHAR(MAX) NULL,  -- Free text or JSON with extra context
        ActorType           NVARCHAR(20) NOT NULL,  -- System, Webhook, User
        ActorId             INT NULL,  -- UserId if Actor=User
        CreatedAt           DATETIME NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT FK_InstallmentPlanAuditLog_Plans FOREIGN KEY (PlanId)
            REFERENCES InstallmentPlans(PlanId),
        CONSTRAINT FK_InstallmentPlanAuditLog_Details FOREIGN KEY (DetailId)
            REFERENCES InstallmentDetails(DetailId),
        CONSTRAINT FK_InstallmentPlanAuditLog_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId)
    );

    CREATE INDEX IX_InstallmentPlanAuditLog_PlanId
        ON InstallmentPlanAuditLog(PlanId);

    CREATE INDEX IX_InstallmentPlanAuditLog_DetailId
        ON InstallmentPlanAuditLog(DetailId);

    CREATE INDEX IX_InstallmentPlanAuditLog_CreatedAt
        ON InstallmentPlanAuditLog(CreatedAt DESC);

    CREATE INDEX IX_InstallmentPlanAuditLog_Action
        ON InstallmentPlanAuditLog(Action);

    PRINT 'Created InstallmentPlanAuditLog table';
END
ELSE
BEGIN
    PRINT 'InstallmentPlanAuditLog table already exists';
END
