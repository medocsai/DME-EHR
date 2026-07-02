namespace EHR.Services.DemoData;

/// <summary>
/// Static data pools for demo data generation.
/// All name, drug, code, and payer constants used by generators.
/// </summary>
public static class DemoDataConstants
{
    public static readonly Random Rng = new(42); // Fixed seed for reproducibility

    // ── Name Pools ──────────────────────────────────────────────────────
    public static readonly string[] FirstNamesMale = {
        "James", "Robert", "Michael", "William", "David", "Richard", "Joseph", "Thomas",
        "Christopher", "Charles", "Daniel", "Matthew", "Anthony", "Mark", "Donald",
        "Steven", "Paul", "Andrew", "Joshua", "Kenneth", "Kevin", "Brian", "George",
        "Timothy", "Ronald", "Edward", "Jason", "Jeffrey", "Ryan", "Jacob",
        "Gary", "Nicholas", "Eric", "Jonathan", "Stephen", "Larry", "Justin", "Scott",
        "Brandon", "Benjamin", "Samuel", "Raymond", "Gregory", "Frank", "Alexander",
        "Patrick", "Jack", "Dennis", "Jerry", "Tyler"
    };

    public static readonly string[] FirstNamesFemale = {
        "Mary", "Patricia", "Jennifer", "Linda", "Barbara", "Elizabeth", "Susan", "Jessica",
        "Sarah", "Karen", "Lisa", "Nancy", "Betty", "Margaret", "Sandra",
        "Ashley", "Dorothy", "Kimberly", "Emily", "Donna", "Michelle", "Carol", "Amanda",
        "Melissa", "Deborah", "Stephanie", "Rebecca", "Sharon", "Laura", "Cynthia",
        "Kathleen", "Amy", "Angela", "Shirley", "Anna", "Brenda", "Pamela", "Emma",
        "Nicole", "Helen", "Samantha", "Katherine", "Christine", "Debra", "Rachel",
        "Carolyn", "Janet", "Catherine", "Maria", "Heather"
    };

    public static readonly string[] LastNames = {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
        "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson",
        "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson",
        "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson",
        "Walker", "Young", "Allen", "King", "Wright", "Scott", "Torres", "Nguyen",
        "Hill", "Flores", "Green", "Adams", "Nelson", "Baker", "Hall", "Rivera",
        "Campbell", "Mitchell", "Carter", "Roberts", "Gomez", "Phillips", "Evans",
        "Turner", "Diaz", "Parker", "Cruz", "Edwards", "Collins", "Reyes",
        "Stewart", "Morris", "Morales", "Murphy", "Cook", "Rogers", "Gutierrez",
        "Ortiz", "Morgan", "Cooper", "Peterson", "Bailey", "Reed", "Kelly",
        "Howard", "Ramos", "Kim", "Cox", "Ward", "Richardson", "Watson", "Brooks",
        "Chavez", "Wood", "James", "Bennett", "Gray", "Mendoza", "Ruiz", "Hughes",
        "Price", "Alvarez", "Castillo", "Sanders", "Patel", "Myers", "Long", "Ross",
        "Foster", "Jimenez"
    };

    // ── Provider Staff ──────────────────────────────────────────────────
    public static readonly (string First, string Last, string Cred, string Specialty, string Taxonomy, string Npi, string Color)[] Providers = {
        ("Laura", "Bennett", "MD", "Internal Medicine", "207R00000X", "1234567890", "#4A90D9"),
        ("Robert", "Nguyen", "DO", "Family Medicine", "207Q00000X", "1234567891", "#50C878"),
        ("Angela", "Torres", "MD", "Cardiology", "207RC0000X", "1234567892", "#E8744F"),
        ("Daniel", "Harper", "MD", "Endocrinology", "207RE0101X", "1234567893", "#9B59B6")
    };

    public static readonly (string First, string Last, int Role, string Email)[] SupportStaff = {
        ("Karen", "Mills", 7, "karen.mills@test.com"),       // Nurse
        ("Steven", "Ward", 7, "steven.ward@test.com"),       // Nurse
        ("Marcus", "Lee", 6, "marcus.lee@test.com"),         // MA
        ("Diane", "Cooper", 3, "diane.cooper@test.com"),     // Front Desk
        ("Brian", "Walsh", 4, "brian.walsh@test.com")        // Biller
    };

    // ── Locations ───────────────────────────────────────────────────────
    public static readonly (string Name, string Addr, string City, string St, string Zip, string Tz, string Pos, string Npi)[] Locations = {
        ("Main Clinic", "1200 Medical Center Dr, Suite 100", "Austin", "TX", "78701", "America/Chicago", "11", "1122334455")
    };

    // ── Insurance / Payers ──────────────────────────────────────────────
    public static readonly (string PayerName, string PayerId, decimal Copay)[] Payers = {
        ("Blue Cross Blue Shield", "BCBS1", 30m),
        ("Aetna", "AETNA1", 25m),
        ("UnitedHealthcare", "UHC1", 35m),
        ("Cigna", "CIGNA1", 30m),
        ("Medicare", "MCARE", 0m),
        ("Medicaid", "MCAID", 0m),
        ("Humana", "HUMANA1", 25m)
    };

    // ── ICD-10 Codes (Common IM Diagnoses) ──────────────────────────────
    public static readonly (string Code, string Desc)[] IcdCodes = {
        ("E11.9", "Type 2 diabetes mellitus without complications"),
        ("I10", "Essential (primary) hypertension"),
        ("E78.5", "Hyperlipidemia, unspecified"),
        ("J44.1", "Chronic obstructive pulmonary disease with acute exacerbation"),
        ("I50.9", "Heart failure, unspecified"),
        ("E03.9", "Hypothyroidism, unspecified"),
        ("K21.0", "Gastro-esophageal reflux disease with esophagitis"),
        ("M54.5", "Low back pain"),
        ("G47.33", "Obstructive sleep apnea"),
        ("N18.3", "Chronic kidney disease, stage 3"),
        ("F41.1", "Generalized anxiety disorder"),
        ("F32.1", "Major depressive disorder, single episode, moderate"),
        ("J45.20", "Mild intermittent asthma, uncomplicated"),
        ("M17.11", "Primary osteoarthritis, right knee"),
        ("E66.01", "Morbid obesity due to excess calories")
    };

    // ── CPT Codes (Common IM Visit Types) ───────────────────────────────
    public static readonly (string Code, string Desc, decimal Amount)[] CptCodes = {
        // Adult E/M
        ("99213", "Office visit, established, low complexity", 125.00m),
        ("99214", "Office visit, established, moderate complexity", 195.00m),
        ("99215", "Office visit, established, high complexity", 275.00m),
        ("99203", "Office visit, new patient, low complexity", 175.00m),
        ("99204", "Office visit, new patient, moderate complexity", 275.00m),
        ("99205", "Office visit, new patient, high complexity", 350.00m),
        // Adult Preventive
        ("99395", "Preventive visit, 18-39 years", 225.00m),
        ("99396", "Preventive visit, 40-64 years", 250.00m),
        ("99397", "Preventive visit, 65+ years", 275.00m),
        // Telehealth
        ("99441", "Telephone E/M, 5-10 min", 50.00m),
        ("99442", "Telephone E/M, 11-20 min", 90.00m),
        // Pediatric Preventive (Well-Child) - New Patient
        ("99381", "Preventive new patient, infant (< 1 yr)", 195.00m),
        ("99382", "Preventive new patient, early childhood (1-4 yr)", 195.00m),
        ("99383", "Preventive new patient, late childhood (5-11 yr)", 195.00m),
        ("99384", "Preventive new patient, adolescent (12-17 yr)", 210.00m),
        // Pediatric Preventive (Well-Child) - Established Patient
        ("99391", "Preventive established, infant (< 1 yr)", 165.00m),
        ("99392", "Preventive established, early childhood (1-4 yr)", 165.00m),
        ("99393", "Preventive established, late childhood (5-11 yr)", 175.00m),
        ("99394", "Preventive established, adolescent (12-17 yr)", 185.00m),
        // Immunization Administration
        ("90460", "Immunization admin, first component", 35.00m),
        ("90461", "Immunization admin, each additional component", 20.00m)
    };

    // ── Drugs (Common IM Prescriptions) ─────────────────────────────────
    public static readonly (string Drug, string Generic, string Strength, int Form, int Route, int Freq, int DaysSupply, decimal Qty, bool Controlled, int? Schedule)[] Drugs = {
        ("Metformin", "Metformin HCl", "500mg", 0, 0, 1, 30, 60, false, null),           // Tablet, Oral, BID
        ("Lisinopril", "Lisinopril", "10mg", 0, 0, 0, 30, 30, false, null),               // Tablet, Oral, Daily
        ("Atorvastatin", "Atorvastatin Calcium", "20mg", 0, 0, 0, 30, 30, false, null),
        ("Amlodipine", "Amlodipine Besylate", "5mg", 0, 0, 0, 30, 30, false, null),
        ("Omeprazole", "Omeprazole", "20mg", 1, 0, 0, 30, 30, false, null),               // Capsule
        ("Metoprolol Succinate", "Metoprolol Succinate ER", "50mg", 0, 0, 0, 30, 30, false, null),
        ("Levothyroxine", "Levothyroxine Sodium", "50mcg", 0, 0, 0, 30, 30, false, null),
        ("Albuterol", "Albuterol Sulfate", "90mcg/actuation", 7, 10, 9, 30, 1, false, null),  // Inhaler, Inhalation, PRN
        ("Losartan", "Losartan Potassium", "50mg", 0, 0, 0, 30, 30, false, null),
        ("Gabapentin", "Gabapentin", "300mg", 1, 0, 2, 30, 90, true, 5),                  // Controlled Sched V
        ("Tramadol", "Tramadol HCl", "50mg", 0, 0, 9, 30, 30, true, 4),                   // Controlled Sched IV, PRN
        ("Hydrochlorothiazide", "Hydrochlorothiazide", "25mg", 0, 0, 0, 30, 30, false, null),
        ("Montelukast", "Montelukast Sodium", "10mg", 0, 0, 0, 30, 30, false, null),
        ("Sertraline", "Sertraline HCl", "50mg", 0, 0, 0, 30, 30, false, null),
        ("Prednisone", "Prednisone", "10mg", 0, 0, 0, 7, 21, false, null)                 // Short course TID
    };

    // ── Lab Panels ──────────────────────────────────────────────────────
    public static readonly (string Panel, (string Test, string Unit, string Range)[] Tests)[] LabPanels = {
        ("Complete Blood Count (CBC)", new[] {
            ("WBC", "K/uL", "4.5-11.0"), ("RBC", "M/uL", "4.5-5.5"), ("Hemoglobin", "g/dL", "12.0-17.5"),
            ("Hematocrit", "%", "36-50"), ("Platelets", "K/uL", "150-400")
        }),
        ("Basic Metabolic Panel (BMP)", new[] {
            ("Glucose", "mg/dL", "70-100"), ("BUN", "mg/dL", "7-20"), ("Creatinine", "mg/dL", "0.7-1.3"),
            ("Sodium", "mEq/L", "136-145"), ("Potassium", "mEq/L", "3.5-5.1"), ("CO2", "mEq/L", "23-29")
        }),
        ("Hemoglobin A1c", new[] { ("HbA1c", "%", "4.0-5.6") }),
        ("Lipid Panel", new[] {
            ("Total Cholesterol", "mg/dL", "< 200"), ("LDL", "mg/dL", "< 100"),
            ("HDL", "mg/dL", "> 40"), ("Triglycerides", "mg/dL", "< 150")
        }),
        ("Thyroid Panel", new[] { ("TSH", "mIU/L", "0.4-4.0"), ("Free T4", "ng/dL", "0.8-1.8") }),
        ("Comprehensive Metabolic Panel", new[] {
            ("Glucose", "mg/dL", "70-100"), ("BUN", "mg/dL", "7-20"), ("Creatinine", "mg/dL", "0.7-1.3"),
            ("Sodium", "mEq/L", "136-145"), ("Potassium", "mEq/L", "3.5-5.1"),
            ("AST", "U/L", "10-40"), ("ALT", "U/L", "7-56"), ("Albumin", "g/dL", "3.4-5.4")
        })
    };

    // ── Imaging Studies ─────────────────────────────────────────────────
    public static readonly (string Study, int Modality, string BodyPart)[] ImagingStudies = {
        ("Chest X-Ray PA/Lateral", 0, "Chest"),
        ("CT Abdomen/Pelvis with contrast", 1, "Abdomen/Pelvis"),
        ("MRI Brain without contrast", 2, "Brain"),
        ("Ultrasound Abdomen", 3, "Abdomen"),
        ("DEXA Bone Density Scan", 5, "Lumbar Spine/Hip"),
        ("CT Chest without contrast", 1, "Chest"),
        ("X-Ray Lumbar Spine", 0, "Lumbar Spine")
    };

    // ── Referral Specialties ────────────────────────────────────────────
    public static readonly (string Specialty, string Facility, string Reason)[] Referrals = {
        ("Gastroenterology", "Austin GI Associates", "Screening colonoscopy"),
        ("Pulmonology", "Texas Lung Center", "COPD management"),
        ("Orthopedics", "Austin Bone & Joint", "Chronic knee pain evaluation"),
        ("Dermatology", "Hill Country Derm", "Suspicious skin lesion"),
        ("Ophthalmology", "Capital Eye Care", "Diabetic retinopathy screening"),
        ("Neurology", "Austin Neuro Specialists", "Chronic headache evaluation"),
        ("Nephrology", "Texas Kidney Associates", "CKD stage 3 management")
    };

    // ── Allergies ───────────────────────────────────────────────────────
    public static readonly (string Allergen, int Type, string Reaction, int Severity)[] Allergies = {
        ("Penicillin", 0, "Rash, hives", 1),
        ("Sulfa drugs", 0, "Anaphylaxis", 2),
        ("NSAIDs", 0, "GI upset, bleeding", 1),
        ("Latex", 3, "Contact dermatitis", 0),
        ("Codeine", 0, "Nausea, vomiting", 1),
        ("Shellfish", 1, "Urticaria", 1),
        ("Peanuts", 1, "Anaphylaxis", 3),
        ("Aspirin", 0, "Bronchospasm", 2),
        ("Contrast dye", 3, "Rash", 1),
        ("Amoxicillin", 0, "Rash", 0)
    };

    // ── Family History Conditions ───────────────────────────────────────
    public static readonly (string Relation, string Condition)[] FamilyHistory = {
        ("Mother", "Type 2 Diabetes"), ("Father", "Coronary Artery Disease"),
        ("Mother", "Hypertension"), ("Father", "Stroke"),
        ("Sister", "Breast Cancer"), ("Brother", "Colon Cancer"),
        ("Father", "Hyperlipidemia"), ("Mother", "Osteoporosis"),
        ("Maternal Grandmother", "Alzheimer's Disease"), ("Father", "Lung Cancer")
    };

    // ── Social History Categories ───────────────────────────────────────
    public static readonly (string Category, string[] Options)[] SocialHistory = {
        ("Smoking", new[] { "Never smoker", "Former smoker, quit 5 years ago", "Current smoker, 1/2 pack/day", "Current smoker, 1 pack/day" }),
        ("Alcohol", new[] { "None", "Social drinker, 1-2 drinks/week", "Moderate, 3-5 drinks/week", "Heavy, 7+ drinks/week" }),
        ("Exercise", new[] { "Sedentary", "Light, 1-2 days/week", "Moderate, 3-4 days/week", "Active, 5+ days/week" }),
        ("Diet", new[] { "Regular diet", "Low-sodium diet", "Diabetic diet", "Mediterranean diet" }),
        ("Occupation", new[] { "Office worker", "Retired", "Teacher", "Healthcare worker", "Construction", "Sales" })
    };

    // ── Immunizations ───────────────────────────────────────────────────
    public static readonly (string Vaccine, string Cvx)[] Immunizations = {
        ("Influenza (Flu)", "141"), ("COVID-19 Pfizer", "208"), ("COVID-19 Moderna", "207"),
        ("Tdap", "115"), ("Pneumococcal (PCV20)", "216"), ("Shingrix (Zoster)", "187"),
        ("Hepatitis B", "08"), ("Hepatitis A", "52")
    };

    // ── Helpers ──────────────────────────────────────────────────────────
    public static T Pick<T>(T[] arr) => arr[Rng.Next(arr.Length)];
    public static bool Chance(int percent) => Rng.Next(100) < percent;
    public static int Between(int min, int max) => Rng.Next(min, max + 1);
    public static decimal BetweenDec(decimal min, decimal max) => Math.Round(min + (decimal)Rng.NextDouble() * (max - min), 2);
    public static DateOnly RandomDate(DateOnly from, DateOnly to)
    {
        var range = to.DayNumber - from.DayNumber;
        return from.AddDays(range > 0 ? Rng.Next(range) : 0);
    }
    public static DateTime RandomDateTime(DateTime from, DateTime to)
    {
        var range = (to - from).TotalMinutes;
        return from.AddMinutes(range > 0 ? Rng.Next((int)range) : 0);
    }
}
