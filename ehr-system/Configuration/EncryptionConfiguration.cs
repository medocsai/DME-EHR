using System.Linq.Expressions;
using EHR.Models.Generated;

namespace EHR.Configuration
{
    /// <summary>
    /// Database-First safe encryption configuration.
    /// Instead of using [EncryptedPhi] attributes on scaffolded models,
    /// we define encrypted fields here - survives scaffold regeneration.
    ///
    /// HIPAA PHI Categories Covered:
    /// - Names (patient names, subscriber names)
    /// - Geographic data (addresses, cities, zip codes)
    /// - Dates (birth dates, admission dates)
    /// - Phone/Fax numbers
    /// - Email addresses
    /// - Social Security Numbers
    /// - Medical record numbers
    /// - Health plan beneficiary numbers
    /// - Account numbers
    /// - Any other unique identifying number
    /// - Full face photos and comparable images
    /// - Any other unique identifying code
    /// </summary>
    public static class EncryptionConfiguration
    {
        private static readonly Dictionary<Type, HashSet<string>> _encryptedFields = new();
        private static bool _isInitialized = false;

        /// <summary>
        /// Register a field as requiring PHI encryption
        /// </summary>
        public static void RegisterEncryptedField<TEntity>(Expression<Func<TEntity, object?>> propertyExpression)
        {
            var propertyName = GetPropertyName(propertyExpression);
            var entityType = typeof(TEntity);

            if (!_encryptedFields.ContainsKey(entityType))
            {
                _encryptedFields[entityType] = new HashSet<string>();
            }

            _encryptedFields[entityType].Add(propertyName);
        }

        /// <summary>
        /// Check if a property requires encryption
        /// </summary>
        public static bool IsEncryptedField(Type entityType, string propertyName)
        {
            return _encryptedFields.TryGetValue(entityType, out var fields) && fields.Contains(propertyName);
        }

        /// <summary>
        /// Get all encrypted field names for an entity type
        /// </summary>
        public static IEnumerable<string> GetEncryptedFields(Type entityType)
        {
            return _encryptedFields.TryGetValue(entityType, out var fields)
                ? fields
                : Enumerable.Empty<string>();
        }

        /// <summary>
        /// Check if an entity type has any encrypted fields
        /// </summary>
        public static bool HasEncryptedFields(Type entityType)
        {
            return _encryptedFields.TryGetValue(entityType, out var fields) && fields.Count > 0;
        }

        private static string GetPropertyName<TEntity>(Expression<Func<TEntity, object?>> propertyExpression)
        {
            if (propertyExpression.Body is MemberExpression memberExpr)
            {
                return memberExpr.Member.Name;
            }
            if (propertyExpression.Body is UnaryExpression unaryExpr && unaryExpr.Operand is MemberExpression memberExpr2)
            {
                return memberExpr2.Member.Name;
            }
            throw new ArgumentException("Expression must be a property access expression");
        }

        /// <summary>
        /// Initialize all encrypted field registrations.
        /// Call this at application startup.
        /// HIPAA compliant - encrypts all PHI at rest.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            // ============================================
            // PATIENT ENCRYPTED FIELDS (CRITICAL PHI)
            // ============================================
            // Direct identifiers
            RegisterEncryptedField<Patient>(p => p.FirstName);
            RegisterEncryptedField<Patient>(p => p.LastName);

            // Contact information
            RegisterEncryptedField<Patient>(p => p.Phone);
            RegisterEncryptedField<Patient>(p => p.Email);
            RegisterEncryptedField<Patient>(p => p.Address);
            RegisterEncryptedField<Patient>(p => p.City);
            RegisterEncryptedField<Patient>(p => p.State);
            RegisterEncryptedField<Patient>(p => p.ZipCode);

            // Emergency contact (also PHI - can identify patient)
            RegisterEncryptedField<Patient>(p => p.EmergencyContactName);
            RegisterEncryptedField<Patient>(p => p.EmergencyContactPhone);
            RegisterEncryptedField<Patient>(p => p.EmergencyContactRelation);

            // SSN (encrypted - SsnLast4Hash is separate for lookup)
            RegisterEncryptedField<Patient>(p => p.SsnEncrypted);

            // ============================================
            // CLINICAL NOTE ENCRYPTED FIELDS (MEDICAL RECORDS)
            // ============================================
            RegisterEncryptedField<ClinicalNote>(n => n.HtmlContent);
            RegisterEncryptedField<ClinicalNote>(n => n.SignatureData);

            // ============================================
            // NOTES TABLE ENCRYPTED FIELDS (DEPRECATED)
            // The Notes table is deprecated. All clinical documentation
            // should use ClinicalNotes. These fields are preserved only
            // until the Notes table migration is complete.
            // ============================================
            // RegisterEncryptedField<Note>(n => n.Subjective);
            // RegisterEncryptedField<Note>(n => n.Objective);
            // RegisterEncryptedField<Note>(n => n.Assessment);
            // RegisterEncryptedField<Note>(n => n.Plan);
            // RegisterEncryptedField<Note>(n => n.VitalSigns);
            // RegisterEncryptedField<Note>(n => n.FunctionalTests);
            // RegisterEncryptedField<Note>(n => n.Interventions);
            // RegisterEncryptedField<Note>(n => n.PatientEducation);
            // RegisterEncryptedField<Note>(n => n.HomeExerciseProgram);
            // RegisterEncryptedField<Note>(n => n.SignatureData);

            // ============================================
            // INSURANCE ENCRYPTED FIELDS (FINANCIAL PHI)
            // ============================================
            RegisterEncryptedField<Insurance>(i => i.SubscriberName);
            RegisterEncryptedField<Insurance>(i => i.SubscriberId);
            RegisterEncryptedField<Insurance>(i => i.PolicyNumber);
            RegisterEncryptedField<Insurance>(i => i.GroupNumber);
            RegisterEncryptedField<Insurance>(i => i.CoverageNotes);
            RegisterEncryptedField<Insurance>(i => i.LastEligibilityResponseJson);

            // ============================================
            // AUTHORIZATION ENCRYPTED FIELDS
            // ============================================
            RegisterEncryptedField<Authorization>(a => a.AuthorizationNumber);
            RegisterEncryptedField<Authorization>(a => a.Notes);

            // ============================================
            // PROVIDER CONTACT INFO
            // ============================================
            RegisterEncryptedField<Provider>(p => p.Email);
            RegisterEncryptedField<Provider>(p => p.Phone);

            // ============================================
            // APPOINTMENT NOTES
            // ============================================
            RegisterEncryptedField<Appointment>(a => a.Reason);
            RegisterEncryptedField<Appointment>(a => a.Notes);

            // ============================================
            // CARE EPISODE MEDICAL INFO
            // ============================================
            RegisterEncryptedField<CareEpisode>(ce => ce.Goals);
            RegisterEncryptedField<CareEpisode>(ce => ce.PlanOfCare);
            RegisterEncryptedField<CareEpisode>(ce => ce.DischargeReason);

            // ============================================
            // USER CONTACT INFO
            // NOTE: User fields (Email, Phone, FirstName, LastName) are NOT encrypted.
            // This data is not PHI under HIPAA and storing as plain text
            // simplifies login and user lookup operations.
            // ============================================

            // ============================================
            // CONSENT RECORDS (Legacy)
            // ============================================
            RegisterEncryptedField<Consent>(c => c.SignatureData);

            // ============================================
            // CARE EPISODE CONSENT RECORDS (New System)
            // ============================================
            RegisterEncryptedField<CareEpisodeConsent>(c => c.VerificationSsnLast4Encrypted);
            RegisterEncryptedField<CareEpisodeConsentForm>(c => c.RenderedHtmlContent);

            // ============================================
            // PATIENT DOCUMENTS (FILE ATTACHMENTS)
            // ============================================
            RegisterEncryptedField<PatientDocument>(d => d.FileName);
            RegisterEncryptedField<PatientDocument>(d => d.Description);

            // ============================================
            // RECORDING SESSIONS (AUDIO TRANSCRIPTION PHI)
            // ============================================
            RegisterEncryptedField<RecordingSession>(r => r.CompleteTranscription);

            // ============================================
            // TRANSCRIPTION CHUNKS (AUDIO TRANSCRIPTION PHI)
            // ============================================
            RegisterEncryptedField<TranscriptionChunk>(c => c.TranscriptionText);

            // ============================================
            // INTERNAL MESSAGING SYSTEM (ENCRYPTED COMMUNICATIONS)
            // ============================================
            // Message content is encrypted for HIPAA compliance
            RegisterEncryptedField<Message>(m => m.MessageText);
            // Conversation last message preview is also encrypted
            RegisterEncryptedField<Conversation>(c => c.LastMessageText);

            // ============================================
            // CARE NOTES (STAFF -> PROVIDER COMMUNICATION)
            // Free-text clinical relays carry PHI (BPs, symptoms, names).
            // ============================================
            RegisterEncryptedField<CareNote>(n => n.Content);

            _isInitialized = true;
        }
    }
}
