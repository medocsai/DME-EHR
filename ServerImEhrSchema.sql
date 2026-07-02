USE [IMEHR]
GO
/****** Object:  Table [dbo].[Appointments]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Appointments](
	[AppointmentId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[LocationId] [int] NULL,
	[CareEpisodeId] [int] NULL,
	[Type] [int] NOT NULL,
	[StartTime] [datetime2](7) NOT NULL,
	[EndTime] [datetime2](7) NOT NULL,
	[Status] [int] NULL,
	[Reason] [nvarchar](max) NULL,
	[Notes] [nvarchar](max) NULL,
	[IsRecurring] [bit] NULL,
	[RecurrenceParentId] [int] NULL,
	[RecurrencePattern] [nvarchar](100) NULL,
	[CheckInTime] [datetime2](7) NULL,
	[CheckOutTime] [datetime2](7) NULL,
	[IsTelehealth] [bit] NULL,
	[TelehealthUrl] [nvarchar](500) NULL,
	[CopayDue] [decimal](10, 2) NULL,
	[CopayCollected] [decimal](10, 2) NULL,
	[InsuranceVerified] [bit] NULL,
	[ReminderSentAt] [datetime2](7) NULL,
	[Reminder24hEnabled] [bit] NOT NULL,
	[Reminder1hEnabled] [bit] NOT NULL,
	[Reminder24hSentAt] [datetime2](7) NULL,
	[Reminder1hSentAt] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[CreatedByUserId] [int] NULL,
	[CancellationReason] [nvarchar](max) NULL,
	[CancelledByUserId] [int] NULL,
	[CancelledAt] [datetime2](7) NULL,
	[RescheduledToAppointmentId] [int] NULL,
	[RescheduledFromAppointmentId] [int] NULL,
	[TelehealthToken] [nvarchar](max) NULL,
	[CreatedByPatientId] [int] NULL,
 CONSTRAINT [PK__Appointm__8ECDFCC26AA138E3] PRIMARY KEY CLUSTERED 
(
	[AppointmentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[AuditLogs]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[AuditLogs](
	[AuditId] [bigint] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NULL,
	[UserId] [int] NULL,
	[UserEmail] [nvarchar](100) NULL,
	[EntityType] [nvarchar](100) NOT NULL,
	[EntityId] [int] NULL,
	[Action] [nvarchar](50) NOT NULL,
	[OldValues] [nvarchar](max) NULL,
	[NewValues] [nvarchar](max) NULL,
	[Changes] [nvarchar](max) NULL,
	[IpAddress] [nvarchar](50) NULL,
	[UserAgent] [nvarchar](500) NULL,
	[Timestamp] [datetime2](7) NULL,
 CONSTRAINT [PK__AuditLog__A17F2398BE9350B9] PRIMARY KEY CLUSTERED 
(
	[AuditId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Authorizations]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Authorizations](
	[AuthorizationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[InsuranceId] [int] NOT NULL,
	[AuthorizationNumber] [nvarchar](100) NOT NULL,
	[ExpiryDate] [date] NULL,
	[AuthorizedVisits] [int] NOT NULL,
	[DateOfValidation] [datetime2](7) NOT NULL,
	[Notes] [nvarchar](1000) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK__Authorizations] PRIMARY KEY CLUSTERED 
(
	[AuthorizationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[BillingClaims]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[BillingClaims](
	[ClaimId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[InsuranceId] [int] NULL,
	[ClaimNumber] [nvarchar](50) NULL,
	[PayerClaimNumber] [nvarchar](50) NULL,
	[ServiceDateFrom] [date] NOT NULL,
	[ServiceDateTo] [date] NOT NULL,
	[TotalCharged] [decimal](10, 2) NOT NULL,
	[TotalAllowed] [decimal](10, 2) NULL,
	[TotalPaid] [decimal](10, 2) NULL,
	[TotalAdjustment] [decimal](10, 2) NULL,
	[PatientResponsibility] [decimal](10, 2) NULL,
	[Status] [int] NULL,
	[Type] [int] NULL,
	[SubmittedAt] [datetime2](7) NULL,
	[ProcessedAt] [datetime2](7) NULL,
	[DenialReasonCode] [nvarchar](50) NULL,
	[DenialReason] [nvarchar](500) NULL,
	[EDIData] [nvarchar](max) NULL,
	[ResponseData] [nvarchar](max) NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[CreatedBy] [int] NULL,
	[ClinicalNoteId] [int] NULL,
	[AppointmentId] [int] NULL,
	[CareEpisodeId] [int] NULL,
	[AuthorizationId] [int] NULL,
	[ProviderId] [int] NULL,
	[LocationId] [int] NULL,
	[InsuranceTypeCode] [int] NULL,
	[PatientSignatureOnFile] [bit] NULL,
	[InsuredSignatureOnFile] [bit] NULL,
	[ReferringProviderName] [nvarchar](200) NULL,
	[ReferringProviderNpi] [nvarchar](20) NULL,
	[DiagnosisCodes] [nvarchar](max) NULL,
	[PriorAuthorizationNumber] [nvarchar](100) NULL,
	[PlaceOfServiceCode] [nvarchar](10) NULL,
	[FederalTaxId] [nvarchar](20) NULL,
	[PatientAccountNumber] [nvarchar](50) NULL,
	[AcceptAssignment] [bit] NULL,
	[AmountPaid] [decimal](10, 2) NULL,
	[RenderingProviderName] [nvarchar](200) NULL,
	[RenderingProviderNpi] [nvarchar](20) NULL,
	[FacilityName] [nvarchar](200) NULL,
	[FacilityAddress] [nvarchar](500) NULL,
	[FacilityNpi] [nvarchar](20) NULL,
	[BillingProviderName] [nvarchar](200) NULL,
	[BillingProviderAddress] [nvarchar](500) NULL,
	[BillingProviderNpi] [nvarchar](20) NULL,
	[BillingProviderTaxonomy] [nvarchar](20) NULL,
	[InsuredName] [nvarchar](200) NULL,
	[InsuredAddress] [nvarchar](500) NULL,
	[InsuredCity] [nvarchar](100) NULL,
	[InsuredState] [nvarchar](10) NULL,
	[InsuredZip] [nvarchar](20) NULL,
	[InsuredDob] [date] NULL,
	[InsuredGender] [nvarchar](10) NULL,
	[InsuredPolicyNumber] [nvarchar](50) NULL,
	[InsuredGroupNumber] [nvarchar](50) NULL,
	[SubscriberRelationship] [nvarchar](20) NULL,
	[TypeOfBill] [nvarchar](10) NULL,
	[AdmissionDate] [date] NULL,
	[AdmissionType] [int] NULL,
	[PatientDischargeStatus] [nvarchar](10) NULL,
	[ConditionCodes] [nvarchar](max) NULL,
	[ValueCodes] [nvarchar](max) NULL,
	[OccurrenceCodes] [nvarchar](max) NULL,
 CONSTRAINT [PK__Claims__EF2E139B3BE776A8] PRIMARY KEY CLUSTERED 
(
	[ClaimId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[CareEpisodeConsentForms]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CareEpisodeConsentForms](
	[CareEpisodeConsentFormId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[CareEpisodeConsentId] [int] NOT NULL,
	[ConsentFormTemplateId] [int] NULL,
	[TemplateVersion] [int] NOT NULL,
	[FormName] [nvarchar](200) NOT NULL,
	[RenderedHtmlContent] [nvarchar](max) NOT NULL,
	[SignaturesJson] [nvarchar](max) NOT NULL,
	[ViewedAt] [datetime2](7) NULL,
	[ViewDurationSeconds] [int] NULL,
	[DisplayOrder] [int] NOT NULL,
	[SignedAt] [datetime2](7) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_CareEpisodeConsentForms] PRIMARY KEY CLUSTERED 
(
	[CareEpisodeConsentFormId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[CareEpisodeConsents]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CareEpisodeConsents](
	[CareEpisodeConsentId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[CareEpisodeId] [int] NULL,
	[PatientId] [int] NOT NULL,
	[AppointmentId] [int] NULL,
	[LocationId] [int] NOT NULL,
	[ConsentType] [int] NOT NULL,
	[SignedAt] [datetime2](7) NOT NULL,
	[EncryptedPdfData] [nvarchar](max) NULL,
	[PdfHash] [nvarchar](100) NULL,
	[VerificationSsnLast4Encrypted] [nvarchar](max) NULL,
	[VerificationDob] [date] NOT NULL,
	[VerificationZipCode] [nvarchar](20) NULL,
	[IpAddress] [nvarchar](50) NULL,
	[UserAgent] [nvarchar](500) NULL,
	[FormCount] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_CareEpisodeConsents] PRIMARY KEY CLUSTERED 
(
	[CareEpisodeConsentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[CareEpisodes]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CareEpisodes](
	[CareEpisodeId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[PrimaryProviderId] [int] NULL,
	[StartDate] [date] NOT NULL,
	[EndDate] [date] NULL,
	[PrimaryDiagnosisCode] [nvarchar](20) NULL,
	[PrimaryDiagnosisDescription] [nvarchar](500) NULL,
	[DiagnosisNotes] [nvarchar](max) NULL,
	[SecondaryDiagnoses] [nvarchar](max) NULL,
	[Goals] [nvarchar](max) NULL,
	[PlanOfCare] [nvarchar](max) NULL,
	[PhysicianName] [nvarchar](max) NULL,
	[ExpectedVisits] [int] NULL,
	[VisitFrequency] [int] NULL,
	[Status] [int] NULL,
	[DischargeReason] [nvarchar](max) NULL,
	[CompletionMethod] [int] NULL,
	[CompletedAt] [datetime2](7) NULL,
	[CompletedByUserId] [int] NULL,
	[CopayAmount] [decimal](18, 2) NULL,
	[CopayVisits] [int] NULL,
	[MissedVisits] [int] NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[ReferringProviderNpi] [nvarchar](20) NULL,
 CONSTRAINT [PK__CareEpis__C57E6C09E3C7B978] PRIMARY KEY CLUSTERED 
(
	[CareEpisodeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Charges]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Charges](
	[ChargeId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[NoteId] [int] NULL,
	[ClinicalNoteId] [int] NULL,
	[AppointmentId] [int] NULL,
	[ProviderId] [int] NOT NULL,
	[ServiceDate] [date] NOT NULL,
	[CPTCode] [nvarchar](10) NOT NULL,
	[CPTDescription] [nvarchar](200) NULL,
	[Units] [int] NULL,
	[Modifier1] [nvarchar](10) NULL,
	[Modifier2] [nvarchar](10) NULL,
	[Modifier3] [nvarchar](10) NULL,
	[Modifier4] [nvarchar](10) NULL,
	[ICDPointers] [nvarchar](max) NULL,
	[ChargeAmount] [decimal](10, 2) NOT NULL,
	[AllowedAmount] [decimal](10, 2) NULL,
	[PaidAmount] [decimal](10, 2) NULL,
	[AdjustmentAmount] [decimal](10, 2) NULL,
	[PatientResponsibility] [decimal](10, 2) NULL,
	[Status] [int] NULL,
	[ClaimId] [int] NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[RevenueCode] [nvarchar](10) NULL,
	[LocationId] [int] NULL,
 CONSTRAINT [PK__Charges__17FC361B518C6080] PRIMARY KEY CLUSTERED 
(
	[ChargeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ClaimStatusHistories]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClaimStatusHistories](
	[HistoryId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[ClaimId] [int] NOT NULL,
	[Status] [int] NOT NULL,
	[Notes] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[CreatedBy] [int] NULL,
 CONSTRAINT [PK__ClaimSta__4D7B4ABDC7D0B9B1] PRIMARY KEY CLUSTERED 
(
	[HistoryId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ClinicalNoteAddendums]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClinicalNoteAddendums](
	[AddendumId] [int] IDENTITY(1,1) NOT NULL,
	[ClinicalNoteId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[Content] [nvarchar](max) NOT NULL,
	[Reason] [nvarchar](500) NOT NULL,
	[SignedAt] [datetime2](7) NOT NULL,
	[SignedByUserId] [int] NOT NULL,
	[SignatureData] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_ClinicalNoteAddendums] PRIMARY KEY CLUSTERED 
(
	[AddendumId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ClinicalNoteAmendments]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClinicalNoteAmendments](
	[AmendmentId] [int] IDENTITY(1,1) NOT NULL,
	[ClinicalNoteId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[VersionNumber] [int] NOT NULL,
	[HtmlContent] [nvarchar](max) NOT NULL,
	[Reason] [nvarchar](500) NOT NULL,
	[SignedAt] [datetime2](7) NOT NULL,
	[SignedByUserId] [int] NOT NULL,
	[SignatureData] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_ClinicalNoteAmendments] PRIMARY KEY CLUSTERED 
(
	[AmendmentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UX_ClinicalNoteAmendments_NoteVersion] UNIQUE NONCLUSTERED 
(
	[ClinicalNoteId] ASC,
	[VersionNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ClinicalNotes]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClinicalNotes](
	[ClinicalNoteId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[AppointmentId] [int] NULL,
	[TemplateId] [int] NULL,
	[Type] [int] NOT NULL,
	[Status] [int] NOT NULL,
	[ServiceDate] [date] NOT NULL,
	[HtmlContent] [nvarchar](max) NOT NULL,
	[SearchHash] [nvarchar](100) NULL,
	[SignedAt] [datetime2](7) NULL,
	[SignedByUserId] [int] NULL,
	[SignatureData] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[UpdatedByUserId] [int] NULL,
	[EncounterId] [int] NULL,
 CONSTRAINT [PK_ClinicalNotes] PRIMARY KEY CLUSTERED 
(
	[ClinicalNoteId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ClinicalNoteTemplates]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ClinicalNoteTemplates](
	[TemplateId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NULL,
	[LocationId] [int] NULL,
	[Name] [nvarchar](200) NOT NULL,
	[HtmlContent] [nvarchar](max) NOT NULL,
	[IsSystemTemplate] [bit] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[SortOrder] [int] NOT NULL,
	[CreatedByUserId] [int] NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_ClinicalNoteTemplates] PRIMARY KEY CLUSTERED 
(
	[TemplateId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ConsentFormTemplates]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ConsentFormTemplates](
	[ConsentFormTemplateId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NULL,
	[Name] [nvarchar](200) NOT NULL,
	[FormType] [int] NOT NULL,
	[Description] [nvarchar](500) NULL,
	[HtmlContent] [nvarchar](max) NOT NULL,
	[DisplayOrder] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[Version] [int] NOT NULL,
	[IsDeleted] [bit] NOT NULL,
	[CreatedByUserId] [int] NOT NULL,
	[UpdatedByUserId] [int] NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_ConsentFormTemplates] PRIMARY KEY CLUSTERED 
(
	[ConsentFormTemplateId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Consents]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Consents](
	[ConsentId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[Type] [int] NOT NULL,
	[DocumentUrl] [nvarchar](500) NULL,
	[SignedBy] [nvarchar](200) NOT NULL,
	[SignedAt] [datetime2](7) NOT NULL,
	[IpAddress] [nvarchar](50) NULL,
	[SignatureData] [nvarchar](max) NULL,
	[Version] [nvarchar](20) NULL,
	[IsActive] [bit] NULL,
	[ExpiresAt] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK__Consents__374AB08600104622] PRIMARY KEY CLUSTERED 
(
	[ConsentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Conversations]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Conversations](
	[ConversationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[User1Id] [int] NOT NULL,
	[User2Id] [int] NOT NULL,
	[LastMessageText] [nvarchar](500) NULL,
	[LastMessageAt] [datetime2](7) NULL,
	[LastMessageSenderId] [int] NULL,
	[User1UnreadCount] [int] NOT NULL,
	[User2UnreadCount] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_Conversations] PRIMARY KEY CLUSTERED 
(
	[ConversationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[CopayPaymentTokens]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CopayPaymentTokens](
	[TokenId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[Token] [nvarchar](100) NOT NULL,
	[ExpiresAt] [datetime] NOT NULL,
	[IsUsed] [bit] NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[TokenId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[CPTCodes]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CPTCodes](
	[Code] [nvarchar](10) NOT NULL,
	[Description] [nvarchar](500) NOT NULL,
	[Category] [nvarchar](50) NULL,
	[DefaultMinutes] [int] NULL,
	[IsTimeBased] [bit] NULL,
	[IsActive] [bit] NULL,
 CONSTRAINT [PK__CPTCodes__A25C5AA6F5A3B91D] PRIMARY KEY CLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[CredentialingRecords]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CredentialingRecords](
	[CredentialingId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[Source] [nvarchar](100) NULL,
	[PayerName] [nvarchar](100) NULL,
	[SubmittedAt] [datetime2](7) NULL,
	[Status] [int] NULL,
	[ApprovedAt] [datetime2](7) NULL,
	[ExpiresAt] [datetime2](7) NULL,
	[DocumentUrls] [nvarchar](max) NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK__Credenti__531E675F1CE73C60] PRIMARY KEY CLUSTERED 
(
	[CredentialingId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[DrugDatabases]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DrugDatabases](
	[DrugId] [int] IDENTITY(1,1) NOT NULL,
	[NDCCode] [nvarchar](50) NULL,
	[BrandName] [nvarchar](200) NOT NULL,
	[GenericName] [nvarchar](200) NULL,
	[Strength] [nvarchar](50) NULL,
	[DosageForm] [int] NOT NULL,
	[Route] [int] NOT NULL,
	[DEASchedule] [int] NULL,
	[CommonDirections] [nvarchar](500) NULL,
	[Warnings] [nvarchar](1000) NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[DrugId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Encounters]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Encounters](
	[EncounterId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[AppointmentId] [int] NULL,
	[EncounterDate] [date] NOT NULL,
	[ChiefComplaint] [nvarchar](max) NULL,
	[HistoryOfPresentIllness] [nvarchar](max) NULL,
	[ReviewOfSystems] [nvarchar](max) NULL,
	[PhysicalExam] [nvarchar](max) NULL,
	[Assessment] [nvarchar](max) NULL,
	[Plan] [nvarchar](max) NULL,
	[Status] [int] NOT NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[UpdatedByUserId] [int] NULL,
	[SignedAt] [datetime2](7) NULL,
	[SignedByUserId] [int] NULL,
	[CptSelections] [nvarchar](max) NULL,
	[IcdSelections] [nvarchar](max) NULL,
PRIMARY KEY CLUSTERED 
(
	[EncounterId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ICDCodes]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ICDCodes](
	[Code] [nvarchar](20) NOT NULL,
	[Description] [nvarchar](500) NOT NULL,
	[Category] [nvarchar](50) NULL,
	[IsActive] [bit] NULL,
 CONSTRAINT [PK__ICDCodes__A25C5AA6902FF359] PRIMARY KEY CLUSTERED 
(
	[Code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[InstallmentDetails]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[InstallmentDetails](
	[DetailId] [int] IDENTITY(1,1) NOT NULL,
	[PlanId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[InstallmentNumber] [int] NOT NULL,
	[DueDate] [date] NOT NULL,
	[Amount] [decimal](10, 2) NOT NULL,
	[Status] [int] NOT NULL,
	[RetryCount] [int] NOT NULL,
	[PaymentId] [int] NULL,
	[StripePaymentIntentId] [nvarchar](255) NULL,
	[CreatedAt] [datetime] NOT NULL,
	[PaidAt] [datetime] NULL,
PRIMARY KEY CLUSTERED 
(
	[DetailId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[InstallmentPlanAuditLog]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[InstallmentPlanAuditLog](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[PlanId] [int] NOT NULL,
	[DetailId] [int] NULL,
	[TenantId] [int] NOT NULL,
	[Action] [nvarchar](50) NOT NULL,
	[OldStatus] [int] NULL,
	[NewStatus] [int] NULL,
	[Details] [nvarchar](max) NULL,
	[ActorType] [nvarchar](20) NOT NULL,
	[ActorId] [int] NULL,
	[CreatedAt] [datetime] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[InstallmentPlans]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[InstallmentPlans](
	[PlanId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[TotalAmount] [decimal](10, 2) NOT NULL,
	[NumberOfInstallments] [int] NOT NULL,
	[Status] [int] NOT NULL,
	[StripeCustomerId] [nvarchar](255) NULL,
	[StripePaymentMethodId] [nvarchar](255) NULL,
	[CreatedAt] [datetime] NOT NULL,
	[CreatedBy] [int] NULL,
	[LocationId] [int] NULL,
	[StripeConnectAccountId] [int] NULL,
	[ProcessingLockId] [uniqueidentifier] NULL,
	[ProcessingLockedAt] [datetime] NULL,
PRIMARY KEY CLUSTERED 
(
	[PlanId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Insurances]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Insurances](
	[InsuranceId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[PayerName] [nvarchar](100) NOT NULL,
	[PayerId] [nvarchar](50) NULL,
	[PolicyNumber] [nvarchar](500) NOT NULL,
	[GroupNumber] [nvarchar](500) NULL,
	[SubscriberName] [nvarchar](500) NULL,
	[SubscriberDOB] [date] NULL,
	[SubscriberRelationship] [nvarchar](50) NULL,
	[SubscriberId] [nvarchar](500) NULL,
	[EffectiveFrom] [date] NULL,
	[EffectiveTo] [date] NULL,
	[Type] [int] NULL,
	[IsActive] [bit] NULL,
	[LastVerifiedAt] [datetime2](7) NULL,
	[EligibilityStatus] [int] NULL,
	[Copay] [decimal](10, 2) NULL,
	[Coinsurance] [decimal](10, 2) NULL,
	[DeductibleTotal] [decimal](10, 2) NULL,
	[DeductibleMet] [decimal](10, 2) NULL,
	[AllowedVisits] [int] NULL,
	[CoverageNotes] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[Deductible] [numeric](10, 2) NULL,
	[InsuranceCategory] [int] NULL,
	[AttorneyName] [nvarchar](max) NULL,
	[AttorneyPhone] [nvarchar](max) NULL,
	[AttorneyEmail] [nvarchar](max) NULL,
	[SubscriberFirstName] [nvarchar](100) NULL,
	[SubscriberLastName] [nvarchar](100) NULL,
	[PlanName] [nvarchar](max) NULL,
	[InNetwork] [bit] NULL,
	[OutOfPocketMax] [decimal](18, 2) NULL,
	[LastEligibilityResponseJson] [nvarchar](max) NULL,
 CONSTRAINT [PK__Insuranc__74231A243A87D557] PRIMARY KEY CLUSTERED 
(
	[InsuranceId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[IntakeVerificationAttempts]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[IntakeVerificationAttempts](
	[IntakeVerificationAttemptId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[PatientId] [int] NULL,
	[AttemptedDob] [date] NULL,
	[AttemptedSsnLast4Hash] [nvarchar](128) NULL,
	[IsSuccessful] [bit] NOT NULL,
	[IpAddress] [nvarchar](45) NULL,
	[UserAgent] [nvarchar](500) NULL,
	[AttemptedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_IntakeVerificationAttempts] PRIMARY KEY CLUSTERED 
(
	[IntakeVerificationAttemptId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[KioskSessions]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[KioskSessions](
	[KioskSessionId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[SessionToken] [nvarchar](64) NOT NULL,
	[PatientId] [int] NOT NULL,
	[AppointmentId] [int] NOT NULL,
	[CareEpisodeId] [int] NULL,
	[IpAddress] [nvarchar](50) NULL,
	[UserAgent] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[ExpiresAt] [datetime2](7) NOT NULL,
	[LastActivityAt] [datetime2](7) NOT NULL,
	[CurrentFormIndex] [int] NOT NULL,
	[FormsProgressJson] [nvarchar](max) NULL,
	[IsCompleted] [bit] NOT NULL,
	[IsInvalidated] [bit] NOT NULL,
 CONSTRAINT [PK_KioskSessions] PRIMARY KEY CLUSTERED 
(
	[KioskSessionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[KioskVerificationAttempts]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[KioskVerificationAttempts](
	[KioskVerificationAttemptId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[IpAddress] [nvarchar](50) NOT NULL,
	[IsSuccessful] [bit] NOT NULL,
	[AttemptedSsnLast4Hash] [nvarchar](100) NULL,
	[AttemptedDob] [date] NULL,
	[AttemptedZip] [nvarchar](20) NULL,
	[PatientId] [int] NULL,
	[UserAgent] [nvarchar](500) NULL,
	[AttemptedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_KioskVerificationAttempts] PRIMARY KEY CLUSTERED 
(
	[KioskVerificationAttemptId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[LabTestCatalogs]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[LabTestCatalogs](
	[LabTestId] [int] IDENTITY(1,1) NOT NULL,
	[PanelName] [nvarchar](100) NULL,
	[TestName] [nvarchar](200) NOT NULL,
	[TestCode] [nvarchar](20) NULL,
	[Unit] [nvarchar](50) NULL,
	[ReferenceRange] [nvarchar](100) NULL,
	[SpecimenType] [nvarchar](100) NULL,
	[DisplayOrder] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[LabTestId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[LocationKioskSettings]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[LocationKioskSettings](
	[LocationKioskSettingsId] [int] IDENTITY(1,1) NOT NULL,
	[LocationId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[KioskToken] [nvarchar](64) NULL,
	[IsEnabled] [bit] NOT NULL,
	[SessionTimeoutMinutes] [int] NOT NULL,
	[TokenGeneratedAt] [datetime2](7) NOT NULL,
	[TokenGeneratedByUserId] [int] NULL,
	[LastAccessedAt] [datetime2](7) NULL,
	[LastAccessIpAddress] [nvarchar](50) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_LocationKioskSettings] PRIMARY KEY CLUSTERED 
(
	[LocationKioskSettingsId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Locations]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Locations](
	[LocationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[Address] [nvarchar](500) NULL,
	[City] [nvarchar](100) NULL,
	[State] [nvarchar](50) NULL,
	[ZipCode] [nvarchar](20) NULL,
	[Phone] [nvarchar](20) NULL,
	[IsActive] [bit] NULL,
	[IsPrimary] [bit] NULL,
	[TimeZoneId] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[PortalCode] [nvarchar](max) NULL,
	[PlaceOfServiceCode] [nvarchar](10) NULL,
	[FacilityNpi] [nvarchar](20) NULL,
	[StripeConnectAccountId] [int] NULL,
	[OnlineFeePercent] [decimal](5, 2) NULL,
	[OnlineFeeFlatCents] [int] NULL,
	[CardPresentFeePercent] [decimal](5, 2) NULL,
	[CardPresentFeeFlatCents] [int] NULL,
	[EnableLongevity] [bit] NOT NULL,
 CONSTRAINT [PK__Location__E7FEA497837E38AA] PRIMARY KEY CLUSTERED 
(
	[LocationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[MedicalLienTemplates]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[MedicalLienTemplates](
	[TemplateId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[Description] [nvarchar](500) NULL,
	[HtmlContent] [nvarchar](max) NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedByUserId] [int] NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_MedicalLienTemplates] PRIMARY KEY CLUSTERED 
(
	[TemplateId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Messages]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Messages](
	[MessageId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[ConversationId] [int] NOT NULL,
	[SenderId] [int] NOT NULL,
	[RecipientId] [int] NOT NULL,
	[MessageText] [nvarchar](max) NULL,
	[MessageType] [int] NOT NULL,
	[FileUrl] [nvarchar](1000) NULL,
	[FileName] [nvarchar](500) NULL,
	[FileSize] [bigint] NULL,
	[FileMimeType] [nvarchar](100) NULL,
	[FileDurationSeconds] [decimal](10, 2) NULL,
	[IsRead] [bit] NOT NULL,
	[ReadAt] [datetime2](7) NULL,
	[IsDeletedBySender] [bit] NOT NULL,
	[IsDeletedByRecipient] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_Messages] PRIMARY KEY CLUSTERED 
(
	[MessageId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Notes]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Notes](
	[NoteId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[AppointmentId] [int] NULL,
	[CareEpisodeId] [int] NULL,
	[Type] [int] NOT NULL,
	[ServiceDate] [date] NOT NULL,
	[Subjective] [nvarchar](max) NULL,
	[Objective] [nvarchar](max) NULL,
	[Assessment] [nvarchar](max) NULL,
	[Plan] [nvarchar](max) NULL,
	[VitalSigns] [nvarchar](max) NULL,
	[FunctionalTests] [nvarchar](max) NULL,
	[Interventions] [nvarchar](max) NULL,
	[PatientEducation] [nvarchar](max) NULL,
	[HomeExerciseProgram] [nvarchar](max) NULL,
	[CPTCodes] [nvarchar](max) NULL,
	[ICDCodes] [nvarchar](max) NULL,
	[TotalMinutes] [int] NULL,
	[DirectMinutes] [int] NULL,
	[IndirectMinutes] [int] NULL,
	[Status] [int] NULL,
	[SignedAt] [datetime2](7) NULL,
	[SignedBy] [int] NULL,
	[SignatureData] [nvarchar](max) NULL,
	[CoSignedBy] [int] NULL,
	[CoSignedAt] [datetime2](7) NULL,
	[ParentNoteId] [int] NULL,
	[IsAddendum] [bit] NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[CreatedBy] [int] NULL,
 CONSTRAINT [PK__Notes__EACE355FB8248B2C] PRIMARY KEY CLUSTERED 
(
	[NoteId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[OrderResults]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[OrderResults](
	[OrderResultId] [int] IDENTITY(1,1) NOT NULL,
	[OrderId] [int] NOT NULL,
	[TestName] [nvarchar](200) NULL,
	[ResultValue] [nvarchar](100) NULL,
	[ResultUnit] [nvarchar](50) NULL,
	[ReferenceRange] [nvarchar](100) NULL,
	[IsAbnormal] [bit] NULL,
	[FindingsText] [nvarchar](max) NULL,
	[ResultDate] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[OrderResultId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Orders]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Orders](
	[OrderId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[EncounterId] [int] NULL,
	[OrderType] [int] NOT NULL,
	[Status] [int] NOT NULL,
	[Priority] [int] NOT NULL,
	[OrderDate] [date] NOT NULL,
	[DiagnosisCode] [nvarchar](20) NULL,
	[ClinicalIndication] [nvarchar](500) NULL,
	[Notes] [nvarchar](max) NULL,
	[LabPanelName] [nvarchar](100) NULL,
	[FastingRequired] [bit] NULL,
	[SpecimenType] [nvarchar](100) NULL,
	[Modality] [int] NULL,
	[BodyPart] [nvarchar](200) NULL,
	[ContrastRequired] [bit] NULL,
	[ImagingFacility] [nvarchar](200) NULL,
	[ReferralSpecialty] [nvarchar](100) NULL,
	[ReferredToProvider] [nvarchar](200) NULL,
	[ReferredToFacility] [nvarchar](200) NULL,
	[ReferredToPhone] [nvarchar](20) NULL,
	[ReferredToFax] [nvarchar](20) NULL,
	[ReferralReason] [nvarchar](500) NULL,
	[ReferralUrgency] [int] NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[CompletedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[OrderId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientAllergies]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientAllergies](
	[PatientAllergyId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[AllergenName] [nvarchar](max) NULL,
	[Type] [int] NOT NULL,
	[Reaction] [nvarchar](max) NULL,
	[Severity] [int] NOT NULL,
	[OnsetDate] [date] NULL,
	[IsActive] [bit] NOT NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[EncounterId] [int] NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime2](7) NULL,
	[DeletedByUserId] [int] NULL,
	[DeletedReason] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientAllergyId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientConversations]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientConversations](
	[PatientConversationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[UserId] [int] NOT NULL,
	[LastMessageText] [nvarchar](500) NULL,
	[LastMessageAt] [datetime2](7) NULL,
	[LastMessageSenderType] [nvarchar](20) NULL,
	[PatientUnreadCount] [int] NOT NULL,
	[UserUnreadCount] [int] NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_PatientConversations] PRIMARY KEY CLUSTERED 
(
	[PatientConversationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientDocuments]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientDocuments](
	[DocumentId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[FileName] [nvarchar](500) NOT NULL,
	[StorageFileName] [nvarchar](100) NOT NULL,
	[ContentType] [nvarchar](100) NOT NULL,
	[FileSize] [bigint] NOT NULL,
	[Category] [int] NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[IsEncrypted] [bit] NOT NULL,
	[FileHash] [nvarchar](100) NULL,
	[UploadedByUserId] [int] NULL,
	[IsDeleted] [bit] NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsPatientUploaded] [bit] NOT NULL,
 CONSTRAINT [PK_PatientDocuments] PRIMARY KEY CLUSTERED 
(
	[DocumentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientFamilyHistories]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientFamilyHistories](
	[PatientFamilyHistoryId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[Relation] [nvarchar](50) NULL,
	[Condition] [nvarchar](max) NULL,
	[AgeAtOnset] [int] NULL,
	[IsDeceased] [bit] NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[EncounterId] [int] NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime2](7) NULL,
	[DeletedByUserId] [int] NULL,
	[DeletedReason] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientFamilyHistoryId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientGenderHealths]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientGenderHealths](
	[PatientGenderHealthsId] [int] IDENTITY(1,1) NOT NULL,
	[PatientId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[BiologicalSex] [nvarchar](20) NULL,
	[StructuredData] [nvarchar](max) NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientGenderHealthsId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientHealthConcerns]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientHealthConcerns](
	[PatientHealthConcernId] [int] IDENTITY(1,1) NOT NULL,
	[PatientId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[Priority] [int] NOT NULL,
	[Concern] [nvarchar](200) NOT NULL,
	[Details] [nvarchar](2000) NULL,
	[Severity] [int] NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NOT NULL,
 CONSTRAINT [PK_PatientHealthConcerns] PRIMARY KEY CLUSTERED 
(
	[PatientHealthConcernId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientImmunizations]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientImmunizations](
	[PatientImmunizationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[VaccineName] [nvarchar](max) NULL,
	[CvxCode] [nvarchar](10) NULL,
	[AdministeredDate] [date] NOT NULL,
	[LotNumber] [nvarchar](50) NULL,
	[Manufacturer] [nvarchar](max) NULL,
	[Site] [nvarchar](max) NULL,
	[AdministeredByProviderId] [int] NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[EncounterId] [int] NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime2](7) NULL,
	[DeletedByUserId] [int] NULL,
	[DeletedReason] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientImmunizationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientIntakeSubmissions]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientIntakeSubmissions](
	[PatientIntakeSubmissionId] [int] IDENTITY(1,1) NOT NULL,
	[PatientId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[StartedAt] [datetime2](7) NOT NULL,
	[SubmittedAt] [datetime2](7) NULL,
	[SectionsTouched] [nvarchar](500) NULL,
	[SourceChannel] [int] NOT NULL,
	[IpAddress] [nvarchar](45) NULL,
	[UserAgent] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NOT NULL,
	[LastFeltWell] [nvarchar](1000) NULL,
	[WhatTriggered] [nvarchar](1000) NULL,
	[BetterFactors] [nvarchar](1000) NULL,
	[WorseFactors] [nvarchar](1000) NULL,
	[AdditionalTimeline] [nvarchar](max) NULL,
 CONSTRAINT [PK_PatientIntakeSubmissions] PRIMARY KEY CLUSTERED 
(
	[PatientIntakeSubmissionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientLedgers]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientLedgers](
	[LedgerId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[EntryType] [int] NOT NULL,
	[ChargeId] [int] NULL,
	[PaymentId] [int] NULL,
	[ClaimId] [int] NULL,
	[TransactionDate] [date] NOT NULL,
	[Amount] [decimal](10, 2) NOT NULL,
	[Description] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK__PatientL__AE70E0CF5E762058] PRIMARY KEY CLUSTERED 
(
	[LedgerId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientLongevityProfiles]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientLongevityProfiles](
	[PatientLongevityProfileId] [int] IDENTITY(1,1) NOT NULL,
	[PatientId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[SymptomRatings] [nvarchar](max) NULL,
	[Goals] [nvarchar](max) NULL,
	[PriorTesting] [nvarchar](max) NULL,
	[CurrentInterventions] [nvarchar](max) NULL,
	[ToxinExposure] [nvarchar](max) NULL,
	[BiomarkerGoals] [nvarchar](1000) NULL,
	[OptimalHealthVision] [nvarchar](1000) NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NOT NULL,
 CONSTRAINT [PK_PatientLongevityProfiles] PRIMARY KEY CLUSTERED 
(
	[PatientLongevityProfileId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UX_PatientLongevityProfiles_PatientId] UNIQUE NONCLUSTERED 
(
	[PatientId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientMedications]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientMedications](
	[PatientMedicationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[DrugName] [nvarchar](max) NULL,
	[Dosage] [nvarchar](max) NULL,
	[Form] [nvarchar](max) NULL,
	[Route] [nvarchar](max) NULL,
	[Frequency] [nvarchar](max) NULL,
	[Status] [int] NOT NULL,
	[StartDate] [date] NULL,
	[EndDate] [date] NULL,
	[PrescribedByProviderId] [int] NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[EncounterId] [int] NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime2](7) NULL,
	[DeletedByUserId] [int] NULL,
	[DeletedReason] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientMedicationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientMessages]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientMessages](
	[PatientMessageId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientConversationId] [int] NOT NULL,
	[SenderType] [nvarchar](20) NOT NULL,
	[SenderPatientId] [int] NULL,
	[SenderUserId] [int] NULL,
	[MessageText] [nvarchar](max) NOT NULL,
	[IsReadByPatient] [bit] NOT NULL,
	[IsReadByUser] [bit] NOT NULL,
	[ReadByPatientAt] [datetime2](7) NULL,
	[ReadByUserAt] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_PatientMessages] PRIMARY KEY CLUSTERED 
(
	[PatientMessageId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientPortalAccounts]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientPortalAccounts](
	[PatientPortalAccountId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[PasswordHash] [nvarchar](500) NOT NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[RegisteredAt] [datetime2](7) NULL,
	[LastLoginAt] [datetime2](7) NULL,
	[FailedLoginAttempts] [int] NOT NULL,
	[LockoutEndAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientPortalAccountId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientPortalInvitations]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientPortalInvitations](
	[PatientPortalInvitationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[Email] [nvarchar](256) NOT NULL,
	[TokenHash] [nvarchar](500) NOT NULL,
	[Status] [int] NOT NULL,
	[InvitedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[ExpiresAt] [datetime2](7) NOT NULL,
	[UsedAt] [datetime2](7) NULL,
	[ResentCount] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientPortalInvitationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientPortalOtps]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientPortalOtps](
	[PatientPortalOtpId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[CodeHash] [nvarchar](500) NOT NULL,
	[ExpiresAt] [datetime2](7) NOT NULL,
	[AttemptCount] [int] NOT NULL,
	[UsedAt] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientPortalOtpId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientPortalPasswordResets]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientPortalPasswordResets](
	[PatientPortalPasswordResetId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[TokenHash] [nvarchar](500) NOT NULL,
	[ExpiresAt] [datetime2](7) NOT NULL,
	[UsedAt] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientPortalPasswordResetId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientProblems]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientProblems](
	[PatientProblemId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[IcdCode] [nvarchar](20) NULL,
	[Description] [nvarchar](max) NULL,
	[Status] [int] NOT NULL,
	[OnsetDate] [date] NULL,
	[ResolvedDate] [date] NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[EncounterId] [int] NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime2](7) NULL,
	[DeletedByUserId] [int] NULL,
	[DeletedReason] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientProblemId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Patients]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Patients](
	[PatientId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[MRN] [nvarchar](20) NOT NULL,
	[FirstName] [nvarchar](500) NOT NULL,
	[LastName] [nvarchar](500) NOT NULL,
	[DateOfBirth] [date] NOT NULL,
	[Gender] [nvarchar](20) NULL,
	[Phone] [nvarchar](500) NULL,
	[Email] [nvarchar](500) NULL,
	[Address] [nvarchar](max) NULL,
	[City] [nvarchar](500) NULL,
	[State] [nvarchar](500) NULL,
	[ZipCode] [nvarchar](500) NULL,
	[EmergencyContactName] [nvarchar](500) NULL,
	[EmergencyContactPhone] [nvarchar](500) NULL,
	[EmergencyContactAltPhone] [nvarchar](max) NULL,
	[EmergencyContactRelation] [nvarchar](500) NULL,
	[PreferredProviderId] [int] NULL,
	[PreferredLocationId] [int] NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NULL,
	[IsArchived] [bit] NOT NULL,
	[SsnEncrypted] [nvarchar](max) NULL,
	[SsnLast4Hash] [nvarchar](450) NULL,
	[DateOfInjury] [date] NULL,
	[ProfilePicturePath] [nvarchar](500) NULL,
	[StripeCustomerId] [nvarchar](255) NULL,
	[IntakePortalToken] [uniqueidentifier] NULL,
	[DrugReactionHistory] [nvarchar](max) NULL,
	[PrimaryPharmacyInfo] [nvarchar](500) NULL,
	[CompoundingPharmacyInfo] [nvarchar](500) NULL,
	[HealthcareTeamNotes] [nvarchar](max) NULL,
 CONSTRAINT [PK__Patients__970EC366F88B725F] PRIMARY KEY CLUSTERED 
(
	[PatientId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientSearchTokens]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientSearchTokens](
	[PatientSearchTokenId] [int] IDENTITY(1,1) NOT NULL,
	[PatientId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[LocationId] [int] NULL,
	[TokenHash] [nvarchar](64) NOT NULL,
	[FieldType] [nvarchar](20) NOT NULL,
	[PrefixLength] [int] NOT NULL,
 CONSTRAINT [PK_PatientSearchTokens] PRIMARY KEY CLUSTERED 
(
	[PatientSearchTokenId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientSocialHistories]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientSocialHistories](
	[PatientSocialHistoryId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[Category] [nvarchar](50) NULL,
	[Description] [nvarchar](max) NULL,
	[Status] [nvarchar](20) NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[EncounterId] [int] NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime2](7) NULL,
	[DeletedByUserId] [int] NULL,
	[DeletedReason] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientSocialHistoryId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientStickyNotes]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientStickyNotes](
	[PatientStickyNoteId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[Content] [nvarchar](1000) NOT NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedByName] [nvarchar](200) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NOT NULL,
 CONSTRAINT [PK_PatientStickyNotes] PRIMARY KEY CLUSTERED 
(
	[PatientStickyNoteId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientSupplements]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientSupplements](
	[PatientSupplementId] [int] IDENTITY(1,1) NOT NULL,
	[PatientId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[SupplementName] [nvarchar](200) NOT NULL,
	[Notes] [nvarchar](1000) NULL,
	[IsActive] [bit] NOT NULL,
	[Source] [int] NOT NULL,
	[IntakeSubmissionId] [int] NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NOT NULL,
	[DeletedAt] [datetime2](7) NULL,
	[DeletedByUserId] [int] NULL,
	[DeletedReason] [nvarchar](500) NULL,
 CONSTRAINT [PK_PatientSupplements] PRIMARY KEY CLUSTERED 
(
	[PatientSupplementId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientValidations]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientValidations](
	[PatientValidationId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[IsComplete] [bit] NOT NULL,
	[MissingFieldsCount] [int] NOT NULL,
	[MissingSections] [nvarchar](1000) NULL,
	[MissingFieldsDetails] [nvarchar](4000) NULL,
	[FirstIncompleteSection] [nvarchar](50) NULL,
	[LastValidatedAt] [datetime2](7) NULL,
	[ValidatedByUserId] [int] NULL,
	[ValidationSource] [nvarchar](20) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK__PatientValidations] PRIMARY KEY CLUSTERED 
(
	[PatientValidationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PatientVitals]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PatientVitals](
	[PatientVitalId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[EncounterId] [int] NULL,
	[RecordedAt] [datetime2](7) NOT NULL,
	[RecordedByUserId] [int] NOT NULL,
	[SystolicBp] [int] NULL,
	[DiastolicBp] [int] NULL,
	[HeartRate] [int] NULL,
	[RespiratoryRate] [int] NULL,
	[Temperature] [decimal](5, 1) NULL,
	[SpO2] [decimal](5, 1) NULL,
	[Weight] [decimal](6, 1) NULL,
	[Height] [decimal](5, 1) NULL,
	[Bmi] [decimal](5, 1) NULL,
	[Notes] [nvarchar](max) NULL,
PRIMARY KEY CLUSTERED 
(
	[PatientVitalId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Payers]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Payers](
	[PayerId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[PayerIdCode] [nvarchar](50) NULL,
	[Address] [nvarchar](500) NULL,
	[Phone] [nvarchar](20) NULL,
	[Website] [nvarchar](100) NULL,
	[EligibilityUrl] [nvarchar](100) NULL,
	[ClaimsUrl] [nvarchar](100) NULL,
	[IsActive] [bit] NULL,
 CONSTRAINT [PK__Payers__0ADBE8677686BB41] PRIMARY KEY CLUSTERED 
(
	[PayerId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PaymentRefunds]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PaymentRefunds](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[PaymentId] [int] NOT NULL,
	[TenantId] [int] NOT NULL,
	[StripeRefundId] [nvarchar](255) NOT NULL,
	[StripeChargeId] [nvarchar](255) NULL,
	[AmountCents] [int] NOT NULL,
	[Reason] [nvarchar](100) NULL,
	[Status] [int] NOT NULL,
	[RefundedAt] [datetime] NOT NULL,
	[CreatedByExternal] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_PaymentRefunds_StripeRefundId] UNIQUE NONCLUSTERED 
(
	[StripeRefundId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Payments]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Payments](
	[PaymentId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[AppointmentId] [int] NULL,
	[ClaimId] [int] NULL,
	[Type] [int] NOT NULL,
	[Method] [int] NOT NULL,
	[Amount] [decimal](10, 2) NOT NULL,
	[TransactionId] [nvarchar](100) NULL,
	[CheckNumber] [nvarchar](50) NULL,
	[PayerName] [nvarchar](100) NULL,
	[Status] [int] NULL,
	[PaymentDate] [date] NOT NULL,
	[Notes] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[CreatedBy] [int] NULL,
	[IsRefund] [bit] NULL,
	[RefundOfPaymentId] [int] NULL,
	[StripePaymentIntentId] [nvarchar](255) NULL,
	[InstallmentDetailId] [int] NULL,
	[CheckDate] [date] NULL,
	[CardReference] [nvarchar](100) NULL,
	[ReferenceNumber] [nvarchar](100) NULL,
	[LocationId] [int] NULL,
	[StripeConnectAccountId] [int] NULL,
	[PaymentMethodType] [int] NULL,
	[ClinicTotalFeeCents] [int] NULL,
	[StripeProcessingFeeCents] [int] NULL,
	[ApplicationFeeCents] [int] NULL,
	[NetToClinicCents] [int] NULL,
	[StripeChargeId] [nvarchar](255) NULL,
	[HasOpenDispute] [bit] NOT NULL,
 CONSTRAINT [PK__Payments__9B556A3827093A8E] PRIMARY KEY CLUSTERED 
(
	[PaymentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Pharmacies]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Pharmacies](
	[PharmacyId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NULL,
	[Name] [nvarchar](200) NOT NULL,
	[NCPDP] [nvarchar](20) NULL,
	[NPI] [nvarchar](20) NULL,
	[Address] [nvarchar](200) NULL,
	[City] [nvarchar](100) NULL,
	[State] [nvarchar](2) NULL,
	[Zip] [nvarchar](10) NULL,
	[Phone] [nvarchar](20) NULL,
	[Fax] [nvarchar](20) NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[PharmacyId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Prescriptions]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Prescriptions](
	[PrescriptionId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[EncounterId] [int] NULL,
	[DrugName] [nvarchar](200) NOT NULL,
	[GenericName] [nvarchar](200) NULL,
	[NDCCode] [nvarchar](50) NULL,
	[RxNormCode] [nvarchar](50) NULL,
	[Strength] [nvarchar](50) NULL,
	[DosageForm] [int] NOT NULL,
	[Quantity] [decimal](10, 2) NOT NULL,
	[DaysSupply] [int] NOT NULL,
	[DoseAmount] [nvarchar](20) NULL,
	[DoseUnit] [nvarchar](50) NULL,
	[Route] [int] NOT NULL,
	[Frequency] [int] NOT NULL,
	[DirectionsFreeText] [nvarchar](500) NULL,
	[Refills] [int] NOT NULL,
	[DAW] [bit] NOT NULL,
	[PharmacyName] [nvarchar](200) NULL,
	[PharmacyPhone] [nvarchar](20) NULL,
	[PharmacyAddress] [nvarchar](500) NULL,
	[Status] [int] NOT NULL,
	[IsControlledSubstance] [bit] NOT NULL,
	[DEASchedule] [int] NULL,
	[DiagnosisCode] [nvarchar](20) NULL,
	[PrescribedDate] [date] NOT NULL,
	[ExpirationDate] [date] NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[PrescriptionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ProviderFavoriteCodes]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ProviderFavoriteCodes](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[UserId] [int] NOT NULL,
	[CodeType] [nvarchar](10) NOT NULL,
	[Code] [nvarchar](20) NOT NULL,
	[Description] [nvarchar](500) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
 CONSTRAINT [PK_ProviderFavoriteCodes] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Providers]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Providers](
	[ProviderId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[NPI] [nvarchar](10) NOT NULL,
	[FirstName] [nvarchar](100) NOT NULL,
	[LastName] [nvarchar](100) NOT NULL,
	[Credentials] [nvarchar](50) NULL,
	[Specialty] [nvarchar](100) NULL,
	[Taxonomy] [nvarchar](20) NULL,
	[Email] [nvarchar](500) NULL,
	[Phone] [nvarchar](500) NULL,
	[Color] [nvarchar](7) NULL,
	[CredentialStatus] [int] NULL,
	[CredentialExpiry] [datetime2](7) NULL,
	[LicenseExpiry] [datetime2](7) NULL,
	[LicenseNumber] [nvarchar](50) NULL,
	[LicenseState] [nvarchar](50) NULL,
	[IsActive] [bit] NULL,
	[DefaultAppointmentDuration] [int] NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[SignatureImagePath] [nvarchar](max) NULL,
	[ShowResumePopup] [bit] NOT NULL,
	[ProfilePicturePath] [nvarchar](500) NULL,
 CONSTRAINT [PK__Provider__B54C687DCE30F6EF] PRIMARY KEY CLUSTERED 
(
	[ProviderId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ProviderSchedules]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ProviderSchedules](
	[ScheduleId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[LocationId] [int] NULL,
	[DayOfWeek] [int] NOT NULL,
	[StartTime] [time](7) NOT NULL,
	[EndTime] [time](7) NOT NULL,
	[IsAvailable] [bit] NULL,
 CONSTRAINT [PK__Provider__9C8A5B49F3345BB5] PRIMARY KEY CLUSTERED 
(
	[ScheduleId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[RecordingSessions]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[RecordingSessions](
	[SessionId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[AppointmentId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[RecordingStatus] [nvarchar](50) NOT NULL,
	[StartTime] [datetime2](7) NULL,
	[EndTime] [datetime2](7) NULL,
	[TotalDurationSeconds] [int] NULL,
	[CompleteTranscription] [nvarchar](max) NULL,
	[MergedAudioFileName] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[CreatedByUserId] [int] NULL,
	[UpdatedByUserId] [int] NULL,
 CONSTRAINT [PK_RecordingSessions] PRIMARY KEY CLUSTERED 
(
	[SessionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[StripeConnectAccounts]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[StripeConnectAccounts](
	[StripeConnectAccountId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[StripeAccountId] [nvarchar](255) NOT NULL,
	[DisplayName] [nvarchar](255) NOT NULL,
	[BusinessEmail] [nvarchar](255) NULL,
	[Country] [nvarchar](2) NOT NULL,
	[DefaultCurrency] [nvarchar](3) NOT NULL,
	[Status] [int] NOT NULL,
	[ChargesEnabled] [bit] NOT NULL,
	[PayoutsEnabled] [bit] NOT NULL,
	[DetailsSubmitted] [bit] NOT NULL,
	[ConnectedAt] [datetime] NOT NULL,
	[ConnectedByUserId] [int] NULL,
	[DisconnectedAt] [datetime] NULL,
	[LastWebhookAt] [datetime] NULL,
	[CreatedAt] [datetime] NOT NULL,
	[UpdatedAt] [datetime] NULL,
PRIMARY KEY CLUSTERED 
(
	[StripeConnectAccountId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_StripeConnectAccounts_StripeAccountId] UNIQUE NONCLUSTERED 
(
	[StripeAccountId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[StripeWebhookEvents]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[StripeWebhookEvents](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[StripeEventId] [nvarchar](255) NOT NULL,
	[EventType] [nvarchar](100) NOT NULL,
	[StripeAccountId] [nvarchar](255) NULL,
	[Payload] [nvarchar](max) NOT NULL,
	[Status] [int] NOT NULL,
	[ErrorMessage] [nvarchar](max) NULL,
	[ReceivedAt] [datetime] NOT NULL,
	[ProcessedAt] [datetime] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_StripeWebhookEvents_StripeEventId] UNIQUE NONCLUSTERED 
(
	[StripeEventId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[SystemSettings]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[SystemSettings](
	[SystemSettingId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[SettingKey] [nvarchar](100) NOT NULL,
	[SettingValue] [nvarchar](1000) NOT NULL,
	[DataType] [nvarchar](50) NOT NULL,
	[Description] [nvarchar](500) NULL,
	[Category] [nvarchar](100) NULL,
	[DefaultValue] [nvarchar](1000) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK__SystemSettings] PRIMARY KEY CLUSTERED 
(
	[SystemSettingId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TelehealthTranscriptionChunks]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TelehealthTranscriptionChunks](
	[ChunkId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[EncounterId] [int] NOT NULL,
	[SequenceNumber] [int] NOT NULL,
	[TranscriptionText] [nvarchar](max) NOT NULL,
	[DurationSeconds] [decimal](10, 2) NULL,
	[CreatedAt] [datetime] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[ChunkId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Tenants]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Tenants](
	[TenantId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[Subdomain] [nvarchar](50) NOT NULL,
	[LogoUrl] [nvarchar](500) NULL,
	[Phone] [nvarchar](20) NULL,
	[Email] [nvarchar](100) NULL,
	[Address] [nvarchar](500) NULL,
	[City] [nvarchar](100) NULL,
	[State] [nvarchar](50) NULL,
	[ZipCode] [nvarchar](20) NULL,
	[TaxId] [nvarchar](20) NULL,
	[NPI] [nvarchar](10) NULL,
	[Plan] [int] NULL,
	[Status] [int] NULL,
	[SubscriptionStartDate] [datetime2](7) NULL,
	[SubscriptionEndDate] [datetime2](7) NULL,
	[MaxUsers] [int] NULL,
	[MaxPatients] [int] NULL,
	[Settings] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[IsDeleted] [bit] NULL,
 CONSTRAINT [PK__Tenants__2E9B47E1790FFCEE] PRIMARY KEY CLUSTERED 
(
	[TenantId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TherapistUnavailabilities]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TherapistUnavailabilities](
	[UnavailabilityId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[StartDate] [date] NOT NULL,
	[EndDate] [date] NOT NULL,
	[Type] [int] NOT NULL,
	[Reason] [nvarchar](500) NULL,
	[IsFullDay] [bit] NOT NULL,
	[StartTime] [time](7) NULL,
	[EndTime] [time](7) NULL,
	[IsApproved] [bit] NOT NULL,
	[ApprovedByUserId] [int] NULL,
	[ApprovedAt] [datetime2](7) NULL,
	[RecurrencePattern] [nvarchar](100) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_TherapistUnavailabilities] PRIMARY KEY CLUSTERED 
(
	[UnavailabilityId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TranscriptionChunks]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TranscriptionChunks](
	[ChunkId] [int] IDENTITY(1,1) NOT NULL,
	[SessionId] [int] NOT NULL,
	[SequenceNumber] [int] NOT NULL,
	[ChunkAudioFileName] [nvarchar](500) NULL,
	[ChunkDurationSeconds] [decimal](10, 2) NULL,
	[RecordedTimestamp] [datetime2](7) NOT NULL,
	[TranscriptionText] [nvarchar](max) NULL,
	[TranscriptionStatus] [nvarchar](50) NOT NULL,
	[TranscriptionCompletedAt] [datetime2](7) NULL,
	[TranscriptionError] [nvarchar](1000) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
 CONSTRAINT [PK_TranscriptionChunks] PRIMARY KEY CLUSTERED 
(
	[ChunkId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TreatmentPlans]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TreatmentPlans](
	[TreatmentPlanId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[PatientId] [int] NOT NULL,
	[ProviderId] [int] NOT NULL,
	[ConditionName] [nvarchar](max) NULL,
	[IcdCode] [nvarchar](20) NULL,
	[Goals] [nvarchar](max) NULL,
	[FollowUpIntervalDays] [int] NULL,
	[NextFollowUp] [date] NULL,
	[Status] [int] NOT NULL,
	[Notes] [nvarchar](max) NULL,
	[CreatedByUserId] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[TreatmentPlanId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TrustedDevices]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TrustedDevices](
	[TrustedDeviceId] [int] IDENTITY(1,1) NOT NULL,
	[UserId] [int] NOT NULL,
	[DeviceTokenHash] [nvarchar](128) NOT NULL,
	[ExpiresAt] [datetime2](7) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UserAgent] [nvarchar](500) NULL,
 CONSTRAINT [PK_TrustedDevices] PRIMARY KEY CLUSTERED 
(
	[TrustedDeviceId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[UserPresence]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[UserPresence](
	[UserPresenceId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NOT NULL,
	[UserId] [int] NOT NULL,
	[Status] [int] NOT NULL,
	[LastActiveAt] [datetime2](7) NOT NULL,
	[ConnectionId] [nvarchar](100) NULL,
 CONSTRAINT [PK_UserPresence] PRIMARY KEY CLUSTERED 
(
	[UserPresenceId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[Users]    Script Date: 4/27/2026 2:54:04 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Users](
	[UserId] [int] IDENTITY(1,1) NOT NULL,
	[TenantId] [int] NULL,
	[Email] [nvarchar](500) NOT NULL,
	[PasswordHash] [nvarchar](max) NOT NULL,
	[FirstName] [nvarchar](100) NOT NULL,
	[LastName] [nvarchar](100) NOT NULL,
	[Phone] [nvarchar](500) NULL,
	[Role] [int] NULL,
	[ProviderId] [int] NULL,
	[IsActive] [bit] NULL,
	[LastLoginAt] [datetime2](7) NULL,
	[RefreshToken] [nvarchar](max) NULL,
	[RefreshTokenExpiry] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NULL,
	[UpdatedAt] [datetime2](7) NULL,
	[PasswordResetToken] [nvarchar](255) NULL,
	[PasswordResetTokenExpiry] [datetime2](7) NULL,
	[OtpCode] [nvarchar](10) NULL,
	[OtpExpiry] [datetime2](7) NULL,
	[OtpAttempts] [int] NOT NULL,
	[OtpResendCooldownUntil] [datetime2](7) NULL,
 CONSTRAINT [PK__Users__1788CC4C65BDD67C] PRIMARY KEY CLUSTERED 
(
	[UserId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
ALTER TABLE [dbo].[Appointments] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[Appointments] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsRecurring]
GO
ALTER TABLE [dbo].[Appointments] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsTelehealth]
GO
ALTER TABLE [dbo].[Appointments] ADD  DEFAULT (CONVERT([bit],(0))) FOR [InsuranceVerified]
GO
ALTER TABLE [dbo].[Appointments] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[AuditLogs] ADD  DEFAULT (getutcdate()) FOR [Timestamp]
GO
ALTER TABLE [dbo].[Authorizations] ADD  DEFAULT ((0)) FOR [AuthorizedVisits]
GO
ALTER TABLE [dbo].[Authorizations] ADD  DEFAULT (getutcdate()) FOR [DateOfValidation]
GO
ALTER TABLE [dbo].[Authorizations] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[BillingClaims] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[BillingClaims] ADD  DEFAULT ((0)) FOR [Type]
GO
ALTER TABLE [dbo].[BillingClaims] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[CareEpisodeConsentForms] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[CareEpisodeConsents] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[CareEpisodes] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[CareEpisodes] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Charges] ADD  DEFAULT ((1)) FOR [Units]
GO
ALTER TABLE [dbo].[Charges] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[Charges] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ClaimStatusHistories] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ClinicalNoteAddendums] ADD  CONSTRAINT [DF_ClinicalNoteAddendums_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ClinicalNoteAmendments] ADD  CONSTRAINT [DF_ClinicalNoteAmendments_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ClinicalNotes] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ClinicalNoteTemplates] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[ClinicalNoteTemplates] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ConsentFormTemplates] ADD  DEFAULT ((0)) FOR [DisplayOrder]
GO
ALTER TABLE [dbo].[ConsentFormTemplates] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[ConsentFormTemplates] ADD  DEFAULT ((1)) FOR [Version]
GO
ALTER TABLE [dbo].[ConsentFormTemplates] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[ConsentFormTemplates] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Consents] ADD  DEFAULT (N'1.0') FOR [Version]
GO
ALTER TABLE [dbo].[Consents] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[Consents] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Conversations] ADD  DEFAULT ((0)) FOR [User1UnreadCount]
GO
ALTER TABLE [dbo].[Conversations] ADD  DEFAULT ((0)) FOR [User2UnreadCount]
GO
ALTER TABLE [dbo].[Conversations] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[CopayPaymentTokens] ADD  DEFAULT ((0)) FOR [IsUsed]
GO
ALTER TABLE [dbo].[CopayPaymentTokens] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[CPTCodes] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsTimeBased]
GO
ALTER TABLE [dbo].[CPTCodes] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[CredentialingRecords] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[CredentialingRecords] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[DrugDatabases] ADD  DEFAULT ((0)) FOR [DosageForm]
GO
ALTER TABLE [dbo].[DrugDatabases] ADD  DEFAULT ((0)) FOR [Route]
GO
ALTER TABLE [dbo].[DrugDatabases] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[DrugDatabases] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Encounters] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[Encounters] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ICDCodes] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[InstallmentDetails] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[InstallmentDetails] ADD  DEFAULT ((0)) FOR [RetryCount]
GO
ALTER TABLE [dbo].[InstallmentDetails] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[InstallmentPlanAuditLog] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[InstallmentPlans] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[InstallmentPlans] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Insurances] ADD  DEFAULT ((0)) FOR [Type]
GO
ALTER TABLE [dbo].[Insurances] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[Insurances] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts] ADD  CONSTRAINT [DF_IntakeVerificationAttempts_IsSuccessful]  DEFAULT ((0)) FOR [IsSuccessful]
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts] ADD  CONSTRAINT [DF_IntakeVerificationAttempts_AttemptedAt]  DEFAULT (getutcdate()) FOR [AttemptedAt]
GO
ALTER TABLE [dbo].[KioskSessions] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[KioskSessions] ADD  DEFAULT ((0)) FOR [CurrentFormIndex]
GO
ALTER TABLE [dbo].[KioskSessions] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsCompleted]
GO
ALTER TABLE [dbo].[KioskSessions] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsInvalidated]
GO
ALTER TABLE [dbo].[KioskVerificationAttempts] ADD  DEFAULT (getutcdate()) FOR [AttemptedAt]
GO
ALTER TABLE [dbo].[LabTestCatalogs] ADD  DEFAULT ((0)) FOR [DisplayOrder]
GO
ALTER TABLE [dbo].[LabTestCatalogs] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[LabTestCatalogs] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[LocationKioskSettings] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsEnabled]
GO
ALTER TABLE [dbo].[LocationKioskSettings] ADD  DEFAULT ((30)) FOR [SessionTimeoutMinutes]
GO
ALTER TABLE [dbo].[LocationKioskSettings] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Locations] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[Locations] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsPrimary]
GO
ALTER TABLE [dbo].[Locations] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Locations] ADD  DEFAULT ('11') FOR [PlaceOfServiceCode]
GO
ALTER TABLE [dbo].[Locations] ADD  CONSTRAINT [DF_Locations_EnableLongevity]  DEFAULT ((0)) FOR [EnableLongevity]
GO
ALTER TABLE [dbo].[MedicalLienTemplates] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[MedicalLienTemplates] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Messages] ADD  DEFAULT ((0)) FOR [MessageType]
GO
ALTER TABLE [dbo].[Messages] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsRead]
GO
ALTER TABLE [dbo].[Messages] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsDeletedBySender]
GO
ALTER TABLE [dbo].[Messages] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsDeletedByRecipient]
GO
ALTER TABLE [dbo].[Messages] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Notes] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[Notes] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsAddendum]
GO
ALTER TABLE [dbo].[Notes] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[OrderResults] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Orders] ADD  DEFAULT ((0)) FOR [OrderType]
GO
ALTER TABLE [dbo].[Orders] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[Orders] ADD  DEFAULT ((0)) FOR [Priority]
GO
ALTER TABLE [dbo].[Orders] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientAllergies] ADD  DEFAULT ((0)) FOR [Type]
GO
ALTER TABLE [dbo].[PatientAllergies] ADD  DEFAULT ((0)) FOR [Severity]
GO
ALTER TABLE [dbo].[PatientAllergies] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[PatientAllergies] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientAllergies] ADD  CONSTRAINT [DF_PatientAllergies_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientAllergies] ADD  CONSTRAINT [DF_PatientAllergies_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientConversations] ADD  DEFAULT ((0)) FOR [PatientUnreadCount]
GO
ALTER TABLE [dbo].[PatientConversations] ADD  DEFAULT ((0)) FOR [UserUnreadCount]
GO
ALTER TABLE [dbo].[PatientConversations] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[PatientConversations] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientDocuments] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsEncrypted]
GO
ALTER TABLE [dbo].[PatientDocuments] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientDocuments] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientDocuments] ADD  DEFAULT ((0)) FOR [IsPatientUploaded]
GO
ALTER TABLE [dbo].[PatientFamilyHistories] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientFamilyHistories] ADD  CONSTRAINT [DF_PatientFamilyHistories_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientFamilyHistories] ADD  CONSTRAINT [DF_PatientFamilyHistories_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientGenderHealths] ADD  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientGenderHealths] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientGenderHealths] ADD  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientHealthConcerns] ADD  CONSTRAINT [DF_PatientHealthConcerns_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientHealthConcerns] ADD  CONSTRAINT [DF_PatientHealthConcerns_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientHealthConcerns] ADD  CONSTRAINT [DF_PatientHealthConcerns_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientImmunizations] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientImmunizations] ADD  CONSTRAINT [DF_PatientImmunizations_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientImmunizations] ADD  CONSTRAINT [DF_PatientImmunizations_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions] ADD  CONSTRAINT [DF_PatientIntakeSubmissions_StartedAt]  DEFAULT (getutcdate()) FOR [StartedAt]
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions] ADD  CONSTRAINT [DF_PatientIntakeSubmissions_SourceChannel]  DEFAULT ((0)) FOR [SourceChannel]
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions] ADD  CONSTRAINT [DF_PatientIntakeSubmissions_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions] ADD  CONSTRAINT [DF_PatientIntakeSubmissions_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientLedgers] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientLongevityProfiles] ADD  CONSTRAINT [DF_PatientLongevityProfiles_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientLongevityProfiles] ADD  CONSTRAINT [DF_PatientLongevityProfiles_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientLongevityProfiles] ADD  CONSTRAINT [DF_PatientLongevityProfiles_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientMedications] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[PatientMedications] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientMedications] ADD  CONSTRAINT [DF_PatientMedications_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientMedications] ADD  CONSTRAINT [DF_PatientMedications_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientMessages] ADD  DEFAULT ((0)) FOR [IsReadByPatient]
GO
ALTER TABLE [dbo].[PatientMessages] ADD  DEFAULT ((0)) FOR [IsReadByUser]
GO
ALTER TABLE [dbo].[PatientMessages] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientPortalAccounts] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[PatientPortalAccounts] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientPortalAccounts] ADD  DEFAULT ((0)) FOR [FailedLoginAttempts]
GO
ALTER TABLE [dbo].[PatientPortalInvitations] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[PatientPortalInvitations] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientPortalInvitations] ADD  DEFAULT ((0)) FOR [ResentCount]
GO
ALTER TABLE [dbo].[PatientPortalOtps] ADD  DEFAULT ((0)) FOR [AttemptCount]
GO
ALTER TABLE [dbo].[PatientPortalOtps] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientPortalPasswordResets] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientProblems] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[PatientProblems] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientProblems] ADD  CONSTRAINT [DF_PatientProblems_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientProblems] ADD  CONSTRAINT [DF_PatientProblems_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[Patients] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Patients] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientSocialHistories] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientSocialHistories] ADD  CONSTRAINT [DF_PatientSocialHistories_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientSocialHistories] ADD  CONSTRAINT [DF_PatientSocialHistories_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientStickyNotes] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientStickyNotes] ADD  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientSupplements] ADD  CONSTRAINT [DF_PatientSupplements_IsActive]  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[PatientSupplements] ADD  CONSTRAINT [DF_PatientSupplements_Source]  DEFAULT ((1)) FOR [Source]
GO
ALTER TABLE [dbo].[PatientSupplements] ADD  CONSTRAINT [DF_PatientSupplements_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientSupplements] ADD  CONSTRAINT [DF_PatientSupplements_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[PatientValidations] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsComplete]
GO
ALTER TABLE [dbo].[PatientValidations] ADD  DEFAULT ((0)) FOR [MissingFieldsCount]
GO
ALTER TABLE [dbo].[PatientValidations] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[PatientVitals] ADD  DEFAULT (getutcdate()) FOR [RecordedAt]
GO
ALTER TABLE [dbo].[Payers] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[PaymentRefunds] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[PaymentRefunds] ADD  DEFAULT (getutcdate()) FOR [RefundedAt]
GO
ALTER TABLE [dbo].[PaymentRefunds] ADD  DEFAULT ((1)) FOR [CreatedByExternal]
GO
ALTER TABLE [dbo].[Payments] ADD  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[Payments] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Payments] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsRefund]
GO
ALTER TABLE [dbo].[Payments] ADD  DEFAULT ((0)) FOR [HasOpenDispute]
GO
ALTER TABLE [dbo].[Pharmacies] ADD  DEFAULT ((1)) FOR [IsActive]
GO
ALTER TABLE [dbo].[Pharmacies] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [DosageForm]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [Quantity]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [DaysSupply]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [Route]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [Frequency]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [Refills]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [DAW]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT ((0)) FOR [IsControlledSubstance]
GO
ALTER TABLE [dbo].[Prescriptions] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[ProviderFavoriteCodes] ADD  CONSTRAINT [DF_ProviderFavoriteCodes_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Providers] ADD  DEFAULT ((0)) FOR [CredentialStatus]
GO
ALTER TABLE [dbo].[Providers] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[Providers] ADD  DEFAULT ((30)) FOR [DefaultAppointmentDuration]
GO
ALTER TABLE [dbo].[Providers] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Providers] ADD  DEFAULT (CONVERT([bit],(1))) FOR [ShowResumePopup]
GO
ALTER TABLE [dbo].[ProviderSchedules] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsAvailable]
GO
ALTER TABLE [dbo].[RecordingSessions] ADD  DEFAULT (N'NotStarted') FOR [RecordingStatus]
GO
ALTER TABLE [dbo].[RecordingSessions] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT ('US') FOR [Country]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT ('usd') FOR [DefaultCurrency]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT ((0)) FOR [ChargesEnabled]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT ((0)) FOR [PayoutsEnabled]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT ((0)) FOR [DetailsSubmitted]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT (getutcdate()) FOR [ConnectedAt]
GO
ALTER TABLE [dbo].[StripeConnectAccounts] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[StripeWebhookEvents] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[StripeWebhookEvents] ADD  DEFAULT (getutcdate()) FOR [ReceivedAt]
GO
ALTER TABLE [dbo].[SystemSettings] ADD  DEFAULT (N'string') FOR [DataType]
GO
ALTER TABLE [dbo].[SystemSettings] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[TelehealthTranscriptionChunks] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Tenants] ADD  DEFAULT ((1)) FOR [Plan]
GO
ALTER TABLE [dbo].[Tenants] ADD  DEFAULT ((1)) FOR [Status]
GO
ALTER TABLE [dbo].[Tenants] ADD  DEFAULT ((5)) FOR [MaxUsers]
GO
ALTER TABLE [dbo].[Tenants] ADD  DEFAULT ((500)) FOR [MaxPatients]
GO
ALTER TABLE [dbo].[Tenants] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Tenants] ADD  DEFAULT (CONVERT([bit],(0))) FOR [IsDeleted]
GO
ALTER TABLE [dbo].[TherapistUnavailabilities] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsFullDay]
GO
ALTER TABLE [dbo].[TherapistUnavailabilities] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[TranscriptionChunks] ADD  DEFAULT (getutcdate()) FOR [RecordedTimestamp]
GO
ALTER TABLE [dbo].[TranscriptionChunks] ADD  DEFAULT (N'Pending') FOR [TranscriptionStatus]
GO
ALTER TABLE [dbo].[TranscriptionChunks] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[TreatmentPlans] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[TreatmentPlans] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[TrustedDevices] ADD  CONSTRAINT [DF_TrustedDevices_CreatedAt]  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[UserPresence] ADD  DEFAULT ((0)) FOR [Status]
GO
ALTER TABLE [dbo].[UserPresence] ADD  DEFAULT (getutcdate()) FOR [LastActiveAt]
GO
ALTER TABLE [dbo].[Users] ADD  DEFAULT ((3)) FOR [Role]
GO
ALTER TABLE [dbo].[Users] ADD  DEFAULT (CONVERT([bit],(1))) FOR [IsActive]
GO
ALTER TABLE [dbo].[Users] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[Users] ADD  CONSTRAINT [DF_Users_OtpAttempts]  DEFAULT ((0)) FOR [OtpAttempts]
GO
ALTER TABLE [dbo].[Appointments]  WITH CHECK ADD  CONSTRAINT [FK__Appointme__CareE__05D8E0BE] FOREIGN KEY([CareEpisodeId])
REFERENCES [dbo].[CareEpisodes] ([CareEpisodeId])
GO
ALTER TABLE [dbo].[Appointments] CHECK CONSTRAINT [FK__Appointme__CareE__05D8E0BE]
GO
ALTER TABLE [dbo].[Appointments]  WITH CHECK ADD  CONSTRAINT [FK__Appointme__Locat__04E4BC85] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[Appointments] CHECK CONSTRAINT [FK__Appointme__Locat__04E4BC85]
GO
ALTER TABLE [dbo].[Appointments]  WITH CHECK ADD  CONSTRAINT [FK__Appointme__Patie__02FC7413] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Appointments] CHECK CONSTRAINT [FK__Appointme__Patie__02FC7413]
GO
ALTER TABLE [dbo].[Appointments]  WITH CHECK ADD  CONSTRAINT [FK__Appointme__Provi__03F0984C] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Appointments] CHECK CONSTRAINT [FK__Appointme__Provi__03F0984C]
GO
ALTER TABLE [dbo].[Appointments]  WITH CHECK ADD  CONSTRAINT [FK__Appointme__Tenan__02084FDA] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Appointments] CHECK CONSTRAINT [FK__Appointme__Tenan__02084FDA]
GO
ALTER TABLE [dbo].[Appointments]  WITH CHECK ADD  CONSTRAINT [FK_Appointments_CreatedByPatientId] FOREIGN KEY([CreatedByPatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Appointments] CHECK CONSTRAINT [FK_Appointments_CreatedByPatientId]
GO
ALTER TABLE [dbo].[AuditLogs]  WITH CHECK ADD  CONSTRAINT [FK__AuditLogs__Tenan__43D61337] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[AuditLogs] CHECK CONSTRAINT [FK__AuditLogs__Tenan__43D61337]
GO
ALTER TABLE [dbo].[Authorizations]  WITH CHECK ADD  CONSTRAINT [FK_Authorizations_InsuranceId] FOREIGN KEY([InsuranceId])
REFERENCES [dbo].[Insurances] ([InsuranceId])
GO
ALTER TABLE [dbo].[Authorizations] CHECK CONSTRAINT [FK_Authorizations_InsuranceId]
GO
ALTER TABLE [dbo].[Authorizations]  WITH CHECK ADD  CONSTRAINT [FK_Authorizations_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Authorizations] CHECK CONSTRAINT [FK_Authorizations_TenantId]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK__Claims__Insuranc__2180FB33] FOREIGN KEY([InsuranceId])
REFERENCES [dbo].[Insurances] ([InsuranceId])
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK__Claims__Insuranc__2180FB33]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK__Claims__PatientI__208CD6FA] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK__Claims__PatientI__208CD6FA]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK__Claims__TenantId__1F98B2C1] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK__Claims__TenantId__1F98B2C1]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK_BillingClaims_Appointments] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK_BillingClaims_Appointments]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK_BillingClaims_Authorizations] FOREIGN KEY([AuthorizationId])
REFERENCES [dbo].[Authorizations] ([AuthorizationId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK_BillingClaims_Authorizations]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK_BillingClaims_CareEpisodes] FOREIGN KEY([CareEpisodeId])
REFERENCES [dbo].[CareEpisodes] ([CareEpisodeId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK_BillingClaims_CareEpisodes]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK_BillingClaims_ClinicalNotes] FOREIGN KEY([ClinicalNoteId])
REFERENCES [dbo].[ClinicalNotes] ([ClinicalNoteId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK_BillingClaims_ClinicalNotes]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK_BillingClaims_Insurances] FOREIGN KEY([InsuranceId])
REFERENCES [dbo].[Insurances] ([InsuranceId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK_BillingClaims_Insurances]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK_BillingClaims_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK_BillingClaims_Locations]
GO
ALTER TABLE [dbo].[BillingClaims]  WITH CHECK ADD  CONSTRAINT [FK_BillingClaims_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[BillingClaims] CHECK CONSTRAINT [FK_BillingClaims_Providers]
GO
ALTER TABLE [dbo].[CareEpisodeConsentForms]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsentForms_CareEpisodeConsentId] FOREIGN KEY([CareEpisodeConsentId])
REFERENCES [dbo].[CareEpisodeConsents] ([CareEpisodeConsentId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[CareEpisodeConsentForms] CHECK CONSTRAINT [FK_CareEpisodeConsentForms_CareEpisodeConsentId]
GO
ALTER TABLE [dbo].[CareEpisodeConsentForms]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsentForms_ConsentFormTemplateId] FOREIGN KEY([ConsentFormTemplateId])
REFERENCES [dbo].[ConsentFormTemplates] ([ConsentFormTemplateId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[CareEpisodeConsentForms] CHECK CONSTRAINT [FK_CareEpisodeConsentForms_ConsentFormTemplateId]
GO
ALTER TABLE [dbo].[CareEpisodeConsentForms]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsentForms_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[CareEpisodeConsentForms] CHECK CONSTRAINT [FK_CareEpisodeConsentForms_TenantId]
GO
ALTER TABLE [dbo].[CareEpisodeConsents]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsents_AppointmentId] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
GO
ALTER TABLE [dbo].[CareEpisodeConsents] CHECK CONSTRAINT [FK_CareEpisodeConsents_AppointmentId]
GO
ALTER TABLE [dbo].[CareEpisodeConsents]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsents_CareEpisodeId] FOREIGN KEY([CareEpisodeId])
REFERENCES [dbo].[CareEpisodes] ([CareEpisodeId])
GO
ALTER TABLE [dbo].[CareEpisodeConsents] CHECK CONSTRAINT [FK_CareEpisodeConsents_CareEpisodeId]
GO
ALTER TABLE [dbo].[CareEpisodeConsents]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsents_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[CareEpisodeConsents] CHECK CONSTRAINT [FK_CareEpisodeConsents_LocationId]
GO
ALTER TABLE [dbo].[CareEpisodeConsents]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsents_PatientId] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[CareEpisodeConsents] CHECK CONSTRAINT [FK_CareEpisodeConsents_PatientId]
GO
ALTER TABLE [dbo].[CareEpisodeConsents]  WITH CHECK ADD  CONSTRAINT [FK_CareEpisodeConsents_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[CareEpisodeConsents] CHECK CONSTRAINT [FK_CareEpisodeConsents_TenantId]
GO
ALTER TABLE [dbo].[CareEpisodes]  WITH CHECK ADD  CONSTRAINT [FK__CareEpiso__Patie__787EE5A0] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[CareEpisodes] CHECK CONSTRAINT [FK__CareEpiso__Patie__787EE5A0]
GO
ALTER TABLE [dbo].[CareEpisodes]  WITH CHECK ADD  CONSTRAINT [FK__CareEpiso__Prima__797309D9] FOREIGN KEY([PrimaryProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[CareEpisodes] CHECK CONSTRAINT [FK__CareEpiso__Prima__797309D9]
GO
ALTER TABLE [dbo].[CareEpisodes]  WITH CHECK ADD  CONSTRAINT [FK__CareEpiso__Tenan__778AC167] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[CareEpisodes] CHECK CONSTRAINT [FK__CareEpiso__Tenan__778AC167]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK__Charges__Appoint__18EBB532] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK__Charges__Appoint__18EBB532]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK__Charges__ClaimId__22751F6C] FOREIGN KEY([ClaimId])
REFERENCES [dbo].[BillingClaims] ([ClaimId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK__Charges__ClaimId__22751F6C]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK__Charges__ClinicalNoteId] FOREIGN KEY([ClinicalNoteId])
REFERENCES [dbo].[ClinicalNotes] ([ClinicalNoteId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK__Charges__ClinicalNoteId]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK__Charges__NoteId__17F790F9] FOREIGN KEY([NoteId])
REFERENCES [dbo].[Notes] ([NoteId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK__Charges__NoteId__17F790F9]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK__Charges__Patient__17036CC0] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK__Charges__Patient__17036CC0]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK__Charges__Provide__19DFD96B] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK__Charges__Provide__19DFD96B]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK__Charges__TenantI__160F4887] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK__Charges__TenantI__160F4887]
GO
ALTER TABLE [dbo].[Charges]  WITH CHECK ADD  CONSTRAINT [FK_Charges_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[Charges] CHECK CONSTRAINT [FK_Charges_Locations]
GO
ALTER TABLE [dbo].[ClaimStatusHistories]  WITH CHECK ADD  CONSTRAINT [FK__ClaimStat__Claim__2739D489] FOREIGN KEY([ClaimId])
REFERENCES [dbo].[BillingClaims] ([ClaimId])
GO
ALTER TABLE [dbo].[ClaimStatusHistories] CHECK CONSTRAINT [FK__ClaimStat__Claim__2739D489]
GO
ALTER TABLE [dbo].[ClaimStatusHistories]  WITH CHECK ADD  CONSTRAINT [FK__ClaimStat__Tenan__2645B050] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[ClaimStatusHistories] CHECK CONSTRAINT [FK__ClaimStat__Tenan__2645B050]
GO
ALTER TABLE [dbo].[ClinicalNoteAddendums]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteAddendums_ClinicalNotes] FOREIGN KEY([ClinicalNoteId])
REFERENCES [dbo].[ClinicalNotes] ([ClinicalNoteId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[ClinicalNoteAddendums] CHECK CONSTRAINT [FK_ClinicalNoteAddendums_ClinicalNotes]
GO
ALTER TABLE [dbo].[ClinicalNoteAddendums]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteAddendums_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[ClinicalNoteAddendums] CHECK CONSTRAINT [FK_ClinicalNoteAddendums_Tenants]
GO
ALTER TABLE [dbo].[ClinicalNoteAddendums]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteAddendums_Users] FOREIGN KEY([SignedByUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[ClinicalNoteAddendums] CHECK CONSTRAINT [FK_ClinicalNoteAddendums_Users]
GO
ALTER TABLE [dbo].[ClinicalNoteAmendments]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteAmendments_ClinicalNotes] FOREIGN KEY([ClinicalNoteId])
REFERENCES [dbo].[ClinicalNotes] ([ClinicalNoteId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[ClinicalNoteAmendments] CHECK CONSTRAINT [FK_ClinicalNoteAmendments_ClinicalNotes]
GO
ALTER TABLE [dbo].[ClinicalNoteAmendments]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteAmendments_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[ClinicalNoteAmendments] CHECK CONSTRAINT [FK_ClinicalNoteAmendments_Tenants]
GO
ALTER TABLE [dbo].[ClinicalNoteAmendments]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteAmendments_Users] FOREIGN KEY([SignedByUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[ClinicalNoteAmendments] CHECK CONSTRAINT [FK_ClinicalNoteAmendments_Users]
GO
ALTER TABLE [dbo].[ClinicalNotes]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNotes_Appointments] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[ClinicalNotes] CHECK CONSTRAINT [FK_ClinicalNotes_Appointments]
GO
ALTER TABLE [dbo].[ClinicalNotes]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNotes_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[ClinicalNotes] CHECK CONSTRAINT [FK_ClinicalNotes_Encounters]
GO
ALTER TABLE [dbo].[ClinicalNotes]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNotes_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[ClinicalNotes] CHECK CONSTRAINT [FK_ClinicalNotes_Patients]
GO
ALTER TABLE [dbo].[ClinicalNotes]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNotes_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[ClinicalNotes] CHECK CONSTRAINT [FK_ClinicalNotes_Providers]
GO
ALTER TABLE [dbo].[ClinicalNotes]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNotes_Templates] FOREIGN KEY([TemplateId])
REFERENCES [dbo].[ClinicalNoteTemplates] ([TemplateId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[ClinicalNotes] CHECK CONSTRAINT [FK_ClinicalNotes_Templates]
GO
ALTER TABLE [dbo].[ClinicalNotes]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNotes_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[ClinicalNotes] CHECK CONSTRAINT [FK_ClinicalNotes_Tenants]
GO
ALTER TABLE [dbo].[ClinicalNoteTemplates]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteTemplates_Locations_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[ClinicalNoteTemplates] CHECK CONSTRAINT [FK_ClinicalNoteTemplates_Locations_LocationId]
GO
ALTER TABLE [dbo].[ClinicalNoteTemplates]  WITH CHECK ADD  CONSTRAINT [FK_ClinicalNoteTemplates_Tenants_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[ClinicalNoteTemplates] CHECK CONSTRAINT [FK_ClinicalNoteTemplates_Tenants_TenantId]
GO
ALTER TABLE [dbo].[ConsentFormTemplates]  WITH CHECK ADD  CONSTRAINT [FK_ConsentFormTemplates_CreatedByUserId] FOREIGN KEY([CreatedByUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[ConsentFormTemplates] CHECK CONSTRAINT [FK_ConsentFormTemplates_CreatedByUserId]
GO
ALTER TABLE [dbo].[ConsentFormTemplates]  WITH CHECK ADD  CONSTRAINT [FK_ConsentFormTemplates_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[ConsentFormTemplates] CHECK CONSTRAINT [FK_ConsentFormTemplates_LocationId]
GO
ALTER TABLE [dbo].[ConsentFormTemplates]  WITH CHECK ADD  CONSTRAINT [FK_ConsentFormTemplates_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[ConsentFormTemplates] CHECK CONSTRAINT [FK_ConsentFormTemplates_TenantId]
GO
ALTER TABLE [dbo].[ConsentFormTemplates]  WITH CHECK ADD  CONSTRAINT [FK_ConsentFormTemplates_UpdatedByUserId] FOREIGN KEY([UpdatedByUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[ConsentFormTemplates] CHECK CONSTRAINT [FK_ConsentFormTemplates_UpdatedByUserId]
GO
ALTER TABLE [dbo].[Consents]  WITH CHECK ADD  CONSTRAINT [FK__Consents__Patien__70DDC3D8] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Consents] CHECK CONSTRAINT [FK__Consents__Patien__70DDC3D8]
GO
ALTER TABLE [dbo].[Consents]  WITH CHECK ADD  CONSTRAINT [FK__Consents__Tenant__6FE99F9F] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Consents] CHECK CONSTRAINT [FK__Consents__Tenant__6FE99F9F]
GO
ALTER TABLE [dbo].[Conversations]  WITH CHECK ADD  CONSTRAINT [FK_Conversations_LastMessageSenderId] FOREIGN KEY([LastMessageSenderId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[Conversations] CHECK CONSTRAINT [FK_Conversations_LastMessageSenderId]
GO
ALTER TABLE [dbo].[Conversations]  WITH CHECK ADD  CONSTRAINT [FK_Conversations_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Conversations] CHECK CONSTRAINT [FK_Conversations_TenantId]
GO
ALTER TABLE [dbo].[Conversations]  WITH CHECK ADD  CONSTRAINT [FK_Conversations_User1Id] FOREIGN KEY([User1Id])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[Conversations] CHECK CONSTRAINT [FK_Conversations_User1Id]
GO
ALTER TABLE [dbo].[Conversations]  WITH CHECK ADD  CONSTRAINT [FK_Conversations_User2Id] FOREIGN KEY([User2Id])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[Conversations] CHECK CONSTRAINT [FK_Conversations_User2Id]
GO
ALTER TABLE [dbo].[CopayPaymentTokens]  WITH CHECK ADD  CONSTRAINT [FK_CopayPaymentTokens_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[CopayPaymentTokens] CHECK CONSTRAINT [FK_CopayPaymentTokens_Patients]
GO
ALTER TABLE [dbo].[CopayPaymentTokens]  WITH CHECK ADD  CONSTRAINT [FK_CopayPaymentTokens_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[CopayPaymentTokens] CHECK CONSTRAINT [FK_CopayPaymentTokens_Tenants]
GO
ALTER TABLE [dbo].[CredentialingRecords]  WITH CHECK ADD  CONSTRAINT [FK__Credentia__Provi__5AEE82B9] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[CredentialingRecords] CHECK CONSTRAINT [FK__Credentia__Provi__5AEE82B9]
GO
ALTER TABLE [dbo].[CredentialingRecords]  WITH CHECK ADD  CONSTRAINT [FK__Credentia__Tenan__59FA5E80] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[CredentialingRecords] CHECK CONSTRAINT [FK__Credentia__Tenan__59FA5E80]
GO
ALTER TABLE [dbo].[Encounters]  WITH CHECK ADD  CONSTRAINT [FK_Encounters_Appointments] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
GO
ALTER TABLE [dbo].[Encounters] CHECK CONSTRAINT [FK_Encounters_Appointments]
GO
ALTER TABLE [dbo].[Encounters]  WITH CHECK ADD  CONSTRAINT [FK_Encounters_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Encounters] CHECK CONSTRAINT [FK_Encounters_Patients]
GO
ALTER TABLE [dbo].[Encounters]  WITH CHECK ADD  CONSTRAINT [FK_Encounters_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Encounters] CHECK CONSTRAINT [FK_Encounters_Providers]
GO
ALTER TABLE [dbo].[Encounters]  WITH CHECK ADD  CONSTRAINT [FK_Encounters_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Encounters] CHECK CONSTRAINT [FK_Encounters_Tenants]
GO
ALTER TABLE [dbo].[InstallmentDetails]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentDetails_Payments] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[Payments] ([PaymentId])
GO
ALTER TABLE [dbo].[InstallmentDetails] CHECK CONSTRAINT [FK_InstallmentDetails_Payments]
GO
ALTER TABLE [dbo].[InstallmentDetails]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentDetails_Plans] FOREIGN KEY([PlanId])
REFERENCES [dbo].[InstallmentPlans] ([PlanId])
GO
ALTER TABLE [dbo].[InstallmentDetails] CHECK CONSTRAINT [FK_InstallmentDetails_Plans]
GO
ALTER TABLE [dbo].[InstallmentDetails]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentDetails_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[InstallmentDetails] CHECK CONSTRAINT [FK_InstallmentDetails_Tenants]
GO
ALTER TABLE [dbo].[InstallmentPlanAuditLog]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentPlanAuditLog_Details] FOREIGN KEY([DetailId])
REFERENCES [dbo].[InstallmentDetails] ([DetailId])
GO
ALTER TABLE [dbo].[InstallmentPlanAuditLog] CHECK CONSTRAINT [FK_InstallmentPlanAuditLog_Details]
GO
ALTER TABLE [dbo].[InstallmentPlanAuditLog]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentPlanAuditLog_Plans] FOREIGN KEY([PlanId])
REFERENCES [dbo].[InstallmentPlans] ([PlanId])
GO
ALTER TABLE [dbo].[InstallmentPlanAuditLog] CHECK CONSTRAINT [FK_InstallmentPlanAuditLog_Plans]
GO
ALTER TABLE [dbo].[InstallmentPlanAuditLog]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentPlanAuditLog_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[InstallmentPlanAuditLog] CHECK CONSTRAINT [FK_InstallmentPlanAuditLog_Tenants]
GO
ALTER TABLE [dbo].[InstallmentPlans]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentPlans_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[InstallmentPlans] CHECK CONSTRAINT [FK_InstallmentPlans_Locations]
GO
ALTER TABLE [dbo].[InstallmentPlans]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentPlans_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[InstallmentPlans] CHECK CONSTRAINT [FK_InstallmentPlans_Patients]
GO
ALTER TABLE [dbo].[InstallmentPlans]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentPlans_StripeConnectAccounts] FOREIGN KEY([StripeConnectAccountId])
REFERENCES [dbo].[StripeConnectAccounts] ([StripeConnectAccountId])
GO
ALTER TABLE [dbo].[InstallmentPlans] CHECK CONSTRAINT [FK_InstallmentPlans_StripeConnectAccounts]
GO
ALTER TABLE [dbo].[InstallmentPlans]  WITH CHECK ADD  CONSTRAINT [FK_InstallmentPlans_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[InstallmentPlans] CHECK CONSTRAINT [FK_InstallmentPlans_Tenants]
GO
ALTER TABLE [dbo].[Insurances]  WITH CHECK ADD  CONSTRAINT [FK__Insurance__Patie__6A30C649] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Insurances] CHECK CONSTRAINT [FK__Insurance__Patie__6A30C649]
GO
ALTER TABLE [dbo].[Insurances]  WITH CHECK ADD  CONSTRAINT [FK__Insurance__Tenan__693CA210] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Insurances] CHECK CONSTRAINT [FK__Insurance__Tenan__693CA210]
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts]  WITH CHECK ADD  CONSTRAINT [FK_IntakeVerificationAttempts_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts] CHECK CONSTRAINT [FK_IntakeVerificationAttempts_Locations]
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts]  WITH CHECK ADD  CONSTRAINT [FK_IntakeVerificationAttempts_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts] CHECK CONSTRAINT [FK_IntakeVerificationAttempts_Patients]
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts]  WITH CHECK ADD  CONSTRAINT [FK_IntakeVerificationAttempts_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[IntakeVerificationAttempts] CHECK CONSTRAINT [FK_IntakeVerificationAttempts_Tenants]
GO
ALTER TABLE [dbo].[KioskSessions]  WITH CHECK ADD  CONSTRAINT [FK_KioskSessions_AppointmentId] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
GO
ALTER TABLE [dbo].[KioskSessions] CHECK CONSTRAINT [FK_KioskSessions_AppointmentId]
GO
ALTER TABLE [dbo].[KioskSessions]  WITH CHECK ADD  CONSTRAINT [FK_KioskSessions_CareEpisodeId] FOREIGN KEY([CareEpisodeId])
REFERENCES [dbo].[CareEpisodes] ([CareEpisodeId])
GO
ALTER TABLE [dbo].[KioskSessions] CHECK CONSTRAINT [FK_KioskSessions_CareEpisodeId]
GO
ALTER TABLE [dbo].[KioskSessions]  WITH CHECK ADD  CONSTRAINT [FK_KioskSessions_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[KioskSessions] CHECK CONSTRAINT [FK_KioskSessions_LocationId]
GO
ALTER TABLE [dbo].[KioskSessions]  WITH CHECK ADD  CONSTRAINT [FK_KioskSessions_PatientId] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[KioskSessions] CHECK CONSTRAINT [FK_KioskSessions_PatientId]
GO
ALTER TABLE [dbo].[KioskSessions]  WITH CHECK ADD  CONSTRAINT [FK_KioskSessions_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[KioskSessions] CHECK CONSTRAINT [FK_KioskSessions_TenantId]
GO
ALTER TABLE [dbo].[KioskVerificationAttempts]  WITH CHECK ADD  CONSTRAINT [FK_KioskVerificationAttempts_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[KioskVerificationAttempts] CHECK CONSTRAINT [FK_KioskVerificationAttempts_LocationId]
GO
ALTER TABLE [dbo].[KioskVerificationAttempts]  WITH CHECK ADD  CONSTRAINT [FK_KioskVerificationAttempts_PatientId] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[KioskVerificationAttempts] CHECK CONSTRAINT [FK_KioskVerificationAttempts_PatientId]
GO
ALTER TABLE [dbo].[KioskVerificationAttempts]  WITH CHECK ADD  CONSTRAINT [FK_KioskVerificationAttempts_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[KioskVerificationAttempts] CHECK CONSTRAINT [FK_KioskVerificationAttempts_TenantId]
GO
ALTER TABLE [dbo].[LocationKioskSettings]  WITH CHECK ADD  CONSTRAINT [FK_LocationKioskSettings_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[LocationKioskSettings] CHECK CONSTRAINT [FK_LocationKioskSettings_LocationId]
GO
ALTER TABLE [dbo].[LocationKioskSettings]  WITH CHECK ADD  CONSTRAINT [FK_LocationKioskSettings_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[LocationKioskSettings] CHECK CONSTRAINT [FK_LocationKioskSettings_TenantId]
GO
ALTER TABLE [dbo].[LocationKioskSettings]  WITH CHECK ADD  CONSTRAINT [FK_LocationKioskSettings_TokenGeneratedByUserId] FOREIGN KEY([TokenGeneratedByUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[LocationKioskSettings] CHECK CONSTRAINT [FK_LocationKioskSettings_TokenGeneratedByUserId]
GO
ALTER TABLE [dbo].[Locations]  WITH CHECK ADD  CONSTRAINT [FK__Locations__Tenan__4316F928] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Locations] CHECK CONSTRAINT [FK__Locations__Tenan__4316F928]
GO
ALTER TABLE [dbo].[Locations]  WITH CHECK ADD  CONSTRAINT [FK_Locations_StripeConnectAccounts] FOREIGN KEY([StripeConnectAccountId])
REFERENCES [dbo].[StripeConnectAccounts] ([StripeConnectAccountId])
GO
ALTER TABLE [dbo].[Locations] CHECK CONSTRAINT [FK_Locations_StripeConnectAccounts]
GO
ALTER TABLE [dbo].[MedicalLienTemplates]  WITH CHECK ADD  CONSTRAINT [FK_MedicalLienTemplates_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[MedicalLienTemplates] CHECK CONSTRAINT [FK_MedicalLienTemplates_LocationId]
GO
ALTER TABLE [dbo].[MedicalLienTemplates]  WITH CHECK ADD  CONSTRAINT [FK_MedicalLienTemplates_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[MedicalLienTemplates] CHECK CONSTRAINT [FK_MedicalLienTemplates_TenantId]
GO
ALTER TABLE [dbo].[Messages]  WITH CHECK ADD  CONSTRAINT [FK_Messages_ConversationId] FOREIGN KEY([ConversationId])
REFERENCES [dbo].[Conversations] ([ConversationId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Messages] CHECK CONSTRAINT [FK_Messages_ConversationId]
GO
ALTER TABLE [dbo].[Messages]  WITH CHECK ADD  CONSTRAINT [FK_Messages_RecipientId] FOREIGN KEY([RecipientId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[Messages] CHECK CONSTRAINT [FK_Messages_RecipientId]
GO
ALTER TABLE [dbo].[Messages]  WITH CHECK ADD  CONSTRAINT [FK_Messages_SenderId] FOREIGN KEY([SenderId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[Messages] CHECK CONSTRAINT [FK_Messages_SenderId]
GO
ALTER TABLE [dbo].[Messages]  WITH CHECK ADD  CONSTRAINT [FK_Messages_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Messages] CHECK CONSTRAINT [FK_Messages_TenantId]
GO
ALTER TABLE [dbo].[Notes]  WITH CHECK ADD  CONSTRAINT [FK__Notes__Appointme__0E6E26BF] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
GO
ALTER TABLE [dbo].[Notes] CHECK CONSTRAINT [FK__Notes__Appointme__0E6E26BF]
GO
ALTER TABLE [dbo].[Notes]  WITH CHECK ADD  CONSTRAINT [FK__Notes__CareEpiso__0F624AF8] FOREIGN KEY([CareEpisodeId])
REFERENCES [dbo].[CareEpisodes] ([CareEpisodeId])
GO
ALTER TABLE [dbo].[Notes] CHECK CONSTRAINT [FK__Notes__CareEpiso__0F624AF8]
GO
ALTER TABLE [dbo].[Notes]  WITH CHECK ADD  CONSTRAINT [FK__Notes__ParentNot__10566F31] FOREIGN KEY([ParentNoteId])
REFERENCES [dbo].[Notes] ([NoteId])
GO
ALTER TABLE [dbo].[Notes] CHECK CONSTRAINT [FK__Notes__ParentNot__10566F31]
GO
ALTER TABLE [dbo].[Notes]  WITH CHECK ADD  CONSTRAINT [FK__Notes__PatientId__0C85DE4D] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Notes] CHECK CONSTRAINT [FK__Notes__PatientId__0C85DE4D]
GO
ALTER TABLE [dbo].[Notes]  WITH CHECK ADD  CONSTRAINT [FK__Notes__ProviderI__0D7A0286] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Notes] CHECK CONSTRAINT [FK__Notes__ProviderI__0D7A0286]
GO
ALTER TABLE [dbo].[Notes]  WITH CHECK ADD  CONSTRAINT [FK__Notes__TenantId__0B91BA14] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Notes] CHECK CONSTRAINT [FK__Notes__TenantId__0B91BA14]
GO
ALTER TABLE [dbo].[OrderResults]  WITH CHECK ADD  CONSTRAINT [FK_OrderResults_Orders] FOREIGN KEY([OrderId])
REFERENCES [dbo].[Orders] ([OrderId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[OrderResults] CHECK CONSTRAINT [FK_OrderResults_Orders]
GO
ALTER TABLE [dbo].[Orders]  WITH CHECK ADD  CONSTRAINT [FK_Orders_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
ON DELETE SET NULL
GO
ALTER TABLE [dbo].[Orders] CHECK CONSTRAINT [FK_Orders_Encounters]
GO
ALTER TABLE [dbo].[Orders]  WITH CHECK ADD  CONSTRAINT [FK_Orders_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Orders] CHECK CONSTRAINT [FK_Orders_Patients]
GO
ALTER TABLE [dbo].[Orders]  WITH CHECK ADD  CONSTRAINT [FK_Orders_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Orders] CHECK CONSTRAINT [FK_Orders_Providers]
GO
ALTER TABLE [dbo].[Orders]  WITH CHECK ADD  CONSTRAINT [FK_Orders_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Orders] CHECK CONSTRAINT [FK_Orders_Tenants]
GO
ALTER TABLE [dbo].[PatientAllergies]  WITH CHECK ADD  CONSTRAINT [FK_PatientAllergies_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[PatientAllergies] CHECK CONSTRAINT [FK_PatientAllergies_Encounters]
GO
ALTER TABLE [dbo].[PatientAllergies]  WITH CHECK ADD  CONSTRAINT [FK_PatientAllergies_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientAllergies] CHECK CONSTRAINT [FK_PatientAllergies_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientAllergies]  WITH CHECK ADD  CONSTRAINT [FK_PatientAllergies_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientAllergies] CHECK CONSTRAINT [FK_PatientAllergies_Patients]
GO
ALTER TABLE [dbo].[PatientAllergies]  WITH CHECK ADD  CONSTRAINT [FK_PatientAllergies_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientAllergies] CHECK CONSTRAINT [FK_PatientAllergies_Tenants]
GO
ALTER TABLE [dbo].[PatientConversations]  WITH CHECK ADD  CONSTRAINT [FK_PatientConversations_PatientId] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientConversations] CHECK CONSTRAINT [FK_PatientConversations_PatientId]
GO
ALTER TABLE [dbo].[PatientConversations]  WITH CHECK ADD  CONSTRAINT [FK_PatientConversations_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientConversations] CHECK CONSTRAINT [FK_PatientConversations_TenantId]
GO
ALTER TABLE [dbo].[PatientConversations]  WITH CHECK ADD  CONSTRAINT [FK_PatientConversations_UserId] FOREIGN KEY([UserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[PatientConversations] CHECK CONSTRAINT [FK_PatientConversations_UserId]
GO
ALTER TABLE [dbo].[PatientDocuments]  WITH CHECK ADD  CONSTRAINT [FK_PatientDocuments_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientDocuments] CHECK CONSTRAINT [FK_PatientDocuments_Patients]
GO
ALTER TABLE [dbo].[PatientDocuments]  WITH CHECK ADD  CONSTRAINT [FK_PatientDocuments_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientDocuments] CHECK CONSTRAINT [FK_PatientDocuments_Tenants]
GO
ALTER TABLE [dbo].[PatientDocuments]  WITH CHECK ADD  CONSTRAINT [FK_PatientDocuments_Users] FOREIGN KEY([UploadedByUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[PatientDocuments] CHECK CONSTRAINT [FK_PatientDocuments_Users]
GO
ALTER TABLE [dbo].[PatientFamilyHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientFamilyHistories_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[PatientFamilyHistories] CHECK CONSTRAINT [FK_PatientFamilyHistories_Encounters]
GO
ALTER TABLE [dbo].[PatientFamilyHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientFamilyHistories_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientFamilyHistories] CHECK CONSTRAINT [FK_PatientFamilyHistories_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientFamilyHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientFamilyHistories_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientFamilyHistories] CHECK CONSTRAINT [FK_PatientFamilyHistories_Patients]
GO
ALTER TABLE [dbo].[PatientFamilyHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientFamilyHistories_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientFamilyHistories] CHECK CONSTRAINT [FK_PatientFamilyHistories_Tenants]
GO
ALTER TABLE [dbo].[PatientGenderHealths]  WITH CHECK ADD  CONSTRAINT [FK_PatientGenderHealths_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientGenderHealths] CHECK CONSTRAINT [FK_PatientGenderHealths_Patients]
GO
ALTER TABLE [dbo].[PatientGenderHealths]  WITH CHECK ADD  CONSTRAINT [FK_PatientGenderHealths_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientGenderHealths] CHECK CONSTRAINT [FK_PatientGenderHealths_Tenants]
GO
ALTER TABLE [dbo].[PatientHealthConcerns]  WITH CHECK ADD  CONSTRAINT [FK_PatientHealthConcerns_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientHealthConcerns] CHECK CONSTRAINT [FK_PatientHealthConcerns_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientHealthConcerns]  WITH CHECK ADD  CONSTRAINT [FK_PatientHealthConcerns_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientHealthConcerns] CHECK CONSTRAINT [FK_PatientHealthConcerns_Patients]
GO
ALTER TABLE [dbo].[PatientHealthConcerns]  WITH CHECK ADD  CONSTRAINT [FK_PatientHealthConcerns_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientHealthConcerns] CHECK CONSTRAINT [FK_PatientHealthConcerns_Tenants]
GO
ALTER TABLE [dbo].[PatientImmunizations]  WITH CHECK ADD  CONSTRAINT [FK_PatientImmunizations_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[PatientImmunizations] CHECK CONSTRAINT [FK_PatientImmunizations_Encounters]
GO
ALTER TABLE [dbo].[PatientImmunizations]  WITH CHECK ADD  CONSTRAINT [FK_PatientImmunizations_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientImmunizations] CHECK CONSTRAINT [FK_PatientImmunizations_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientImmunizations]  WITH CHECK ADD  CONSTRAINT [FK_PatientImmunizations_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientImmunizations] CHECK CONSTRAINT [FK_PatientImmunizations_Patients]
GO
ALTER TABLE [dbo].[PatientImmunizations]  WITH CHECK ADD  CONSTRAINT [FK_PatientImmunizations_Providers] FOREIGN KEY([AdministeredByProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[PatientImmunizations] CHECK CONSTRAINT [FK_PatientImmunizations_Providers]
GO
ALTER TABLE [dbo].[PatientImmunizations]  WITH CHECK ADD  CONSTRAINT [FK_PatientImmunizations_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientImmunizations] CHECK CONSTRAINT [FK_PatientImmunizations_Tenants]
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions]  WITH CHECK ADD  CONSTRAINT [FK_PatientIntakeSubmissions_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions] CHECK CONSTRAINT [FK_PatientIntakeSubmissions_Patients]
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions]  WITH CHECK ADD  CONSTRAINT [FK_PatientIntakeSubmissions_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientIntakeSubmissions] CHECK CONSTRAINT [FK_PatientIntakeSubmissions_Tenants]
GO
ALTER TABLE [dbo].[PatientLedgers]  WITH CHECK ADD  CONSTRAINT [FK__PatientLe__Charg__3587F3E0] FOREIGN KEY([ChargeId])
REFERENCES [dbo].[Charges] ([ChargeId])
GO
ALTER TABLE [dbo].[PatientLedgers] CHECK CONSTRAINT [FK__PatientLe__Charg__3587F3E0]
GO
ALTER TABLE [dbo].[PatientLedgers]  WITH CHECK ADD  CONSTRAINT [FK__PatientLe__Patie__3493CFA7] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientLedgers] CHECK CONSTRAINT [FK__PatientLe__Patie__3493CFA7]
GO
ALTER TABLE [dbo].[PatientLedgers]  WITH CHECK ADD  CONSTRAINT [FK__PatientLe__Payme__367C1819] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[Payments] ([PaymentId])
GO
ALTER TABLE [dbo].[PatientLedgers] CHECK CONSTRAINT [FK__PatientLe__Payme__367C1819]
GO
ALTER TABLE [dbo].[PatientLedgers]  WITH CHECK ADD  CONSTRAINT [FK__PatientLe__Tenan__339FAB6E] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientLedgers] CHECK CONSTRAINT [FK__PatientLe__Tenan__339FAB6E]
GO
ALTER TABLE [dbo].[PatientLongevityProfiles]  WITH CHECK ADD  CONSTRAINT [FK_PatientLongevityProfiles_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientLongevityProfiles] CHECK CONSTRAINT [FK_PatientLongevityProfiles_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientLongevityProfiles]  WITH CHECK ADD  CONSTRAINT [FK_PatientLongevityProfiles_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientLongevityProfiles] CHECK CONSTRAINT [FK_PatientLongevityProfiles_Patients]
GO
ALTER TABLE [dbo].[PatientLongevityProfiles]  WITH CHECK ADD  CONSTRAINT [FK_PatientLongevityProfiles_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientLongevityProfiles] CHECK CONSTRAINT [FK_PatientLongevityProfiles_Tenants]
GO
ALTER TABLE [dbo].[PatientMedications]  WITH CHECK ADD  CONSTRAINT [FK_PatientMedications_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[PatientMedications] CHECK CONSTRAINT [FK_PatientMedications_Encounters]
GO
ALTER TABLE [dbo].[PatientMedications]  WITH CHECK ADD  CONSTRAINT [FK_PatientMedications_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientMedications] CHECK CONSTRAINT [FK_PatientMedications_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientMedications]  WITH CHECK ADD  CONSTRAINT [FK_PatientMedications_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientMedications] CHECK CONSTRAINT [FK_PatientMedications_Patients]
GO
ALTER TABLE [dbo].[PatientMedications]  WITH CHECK ADD  CONSTRAINT [FK_PatientMedications_Providers] FOREIGN KEY([PrescribedByProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[PatientMedications] CHECK CONSTRAINT [FK_PatientMedications_Providers]
GO
ALTER TABLE [dbo].[PatientMedications]  WITH CHECK ADD  CONSTRAINT [FK_PatientMedications_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientMedications] CHECK CONSTRAINT [FK_PatientMedications_Tenants]
GO
ALTER TABLE [dbo].[PatientMessages]  WITH CHECK ADD  CONSTRAINT [FK_PatientMessages_ConversationId] FOREIGN KEY([PatientConversationId])
REFERENCES [dbo].[PatientConversations] ([PatientConversationId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[PatientMessages] CHECK CONSTRAINT [FK_PatientMessages_ConversationId]
GO
ALTER TABLE [dbo].[PatientMessages]  WITH CHECK ADD  CONSTRAINT [FK_PatientMessages_SenderPatientId] FOREIGN KEY([SenderPatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientMessages] CHECK CONSTRAINT [FK_PatientMessages_SenderPatientId]
GO
ALTER TABLE [dbo].[PatientMessages]  WITH CHECK ADD  CONSTRAINT [FK_PatientMessages_SenderUserId] FOREIGN KEY([SenderUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[PatientMessages] CHECK CONSTRAINT [FK_PatientMessages_SenderUserId]
GO
ALTER TABLE [dbo].[PatientMessages]  WITH CHECK ADD  CONSTRAINT [FK_PatientMessages_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientMessages] CHECK CONSTRAINT [FK_PatientMessages_TenantId]
GO
ALTER TABLE [dbo].[PatientPortalAccounts]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalAccounts_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[PatientPortalAccounts] CHECK CONSTRAINT [FK_PatientPortalAccounts_Locations]
GO
ALTER TABLE [dbo].[PatientPortalAccounts]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalAccounts_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientPortalAccounts] CHECK CONSTRAINT [FK_PatientPortalAccounts_Patients]
GO
ALTER TABLE [dbo].[PatientPortalAccounts]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalAccounts_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientPortalAccounts] CHECK CONSTRAINT [FK_PatientPortalAccounts_Tenants]
GO
ALTER TABLE [dbo].[PatientPortalInvitations]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalInvitations_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[PatientPortalInvitations] CHECK CONSTRAINT [FK_PatientPortalInvitations_Locations]
GO
ALTER TABLE [dbo].[PatientPortalInvitations]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalInvitations_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientPortalInvitations] CHECK CONSTRAINT [FK_PatientPortalInvitations_Patients]
GO
ALTER TABLE [dbo].[PatientPortalInvitations]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalInvitations_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientPortalInvitations] CHECK CONSTRAINT [FK_PatientPortalInvitations_Tenants]
GO
ALTER TABLE [dbo].[PatientPortalOtps]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalOtps_Accounts] FOREIGN KEY([AccountId])
REFERENCES [dbo].[PatientPortalAccounts] ([PatientPortalAccountId])
GO
ALTER TABLE [dbo].[PatientPortalOtps] CHECK CONSTRAINT [FK_PatientPortalOtps_Accounts]
GO
ALTER TABLE [dbo].[PatientPortalPasswordResets]  WITH CHECK ADD  CONSTRAINT [FK_PatientPortalPasswordResets_Accounts] FOREIGN KEY([AccountId])
REFERENCES [dbo].[PatientPortalAccounts] ([PatientPortalAccountId])
GO
ALTER TABLE [dbo].[PatientPortalPasswordResets] CHECK CONSTRAINT [FK_PatientPortalPasswordResets_Accounts]
GO
ALTER TABLE [dbo].[PatientProblems]  WITH CHECK ADD  CONSTRAINT [FK_PatientProblems_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[PatientProblems] CHECK CONSTRAINT [FK_PatientProblems_Encounters]
GO
ALTER TABLE [dbo].[PatientProblems]  WITH CHECK ADD  CONSTRAINT [FK_PatientProblems_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientProblems] CHECK CONSTRAINT [FK_PatientProblems_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientProblems]  WITH CHECK ADD  CONSTRAINT [FK_PatientProblems_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientProblems] CHECK CONSTRAINT [FK_PatientProblems_Patients]
GO
ALTER TABLE [dbo].[PatientProblems]  WITH CHECK ADD  CONSTRAINT [FK_PatientProblems_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientProblems] CHECK CONSTRAINT [FK_PatientProblems_Tenants]
GO
ALTER TABLE [dbo].[Patients]  WITH CHECK ADD  CONSTRAINT [FK__Patients__Prefer__619B8048] FOREIGN KEY([PreferredProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Patients] CHECK CONSTRAINT [FK__Patients__Prefer__619B8048]
GO
ALTER TABLE [dbo].[Patients]  WITH CHECK ADD  CONSTRAINT [FK__Patients__Prefer__628FA481] FOREIGN KEY([PreferredLocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[Patients] CHECK CONSTRAINT [FK__Patients__Prefer__628FA481]
GO
ALTER TABLE [dbo].[Patients]  WITH CHECK ADD  CONSTRAINT [FK__Patients__Tenant__60A75C0F] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Patients] CHECK CONSTRAINT [FK__Patients__Tenant__60A75C0F]
GO
ALTER TABLE [dbo].[PatientSearchTokens]  WITH CHECK ADD  CONSTRAINT [FK_PatientSearchTokens_LocationId] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[PatientSearchTokens] CHECK CONSTRAINT [FK_PatientSearchTokens_LocationId]
GO
ALTER TABLE [dbo].[PatientSearchTokens]  WITH CHECK ADD  CONSTRAINT [FK_PatientSearchTokens_PatientId] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[PatientSearchTokens] CHECK CONSTRAINT [FK_PatientSearchTokens_PatientId]
GO
ALTER TABLE [dbo].[PatientSearchTokens]  WITH CHECK ADD  CONSTRAINT [FK_PatientSearchTokens_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientSearchTokens] CHECK CONSTRAINT [FK_PatientSearchTokens_TenantId]
GO
ALTER TABLE [dbo].[PatientSocialHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientSocialHistories_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[PatientSocialHistories] CHECK CONSTRAINT [FK_PatientSocialHistories_Encounters]
GO
ALTER TABLE [dbo].[PatientSocialHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientSocialHistories_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientSocialHistories] CHECK CONSTRAINT [FK_PatientSocialHistories_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientSocialHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientSocialHistories_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientSocialHistories] CHECK CONSTRAINT [FK_PatientSocialHistories_Patients]
GO
ALTER TABLE [dbo].[PatientSocialHistories]  WITH CHECK ADD  CONSTRAINT [FK_PatientSocialHistories_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientSocialHistories] CHECK CONSTRAINT [FK_PatientSocialHistories_Tenants]
GO
ALTER TABLE [dbo].[PatientStickyNotes]  WITH CHECK ADD  CONSTRAINT [FK_PatientStickyNotes_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientStickyNotes] CHECK CONSTRAINT [FK_PatientStickyNotes_Patients]
GO
ALTER TABLE [dbo].[PatientStickyNotes]  WITH CHECK ADD  CONSTRAINT [FK_PatientStickyNotes_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientStickyNotes] CHECK CONSTRAINT [FK_PatientStickyNotes_Tenants]
GO
ALTER TABLE [dbo].[PatientSupplements]  WITH CHECK ADD  CONSTRAINT [FK_PatientSupplements_IntakeSubmissions] FOREIGN KEY([IntakeSubmissionId])
REFERENCES [dbo].[PatientIntakeSubmissions] ([PatientIntakeSubmissionId])
GO
ALTER TABLE [dbo].[PatientSupplements] CHECK CONSTRAINT [FK_PatientSupplements_IntakeSubmissions]
GO
ALTER TABLE [dbo].[PatientSupplements]  WITH CHECK ADD  CONSTRAINT [FK_PatientSupplements_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientSupplements] CHECK CONSTRAINT [FK_PatientSupplements_Patients]
GO
ALTER TABLE [dbo].[PatientSupplements]  WITH CHECK ADD  CONSTRAINT [FK_PatientSupplements_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientSupplements] CHECK CONSTRAINT [FK_PatientSupplements_Tenants]
GO
ALTER TABLE [dbo].[PatientValidations]  WITH CHECK ADD  CONSTRAINT [FK__PatientValidations__PatientId] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[PatientValidations] CHECK CONSTRAINT [FK__PatientValidations__PatientId]
GO
ALTER TABLE [dbo].[PatientValidations]  WITH CHECK ADD  CONSTRAINT [FK__PatientValidations__TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientValidations] CHECK CONSTRAINT [FK__PatientValidations__TenantId]
GO
ALTER TABLE [dbo].[PatientVitals]  WITH CHECK ADD  CONSTRAINT [FK_PatientVitals_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[PatientVitals] CHECK CONSTRAINT [FK_PatientVitals_Encounters]
GO
ALTER TABLE [dbo].[PatientVitals]  WITH CHECK ADD  CONSTRAINT [FK_PatientVitals_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[PatientVitals] CHECK CONSTRAINT [FK_PatientVitals_Patients]
GO
ALTER TABLE [dbo].[PatientVitals]  WITH CHECK ADD  CONSTRAINT [FK_PatientVitals_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PatientVitals] CHECK CONSTRAINT [FK_PatientVitals_Tenants]
GO
ALTER TABLE [dbo].[PaymentRefunds]  WITH CHECK ADD  CONSTRAINT [FK_PaymentRefunds_Payments] FOREIGN KEY([PaymentId])
REFERENCES [dbo].[Payments] ([PaymentId])
GO
ALTER TABLE [dbo].[PaymentRefunds] CHECK CONSTRAINT [FK_PaymentRefunds_Payments]
GO
ALTER TABLE [dbo].[PaymentRefunds]  WITH CHECK ADD  CONSTRAINT [FK_PaymentRefunds_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[PaymentRefunds] CHECK CONSTRAINT [FK_PaymentRefunds_Tenants]
GO
ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK__Payments__Appoin__2EDAF651] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK__Payments__Appoin__2EDAF651]
GO
ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK__Payments__ClaimI__2FCF1A8A] FOREIGN KEY([ClaimId])
REFERENCES [dbo].[BillingClaims] ([ClaimId])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK__Payments__ClaimI__2FCF1A8A]
GO
ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK__Payments__Patien__2DE6D218] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK__Payments__Patien__2DE6D218]
GO
ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK__Payments__Tenant__2CF2ADDF] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK__Payments__Tenant__2CF2ADDF]
GO
ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK_Payments_InstallmentDetails] FOREIGN KEY([InstallmentDetailId])
REFERENCES [dbo].[InstallmentDetails] ([DetailId])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK_Payments_InstallmentDetails]
GO
ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK_Payments_Locations] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK_Payments_Locations]
GO
ALTER TABLE [dbo].[Payments]  WITH CHECK ADD  CONSTRAINT [FK_Payments_StripeConnectAccounts] FOREIGN KEY([StripeConnectAccountId])
REFERENCES [dbo].[StripeConnectAccounts] ([StripeConnectAccountId])
GO
ALTER TABLE [dbo].[Payments] CHECK CONSTRAINT [FK_Payments_StripeConnectAccounts]
GO
ALTER TABLE [dbo].[Pharmacies]  WITH CHECK ADD  CONSTRAINT [FK_Pharmacies_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Pharmacies] CHECK CONSTRAINT [FK_Pharmacies_Tenants]
GO
ALTER TABLE [dbo].[Prescriptions]  WITH CHECK ADD  CONSTRAINT [FK_Prescriptions_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[Prescriptions] CHECK CONSTRAINT [FK_Prescriptions_Encounters]
GO
ALTER TABLE [dbo].[Prescriptions]  WITH CHECK ADD  CONSTRAINT [FK_Prescriptions_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[Prescriptions] CHECK CONSTRAINT [FK_Prescriptions_Patients]
GO
ALTER TABLE [dbo].[Prescriptions]  WITH CHECK ADD  CONSTRAINT [FK_Prescriptions_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Prescriptions] CHECK CONSTRAINT [FK_Prescriptions_Providers]
GO
ALTER TABLE [dbo].[Prescriptions]  WITH CHECK ADD  CONSTRAINT [FK_Prescriptions_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Prescriptions] CHECK CONSTRAINT [FK_Prescriptions_Tenants]
GO
ALTER TABLE [dbo].[ProviderFavoriteCodes]  WITH CHECK ADD  CONSTRAINT [FK_ProviderFavoriteCodes_UserId] FOREIGN KEY([UserId])
REFERENCES [dbo].[Users] ([UserId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[ProviderFavoriteCodes] CHECK CONSTRAINT [FK_ProviderFavoriteCodes_UserId]
GO
ALTER TABLE [dbo].[Providers]  WITH CHECK ADD  CONSTRAINT [FK__Providers__Tenan__4F7CD00D] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Providers] CHECK CONSTRAINT [FK__Providers__Tenan__4F7CD00D]
GO
ALTER TABLE [dbo].[ProviderSchedules]  WITH CHECK ADD  CONSTRAINT [FK__ProviderS__Locat__5535A963] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Locations] ([LocationId])
GO
ALTER TABLE [dbo].[ProviderSchedules] CHECK CONSTRAINT [FK__ProviderS__Locat__5535A963]
GO
ALTER TABLE [dbo].[ProviderSchedules]  WITH CHECK ADD  CONSTRAINT [FK__ProviderS__Provi__5441852A] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[ProviderSchedules] CHECK CONSTRAINT [FK__ProviderS__Provi__5441852A]
GO
ALTER TABLE [dbo].[ProviderSchedules]  WITH CHECK ADD  CONSTRAINT [FK__ProviderS__Tenan__534D60F1] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[ProviderSchedules] CHECK CONSTRAINT [FK__ProviderS__Tenan__534D60F1]
GO
ALTER TABLE [dbo].[RecordingSessions]  WITH CHECK ADD  CONSTRAINT [FK_RecordingSessions_Appointments] FOREIGN KEY([AppointmentId])
REFERENCES [dbo].[Appointments] ([AppointmentId])
GO
ALTER TABLE [dbo].[RecordingSessions] CHECK CONSTRAINT [FK_RecordingSessions_Appointments]
GO
ALTER TABLE [dbo].[RecordingSessions]  WITH CHECK ADD  CONSTRAINT [FK_RecordingSessions_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[RecordingSessions] CHECK CONSTRAINT [FK_RecordingSessions_Patients]
GO
ALTER TABLE [dbo].[RecordingSessions]  WITH CHECK ADD  CONSTRAINT [FK_RecordingSessions_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[RecordingSessions] CHECK CONSTRAINT [FK_RecordingSessions_Providers]
GO
ALTER TABLE [dbo].[RecordingSessions]  WITH CHECK ADD  CONSTRAINT [FK_RecordingSessions_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[RecordingSessions] CHECK CONSTRAINT [FK_RecordingSessions_Tenants]
GO
ALTER TABLE [dbo].[StripeConnectAccounts]  WITH CHECK ADD  CONSTRAINT [FK_StripeConnectAccounts_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[StripeConnectAccounts] CHECK CONSTRAINT [FK_StripeConnectAccounts_Tenants]
GO
ALTER TABLE [dbo].[StripeConnectAccounts]  WITH CHECK ADD  CONSTRAINT [FK_StripeConnectAccounts_Users] FOREIGN KEY([ConnectedByUserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[StripeConnectAccounts] CHECK CONSTRAINT [FK_StripeConnectAccounts_Users]
GO
ALTER TABLE [dbo].[SystemSettings]  WITH CHECK ADD  CONSTRAINT [FK__SystemSettings__TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[SystemSettings] CHECK CONSTRAINT [FK__SystemSettings__TenantId]
GO
ALTER TABLE [dbo].[TelehealthTranscriptionChunks]  WITH CHECK ADD  CONSTRAINT [FK_TelehealthTranscriptionChunks_Encounters] FOREIGN KEY([EncounterId])
REFERENCES [dbo].[Encounters] ([EncounterId])
GO
ALTER TABLE [dbo].[TelehealthTranscriptionChunks] CHECK CONSTRAINT [FK_TelehealthTranscriptionChunks_Encounters]
GO
ALTER TABLE [dbo].[TelehealthTranscriptionChunks]  WITH CHECK ADD  CONSTRAINT [FK_TelehealthTranscriptionChunks_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[TelehealthTranscriptionChunks] CHECK CONSTRAINT [FK_TelehealthTranscriptionChunks_Tenants]
GO
ALTER TABLE [dbo].[TherapistUnavailabilities]  WITH CHECK ADD  CONSTRAINT [FK_TherapistUnavailabilities_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[TherapistUnavailabilities] CHECK CONSTRAINT [FK_TherapistUnavailabilities_Providers]
GO
ALTER TABLE [dbo].[TherapistUnavailabilities]  WITH CHECK ADD  CONSTRAINT [FK_TherapistUnavailabilities_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[TherapistUnavailabilities] CHECK CONSTRAINT [FK_TherapistUnavailabilities_Tenants]
GO
ALTER TABLE [dbo].[TranscriptionChunks]  WITH CHECK ADD  CONSTRAINT [FK_TranscriptionChunks_RecordingSessions] FOREIGN KEY([SessionId])
REFERENCES [dbo].[RecordingSessions] ([SessionId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[TranscriptionChunks] CHECK CONSTRAINT [FK_TranscriptionChunks_RecordingSessions]
GO
ALTER TABLE [dbo].[TreatmentPlans]  WITH CHECK ADD  CONSTRAINT [FK_TreatmentPlans_Patients] FOREIGN KEY([PatientId])
REFERENCES [dbo].[Patients] ([PatientId])
GO
ALTER TABLE [dbo].[TreatmentPlans] CHECK CONSTRAINT [FK_TreatmentPlans_Patients]
GO
ALTER TABLE [dbo].[TreatmentPlans]  WITH CHECK ADD  CONSTRAINT [FK_TreatmentPlans_Providers] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[TreatmentPlans] CHECK CONSTRAINT [FK_TreatmentPlans_Providers]
GO
ALTER TABLE [dbo].[TreatmentPlans]  WITH CHECK ADD  CONSTRAINT [FK_TreatmentPlans_Tenants] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[TreatmentPlans] CHECK CONSTRAINT [FK_TreatmentPlans_Tenants]
GO
ALTER TABLE [dbo].[TrustedDevices]  WITH CHECK ADD  CONSTRAINT [FK_TrustedDevices_Users] FOREIGN KEY([UserId])
REFERENCES [dbo].[Users] ([UserId])
GO
ALTER TABLE [dbo].[TrustedDevices] CHECK CONSTRAINT [FK_TrustedDevices_Users]
GO
ALTER TABLE [dbo].[UserPresence]  WITH CHECK ADD  CONSTRAINT [FK_UserPresence_TenantId] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[UserPresence] CHECK CONSTRAINT [FK_UserPresence_TenantId]
GO
ALTER TABLE [dbo].[UserPresence]  WITH CHECK ADD  CONSTRAINT [FK_UserPresence_UserId] FOREIGN KEY([UserId])
REFERENCES [dbo].[Users] ([UserId])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[UserPresence] CHECK CONSTRAINT [FK_UserPresence_UserId]
GO
ALTER TABLE [dbo].[Users]  WITH CHECK ADD  CONSTRAINT [FK__Users__ProviderI__44CA3770] FOREIGN KEY([ProviderId])
REFERENCES [dbo].[Providers] ([ProviderId])
GO
ALTER TABLE [dbo].[Users] CHECK CONSTRAINT [FK__Users__ProviderI__44CA3770]
GO
ALTER TABLE [dbo].[Users]  WITH CHECK ADD  CONSTRAINT [FK__Users__TenantId__48CFD27E] FOREIGN KEY([TenantId])
REFERENCES [dbo].[Tenants] ([TenantId])
GO
ALTER TABLE [dbo].[Users] CHECK CONSTRAINT [FK__Users__TenantId__48CFD27E]
GO
ALTER TABLE [dbo].[ProviderFavoriteCodes]  WITH CHECK ADD  CONSTRAINT [CK_ProviderFavoriteCodes_CodeType] CHECK  (([CodeType]='CPT' OR [CodeType]='ICD10'))
GO
ALTER TABLE [dbo].[ProviderFavoriteCodes] CHECK CONSTRAINT [CK_ProviderFavoriteCodes_CodeType]
GO
