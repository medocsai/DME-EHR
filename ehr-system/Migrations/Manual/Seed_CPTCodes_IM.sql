-- ============================================
-- Seed CPT Codes for Internal Medicine / Primary Care
-- Run once on each environment
-- ============================================

-- Prevent duplicates
IF NOT EXISTS (SELECT 1 FROM CPTCodes WHERE Code = '99202')
BEGIN

INSERT INTO CPTCodes (Code, Description, Category, IsTimeBased, DefaultMinutes, IsActive) VALUES
-- ============================================
-- E/M: New Patient Office Visits
-- ============================================
('99202', 'Office visit, new patient, straightforward MDM', 'E/M', 0, 15, 1),
('99203', 'Office visit, new patient, low complexity MDM', 'E/M', 0, 30, 1),
('99204', 'Office visit, new patient, moderate complexity MDM', 'E/M', 0, 45, 1),
('99205', 'Office visit, new patient, high complexity MDM', 'E/M', 0, 60, 1),

-- ============================================
-- E/M: Established Patient Office Visits
-- ============================================
('99211', 'Office visit, established patient, may not require physician', 'E/M', 0, 5, 1),
('99212', 'Office visit, established patient, straightforward MDM', 'E/M', 0, 10, 1),
('99213', 'Office visit, established patient, low complexity MDM', 'E/M', 0, 15, 1),
('99214', 'Office visit, established patient, moderate complexity MDM', 'E/M', 0, 30, 1),
('99215', 'Office visit, established patient, high complexity MDM', 'E/M', 0, 40, 1),

-- ============================================
-- E/M: Preventive - New Patient
-- ============================================
('99381', 'Preventive visit, new patient, infant (age <1)', 'Preventive', 0, 30, 1),
('99382', 'Preventive visit, new patient, early childhood (1-4)', 'Preventive', 0, 30, 1),
('99383', 'Preventive visit, new patient, late childhood (5-11)', 'Preventive', 0, 30, 1),
('99384', 'Preventive visit, new patient, adolescent (12-17)', 'Preventive', 0, 30, 1),
('99385', 'Preventive visit, new patient, adult (18-39)', 'Preventive', 0, 30, 1),
('99386', 'Preventive visit, new patient, adult (40-64)', 'Preventive', 0, 45, 1),
('99387', 'Preventive visit, new patient, adult (65+)', 'Preventive', 0, 45, 1),

-- ============================================
-- E/M: Preventive - Established Patient
-- ============================================
('99391', 'Preventive visit, established patient, infant (age <1)', 'Preventive', 0, 25, 1),
('99392', 'Preventive visit, established patient, early childhood (1-4)', 'Preventive', 0, 25, 1),
('99393', 'Preventive visit, established patient, late childhood (5-11)', 'Preventive', 0, 25, 1),
('99394', 'Preventive visit, established patient, adolescent (12-17)', 'Preventive', 0, 25, 1),
('99395', 'Preventive visit, established patient, adult (18-39)', 'Preventive', 0, 25, 1),
('99396', 'Preventive visit, established patient, adult (40-64)', 'Preventive', 0, 30, 1),
('99397', 'Preventive visit, established patient, adult (65+)', 'Preventive', 0, 30, 1),

-- ============================================
-- Telehealth E/M
-- ============================================
('99441', 'Telephone E/M, 5-10 minutes', 'Telehealth', 1, 10, 1),
('99442', 'Telephone E/M, 11-20 minutes', 'Telehealth', 1, 20, 1),
('99443', 'Telephone E/M, 21-30 minutes', 'Telehealth', 1, 30, 1),

-- ============================================
-- Procedures - Common IM
-- ============================================
('36415', 'Venipuncture, routine', 'Procedure', 0, NULL, 1),
('36416', 'Capillary blood collection (finger/heel stick)', 'Procedure', 0, NULL, 1),
('93000', 'Electrocardiogram (ECG), 12-lead, with interpretation', 'Procedure', 0, NULL, 1),
('93005', 'Electrocardiogram (ECG), 12-lead, tracing only', 'Procedure', 0, NULL, 1),
('93010', 'Electrocardiogram (ECG), 12-lead, interpretation only', 'Procedure', 0, NULL, 1),
('94760', 'Pulse oximetry, single reading', 'Procedure', 0, NULL, 1),
('96372', 'Therapeutic/prophylactic/diagnostic injection, subcutaneous or IM', 'Procedure', 0, NULL, 1),
('96374', 'Therapeutic/prophylactic/diagnostic injection, IV push', 'Procedure', 0, NULL, 1),
('69210', 'Cerumen removal, one or both ears', 'Procedure', 0, NULL, 1),
('17110', 'Destruction of benign lesions, up to 14', 'Procedure', 0, NULL, 1),
('17111', 'Destruction of benign lesions, 15 or more', 'Procedure', 0, NULL, 1),
('11102', 'Tangential biopsy of skin, single lesion', 'Procedure', 0, NULL, 1),
('11104', 'Punch biopsy of skin, single lesion', 'Procedure', 0, NULL, 1),
('11106', 'Incisional biopsy of skin, single lesion', 'Procedure', 0, NULL, 1),
('10060', 'Incision and drainage of abscess, simple', 'Procedure', 0, NULL, 1),
('10061', 'Incision and drainage of abscess, complicated', 'Procedure', 0, NULL, 1),
('12001', 'Simple repair of wound, 2.5 cm or less', 'Procedure', 0, NULL, 1),
('12002', 'Simple repair of wound, 2.6 to 7.5 cm', 'Procedure', 0, NULL, 1),
('20610', 'Arthrocentesis, aspiration/injection, major joint', 'Procedure', 0, NULL, 1),
('20605', 'Arthrocentesis, aspiration/injection, intermediate joint', 'Procedure', 0, NULL, 1),
('20600', 'Arthrocentesis, aspiration/injection, small joint', 'Procedure', 0, NULL, 1),

-- ============================================
-- Immunization Administration
-- ============================================
('90471', 'Immunization admin, first vaccine, percutaneous/IM/SC', 'Immunization', 0, NULL, 1),
('90472', 'Immunization admin, each additional vaccine', 'Immunization', 0, NULL, 1),
('90473', 'Immunization admin, first vaccine, intranasal/oral', 'Immunization', 0, NULL, 1),
('90474', 'Immunization admin, each additional, intranasal/oral', 'Immunization', 0, NULL, 1),

-- ============================================
-- Screening & Behavioral Health
-- ============================================
('96127', 'Brief emotional/behavioral assessment (e.g., PHQ-9, GAD-7)', 'Screening', 0, NULL, 1),
('96160', 'Health risk assessment instrument (e.g., health questionnaire)', 'Screening', 0, NULL, 1),
('99406', 'Smoking cessation counseling, 3-10 minutes', 'Screening', 0, 10, 1),
('99407', 'Smoking cessation counseling, greater than 10 minutes', 'Screening', 0, 15, 1),

-- ============================================
-- Chronic Care Management
-- ============================================
('99490', 'Chronic care management, first 20 minutes/month', 'CCM', 1, 20, 1),
('99491', 'Chronic care management, first 30 minutes/month (physician)', 'CCM', 1, 30, 1),
('99487', 'Complex chronic care management, first 60 minutes/month', 'CCM', 1, 60, 1),
('99489', 'Complex chronic care management, each additional 30 min', 'CCM', 1, 30, 1),

-- ============================================
-- Annual Wellness Visits (Medicare)
-- ============================================
('G0438', 'Annual wellness visit, initial (Welcome to Medicare)', 'Wellness', 0, 45, 1),
('G0439', 'Annual wellness visit, subsequent', 'Wellness', 0, 30, 1),
('G0442', 'Annual alcohol misuse screening, 15 minutes', 'Screening', 0, 15, 1),
('G0444', 'Annual depression screening, 15 minutes', 'Screening', 0, 15, 1),

-- ============================================
-- Prolonged Services
-- ============================================
('99354', 'Prolonged service, office, first 30-74 minutes', 'E/M', 1, 60, 1),
('99355', 'Prolonged service, office, each additional 30 minutes', 'E/M', 1, 30, 1),
('99417', 'Prolonged office visit, each additional 15 min (with 99205/99215)', 'E/M', 1, 15, 1),

-- ============================================
-- Lab (In-Office)
-- ============================================
('81002', 'Urinalysis, non-automated, without microscopy', 'Lab', 0, NULL, 1),
('81003', 'Urinalysis, automated, without microscopy', 'Lab', 0, NULL, 1),
('82962', 'Glucose, blood, reagent strip (glucometer)', 'Lab', 0, NULL, 1),
('82270', 'Fecal occult blood test (guaiac), 1-3 specimens', 'Lab', 0, NULL, 1),
('85018', 'Hemoglobin, blood count', 'Lab', 0, NULL, 1),
('87880', 'Strep test, rapid antigen (Group A)', 'Lab', 0, NULL, 1),
('87804', 'Influenza rapid test', 'Lab', 0, NULL, 1),
('87426', 'COVID-19 antigen detection, rapid test', 'Lab', 0, NULL, 1),

-- ============================================
-- Spirometry / Pulmonary
-- ============================================
('94010', 'Spirometry, including flow-volume loop', 'Procedure', 0, NULL, 1),
('94060', 'Spirometry before and after bronchodilator', 'Procedure', 0, NULL, 1),
('94640', 'Nebulizer treatment, inhalation', 'Procedure', 0, NULL, 1),

-- ============================================
-- Miscellaneous
-- ============================================
('99000', 'Handling/conveyance of specimen to outside lab', 'Misc', 0, NULL, 1),
('99080', 'Special reports (e.g., insurance forms, disability)', 'Misc', 0, NULL, 1);

PRINT 'CPT Codes seeded successfully: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' rows inserted.';

END
ELSE
BEGIN
    PRINT 'CPT Codes already exist. Skipping seed.';
END
