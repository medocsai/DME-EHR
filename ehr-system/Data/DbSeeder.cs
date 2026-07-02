using BCrypt.Net;
using EHR.Models;
using EHR.Models.Generated;
using System.Text.Json;

namespace EHR.Data;

public static class DbSeeder
{
    public static string Seed(EhrDbContext context)
    {
        // Seed Pharmacies (shared, no TenantId)
        if (!context.Pharmacies.Any())
        {
            var pharmacies = new List<Pharmacy>
            {
                new() { Name = "CVS Pharmacy #4821", NCPDP = "3412850", NPI = "1234567001", Address = "1500 Main Street", City = "Houston", State = "TX", Zip = "77002", Phone = "(713) 555-0101", Fax = "(713) 555-0102", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Walgreens #12045", NCPDP = "3418920", NPI = "1234567002", Address = "2200 Westheimer Rd", City = "Houston", State = "TX", Zip = "77098", Phone = "(713) 555-0201", Fax = "(713) 555-0202", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Rite Aid #7632", NCPDP = "3425180", NPI = "1234567003", Address = "3400 Richmond Ave", City = "Houston", State = "TX", Zip = "77046", Phone = "(713) 555-0301", Fax = "(713) 555-0302", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Walmart Pharmacy #5210", NCPDP = "3430150", NPI = "1234567004", Address = "4600 Beechnut St", City = "Houston", State = "TX", Zip = "77096", Phone = "(713) 555-0401", Fax = "(713) 555-0402", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Costco Pharmacy #1182", NCPDP = "3435200", NPI = "1234567005", Address = "5800 Westpark Dr", City = "Houston", State = "TX", Zip = "77057", Phone = "(713) 555-0501", Fax = "(713) 555-0502", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "H-E-B Pharmacy #048", NCPDP = "3440300", NPI = "1234567006", Address = "1701 W Alabama St", City = "Houston", State = "TX", Zip = "77006", Phone = "(713) 555-0601", Fax = "(713) 555-0602", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Kroger Pharmacy #529", NCPDP = "3445400", NPI = "1234567007", Address = "3300 Montrose Blvd", City = "Houston", State = "TX", Zip = "77006", Phone = "(713) 555-0701", Fax = "(713) 555-0702", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Target Pharmacy #1843", NCPDP = "3450500", NPI = "1234567008", Address = "4323 San Felipe St", City = "Houston", State = "TX", Zip = "77027", Phone = "(713) 555-0801", Fax = "(713) 555-0802", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Sam's Club Pharmacy #6340", NCPDP = "3455600", NPI = "1234567009", Address = "8700 Katy Fwy", City = "Houston", State = "TX", Zip = "77024", Phone = "(713) 555-0901", Fax = "(713) 555-0902", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "MedGroup Compounding Pharmacy", NCPDP = "3460700", NPI = "1234567010", Address = "2100 Crawford St", City = "Houston", State = "TX", Zip = "77002", Phone = "(713) 555-1001", Fax = "(713) 555-1002", IsActive = true, CreatedAt = DateTime.UtcNow }
            };
            context.Pharmacies.AddRange(pharmacies);
            context.SaveChanges();
        }

        // Seed Drug Database (shared, no TenantId)
        if (!context.DrugDatabases.Any())
        {
            var drugs = new List<DrugDatabase>
            {
                // Cardiovascular / Blood Pressure
                new() { NDCCode = "68180-0513-01", BrandName = "Prinivil", GenericName = "Lisinopril", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "May cause dizziness, cough. Avoid potassium supplements.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "68180-0514-01", BrandName = "Prinivil", GenericName = "Lisinopril", Strength = "20mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "May cause dizziness, cough. Avoid potassium supplements.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00378-0126-01", BrandName = "Norvasc", GenericName = "Amlodipine", Strength = "5mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "May cause swelling of ankles/feet. Report chest pain.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00378-0127-01", BrandName = "Norvasc", GenericName = "Amlodipine", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "May cause swelling of ankles/feet. Report chest pain.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00781-5181-01", BrandName = "Lopressor", GenericName = "Metoprolol Succinate", Strength = "25mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Do not stop abruptly. May cause fatigue, dizziness.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00781-5182-01", BrandName = "Lopressor", GenericName = "Metoprolol Succinate", Strength = "50mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Do not stop abruptly. May cause fatigue, dizziness.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00093-7367-01", BrandName = "Cozaar", GenericName = "Losartan", Strength = "50mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "May cause dizziness. Avoid potassium supplements.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "65862-0537-01", BrandName = "Diovan", GenericName = "Valsartan", Strength = "160mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth once daily", Warnings = "Do not use in pregnancy. May cause dizziness.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00228-2757-01", BrandName = "Microzide", GenericName = "Hydrochlorothiazide", Strength = "25mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily in the morning", Warnings = "Take in morning to avoid nighttime urination. Monitor potassium.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00378-0195-01", BrandName = "Lasix", GenericName = "Furosemide", Strength = "40mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily in the morning", Warnings = "Monitor potassium and kidney function. May cause dehydration.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "51079-0882-01", BrandName = "Aldactone", GenericName = "Spironolactone", Strength = "25mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Avoid potassium-rich foods. Monitor kidney function.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Cholesterol / Lipids
                new() { NDCCode = "00071-0155-23", BrandName = "Lipitor", GenericName = "Atorvastatin", Strength = "20mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily at bedtime", Warnings = "Report muscle pain/weakness. Avoid grapefruit juice.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00071-0156-23", BrandName = "Lipitor", GenericName = "Atorvastatin", Strength = "40mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily at bedtime", Warnings = "Report muscle pain/weakness. Avoid grapefruit juice.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00310-0274-90", BrandName = "Crestor", GenericName = "Rosuvastatin", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Report unexplained muscle pain. Monitor liver function.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00093-7154-01", BrandName = "Zocor", GenericName = "Simvastatin", Strength = "20mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily in the evening", Warnings = "Take in evening. Avoid grapefruit. Report muscle pain.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "66993-0059-01", BrandName = "Zetia", GenericName = "Ezetimibe", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Report muscle pain if taking with a statin.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Diabetes
                new() { NDCCode = "00228-2570-01", BrandName = "Glucophage", GenericName = "Metformin", Strength = "500mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth twice daily with meals", Warnings = "Take with food. Report nausea/vomiting. Hold before contrast dye.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00228-2571-01", BrandName = "Glucophage", GenericName = "Metformin", Strength = "1000mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth twice daily with meals", Warnings = "Take with food. Report nausea/vomiting. Hold before contrast dye.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00378-0186-01", BrandName = "Glucotrol", GenericName = "Glipizide", Strength = "5mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily 30 minutes before breakfast", Warnings = "Risk of low blood sugar. Take before meals.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00002-1437-80", BrandName = "Jardiance", GenericName = "Empagliflozin", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily in the morning", Warnings = "May cause urinary infections. Stay hydrated.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00006-0277-31", BrandName = "Januvia", GenericName = "Sitagliptin", Strength = "100mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Report severe joint pain. Monitor kidney function.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00169-3638-12", BrandName = "Lantus", GenericName = "Insulin Glargine", Strength = "100 units/mL", DosageForm = 6, Route = 2, CommonDirections = "Inject subcutaneously once daily at the same time each day", Warnings = "Do not mix with other insulins. Rotate injection sites.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // GI / Acid Reflux
                new() { NDCCode = "00186-5020-31", BrandName = "Prilosec", GenericName = "Omeprazole", Strength = "20mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth once daily before breakfast", Warnings = "Take 30 min before eating. Long-term use may affect bones.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00008-0841-81", BrandName = "Protonix", GenericName = "Pantoprazole", Strength = "40mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily before breakfast", Warnings = "Take 30 min before eating. Swallow whole, do not crush.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Thyroid
                new() { NDCCode = "00074-6961-13", BrandName = "Synthroid", GenericName = "Levothyroxine", Strength = "50mcg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily on empty stomach", Warnings = "Take on empty stomach 30-60 min before food. Avoid calcium/iron within 4 hrs.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00074-6962-13", BrandName = "Synthroid", GenericName = "Levothyroxine", Strength = "100mcg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily on empty stomach", Warnings = "Take on empty stomach 30-60 min before food. Avoid calcium/iron within 4 hrs.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Mental Health / Neuro
                new() { NDCCode = "00049-4960-66", BrandName = "Zoloft", GenericName = "Sertraline", Strength = "50mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "May take 4-6 weeks for full effect. Report mood changes.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00456-2010-01", BrandName = "Lexapro", GenericName = "Escitalopram", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "May take 4-6 weeks for full effect. Do not stop abruptly.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00002-4461-30", BrandName = "Cymbalta", GenericName = "Duloxetine", Strength = "60mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth once daily", Warnings = "Do not crush/chew. Avoid alcohol. Do not stop abruptly.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "68180-0359-09", BrandName = "Wellbutrin XL", GenericName = "Bupropion XL", Strength = "150mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily in the morning", Warnings = "Do not crush. Seizure risk at high doses. Avoid alcohol.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "50111-0648-01", BrandName = "Desyrel", GenericName = "Trazodone", Strength = "50mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth at bedtime", Warnings = "May cause drowsiness and dizziness. Take at bedtime.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00071-0865-24", BrandName = "Neurontin", GenericName = "Gabapentin", Strength = "300mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth three times daily", Warnings = "May cause drowsiness/dizziness. Do not stop abruptly.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Blood Thinners
                new() { NDCCode = "00555-0972-02", BrandName = "Plavix", GenericName = "Clopidogrel", Strength = "75mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Increases bleeding risk. Inform dentist/surgeon before procedures.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00056-0172-68", BrandName = "Coumadin", GenericName = "Warfarin", Strength = "5mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily at the same time", Warnings = "Requires regular INR monitoring. Consistent vitamin K diet. Many drug interactions.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00003-0894-21", BrandName = "Eliquis", GenericName = "Apixaban", Strength = "5mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth twice daily", Warnings = "Do not stop without consulting doctor. Increases bleeding risk.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Respiratory
                new() { NDCCode = "00591-3168-01", BrandName = "Singulair", GenericName = "Montelukast", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily in the evening", Warnings = "Report mood/behavior changes. Not for acute asthma attacks.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "66993-0015-01", BrandName = "ProAir HFA", GenericName = "Albuterol", Strength = "90mcg/actuation", DosageForm = 7, Route = 10, CommonDirections = "Inhale 2 puffs every 4-6 hours as needed", Warnings = "Shake well before use. Rinse mouth after use. For rescue use.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00173-0719-00", BrandName = "Flonase", GenericName = "Fluticasone Nasal", Strength = "50mcg/spray", DosageForm = 8, Route = 8, CommonDirections = "Spray 2 sprays in each nostril once daily", Warnings = "For nasal use only. Prime before first use.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Pain / Anti-inflammatory
                new() { NDCCode = "00904-5309-60", BrandName = "Advil", GenericName = "Ibuprofen", Strength = "400mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth every 6-8 hours as needed with food", Warnings = "Take with food. Avoid if kidney problems or on blood thinners.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00045-0485-50", BrandName = "Mobic", GenericName = "Meloxicam", Strength = "15mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily with food", Warnings = "Take with food. Avoid prolonged use. Monitor kidney function.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "68462-0147-01", BrandName = "Voltaren", GenericName = "Diclofenac", Strength = "75mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth twice daily with food", Warnings = "Take with food. GI bleeding risk. Avoid with aspirin.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00054-4728-25", BrandName = "Deltasone", GenericName = "Prednisone", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take as directed — typically taper dose over days", Warnings = "Take with food. Do not stop abruptly. May increase blood sugar.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00228-2097-01", BrandName = "Flexeril", GenericName = "Cyclobenzaprine", Strength = "10mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth three times daily as needed", Warnings = "May cause drowsiness. Avoid alcohol. Short-term use (2-3 weeks).", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Antibiotics
                new() { NDCCode = "65862-0015-01", BrandName = "Amoxil", GenericName = "Amoxicillin", Strength = "500mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth three times daily for 10 days", Warnings = "Complete full course. Report rash or diarrhea.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00069-3060-30", BrandName = "Zithromax", GenericName = "Azithromycin", Strength = "250mg", DosageForm = 0, Route = 0, CommonDirections = "Take 2 tablets day 1, then 1 tablet daily for 4 days", Warnings = "May cause nausea/diarrhea. Finish entire course.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00093-0862-01", BrandName = "Cipro", GenericName = "Ciprofloxacin", Strength = "500mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth twice daily for 7-14 days", Warnings = "Avoid dairy/calcium within 2 hrs. May cause tendon problems. Stay hydrated.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00093-2191-01", BrandName = "Vibramycin", GenericName = "Doxycycline", Strength = "100mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth twice daily with food", Warnings = "Avoid sun exposure. Take with full glass of water. Do not lie down after.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00093-3147-01", BrandName = "Keflex", GenericName = "Cephalexin", Strength = "500mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth four times daily for 7-10 days", Warnings = "Complete full course. Report rash or severe diarrhea.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // Urology
                new() { NDCCode = "00597-0081-01", BrandName = "Flomax", GenericName = "Tamsulosin", Strength = "0.4mg", DosageForm = 1, Route = 0, CommonDirections = "Take 1 capsule by mouth once daily 30 min after same meal", Warnings = "May cause dizziness when standing. Swallow whole.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00093-7336-01", BrandName = "Proscar", GenericName = "Finasteride", Strength = "5mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Women who are pregnant must not handle crushed tablets.", IsActive = true, CreatedAt = DateTime.UtcNow },

                // OTC-Strength / Common
                new() { NDCCode = "00363-0109-01", BrandName = "Tylenol", GenericName = "Acetaminophen", Strength = "500mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1-2 tablets by mouth every 6 hours as needed", Warnings = "Do not exceed 3000mg/day. Avoid alcohol. Check other meds for acetaminophen.", IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { NDCCode = "00904-2013-60", BrandName = "Bayer Aspirin", GenericName = "Aspirin", Strength = "81mg", DosageForm = 0, Route = 0, CommonDirections = "Take 1 tablet by mouth once daily", Warnings = "Do not use if aspirin allergy. Increases bleeding risk.", IsActive = true, CreatedAt = DateTime.UtcNow }
            };
            context.DrugDatabases.AddRange(drugs);
            context.SaveChanges();
        }

        // Seed Lab Test Catalog (shared reference data)
        if (!context.LabTestCatalogs.Any())
        {
            var labTests = new List<LabTestCatalog>
            {
                // CBC Panel
                new() { PanelName = "CBC", TestName = "White Blood Cell Count", TestCode = "6690-2", Unit = "K/uL", ReferenceRange = "4.5-11.0", SpecimenType = "Blood", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CBC", TestName = "Red Blood Cell Count", TestCode = "789-8", Unit = "M/uL", ReferenceRange = "4.5-5.5", SpecimenType = "Blood", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CBC", TestName = "Hemoglobin", TestCode = "718-7", Unit = "g/dL", ReferenceRange = "13.5-17.5", SpecimenType = "Blood", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CBC", TestName = "Hematocrit", TestCode = "4544-3", Unit = "%", ReferenceRange = "38.0-50.0", SpecimenType = "Blood", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CBC", TestName = "Platelet Count", TestCode = "777-3", Unit = "K/uL", ReferenceRange = "150-400", SpecimenType = "Blood", DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CBC", TestName = "MCV", TestCode = "787-2", Unit = "fL", ReferenceRange = "80-100", SpecimenType = "Blood", DisplayOrder = 6, IsActive = true, CreatedAt = DateTime.UtcNow },

                // BMP Panel
                new() { PanelName = "BMP", TestName = "Glucose", TestCode = "2345-7", Unit = "mg/dL", ReferenceRange = "70-100", SpecimenType = "Blood", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "BMP", TestName = "BUN", TestCode = "3094-0", Unit = "mg/dL", ReferenceRange = "7-20", SpecimenType = "Blood", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "BMP", TestName = "Creatinine", TestCode = "2160-0", Unit = "mg/dL", ReferenceRange = "0.7-1.3", SpecimenType = "Blood", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "BMP", TestName = "Sodium", TestCode = "2951-2", Unit = "mEq/L", ReferenceRange = "136-145", SpecimenType = "Blood", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "BMP", TestName = "Potassium", TestCode = "2823-3", Unit = "mEq/L", ReferenceRange = "3.5-5.0", SpecimenType = "Blood", DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "BMP", TestName = "Chloride", TestCode = "2075-0", Unit = "mEq/L", ReferenceRange = "98-106", SpecimenType = "Blood", DisplayOrder = 6, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "BMP", TestName = "CO2", TestCode = "2028-9", Unit = "mEq/L", ReferenceRange = "23-29", SpecimenType = "Blood", DisplayOrder = 7, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "BMP", TestName = "Calcium", TestCode = "17861-6", Unit = "mg/dL", ReferenceRange = "8.5-10.5", SpecimenType = "Blood", DisplayOrder = 8, IsActive = true, CreatedAt = DateTime.UtcNow },

                // CMP Panel (BMP + liver)
                new() { PanelName = "CMP", TestName = "Glucose", TestCode = "2345-7", Unit = "mg/dL", ReferenceRange = "70-100", SpecimenType = "Blood", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "BUN", TestCode = "3094-0", Unit = "mg/dL", ReferenceRange = "7-20", SpecimenType = "Blood", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Creatinine", TestCode = "2160-0", Unit = "mg/dL", ReferenceRange = "0.7-1.3", SpecimenType = "Blood", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Sodium", TestCode = "2951-2", Unit = "mEq/L", ReferenceRange = "136-145", SpecimenType = "Blood", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Potassium", TestCode = "2823-3", Unit = "mEq/L", ReferenceRange = "3.5-5.0", SpecimenType = "Blood", DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Chloride", TestCode = "2075-0", Unit = "mEq/L", ReferenceRange = "98-106", SpecimenType = "Blood", DisplayOrder = 6, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "CO2", TestCode = "2028-9", Unit = "mEq/L", ReferenceRange = "23-29", SpecimenType = "Blood", DisplayOrder = 7, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Calcium", TestCode = "17861-6", Unit = "mg/dL", ReferenceRange = "8.5-10.5", SpecimenType = "Blood", DisplayOrder = 8, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Total Protein", TestCode = "2885-2", Unit = "g/dL", ReferenceRange = "6.0-8.3", SpecimenType = "Blood", DisplayOrder = 9, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Albumin", TestCode = "1751-7", Unit = "g/dL", ReferenceRange = "3.5-5.0", SpecimenType = "Blood", DisplayOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Bilirubin Total", TestCode = "1975-2", Unit = "mg/dL", ReferenceRange = "0.1-1.2", SpecimenType = "Blood", DisplayOrder = 11, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "Alkaline Phosphatase", TestCode = "6768-6", Unit = "U/L", ReferenceRange = "44-147", SpecimenType = "Blood", DisplayOrder = 12, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "AST (SGOT)", TestCode = "1920-8", Unit = "U/L", ReferenceRange = "10-40", SpecimenType = "Blood", DisplayOrder = 13, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "CMP", TestName = "ALT (SGPT)", TestCode = "1742-6", Unit = "U/L", ReferenceRange = "7-56", SpecimenType = "Blood", DisplayOrder = 14, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Lipid Panel
                new() { PanelName = "Lipid Panel", TestName = "Total Cholesterol", TestCode = "2093-3", Unit = "mg/dL", ReferenceRange = "<200", SpecimenType = "Blood", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Lipid Panel", TestName = "Triglycerides", TestCode = "2571-8", Unit = "mg/dL", ReferenceRange = "<150", SpecimenType = "Blood", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Lipid Panel", TestName = "HDL Cholesterol", TestCode = "2085-9", Unit = "mg/dL", ReferenceRange = ">40", SpecimenType = "Blood", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Lipid Panel", TestName = "LDL Cholesterol", TestCode = "2089-1", Unit = "mg/dL", ReferenceRange = "<100", SpecimenType = "Blood", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Thyroid Panel
                new() { PanelName = "Thyroid Panel", TestName = "TSH", TestCode = "3016-3", Unit = "mIU/L", ReferenceRange = "0.4-4.0", SpecimenType = "Blood", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Thyroid Panel", TestName = "Free T4", TestCode = "3024-7", Unit = "ng/dL", ReferenceRange = "0.8-1.8", SpecimenType = "Blood", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Thyroid Panel", TestName = "Free T3", TestCode = "3051-0", Unit = "pg/mL", ReferenceRange = "2.3-4.2", SpecimenType = "Blood", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Urinalysis
                new() { PanelName = "Urinalysis", TestName = "Color", TestCode = "5778-6", Unit = null, ReferenceRange = "Yellow", SpecimenType = "Urine", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Urinalysis", TestName = "Clarity", TestCode = "32167-9", Unit = null, ReferenceRange = "Clear", SpecimenType = "Urine", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Urinalysis", TestName = "Specific Gravity", TestCode = "2965-2", Unit = null, ReferenceRange = "1.005-1.030", SpecimenType = "Urine", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Urinalysis", TestName = "pH", TestCode = "2756-5", Unit = null, ReferenceRange = "5.0-8.0", SpecimenType = "Urine", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Urinalysis", TestName = "Protein", TestCode = "2888-6", Unit = null, ReferenceRange = "Negative", SpecimenType = "Urine", DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = "Urinalysis", TestName = "Glucose", TestCode = "2349-9", Unit = null, ReferenceRange = "Negative", SpecimenType = "Urine", DisplayOrder = 6, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Individual Tests
                new() { PanelName = null, TestName = "Hemoglobin A1C", TestCode = "4548-4", Unit = "%", ReferenceRange = "<5.7", SpecimenType = "Blood", DisplayOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "PT/INR", TestCode = "5902-2", Unit = "INR", ReferenceRange = "0.8-1.2", SpecimenType = "Blood", DisplayOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "Vitamin D, 25-Hydroxy", TestCode = "1989-3", Unit = "ng/mL", ReferenceRange = "30-100", SpecimenType = "Blood", DisplayOrder = 3, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "Vitamin B12", TestCode = "2132-9", Unit = "pg/mL", ReferenceRange = "200-900", SpecimenType = "Blood", DisplayOrder = 4, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "Ferritin", TestCode = "2276-4", Unit = "ng/mL", ReferenceRange = "20-250", SpecimenType = "Blood", DisplayOrder = 5, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "Iron", TestCode = "2498-4", Unit = "mcg/dL", ReferenceRange = "60-170", SpecimenType = "Blood", DisplayOrder = 6, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "PSA", TestCode = "2857-1", Unit = "ng/mL", ReferenceRange = "<4.0", SpecimenType = "Blood", DisplayOrder = 7, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "ESR", TestCode = "4537-7", Unit = "mm/hr", ReferenceRange = "0-20", SpecimenType = "Blood", DisplayOrder = 8, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "CRP", TestCode = "1988-5", Unit = "mg/L", ReferenceRange = "<3.0", SpecimenType = "Blood", DisplayOrder = 9, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { PanelName = null, TestName = "Uric Acid", TestCode = "3084-1", Unit = "mg/dL", ReferenceRange = "3.5-7.2", SpecimenType = "Blood", DisplayOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow }
            };
            context.LabTestCatalogs.AddRange(labTests);
            context.SaveChanges();
        }

        // Seed demo Order (BMP lab for Robert Williams with results)
        if (!context.Orders.Any())
        {
            var order = new Order
            {
                TenantId = 1,
                PatientId = 1,
                ProviderId = 1,
                EncounterId = 1,
                OrderType = (int)OrderType.Lab,
                Status = (int)OrderStatus.ResultsReceived,
                Priority = (int)OrderPriority.Routine,
                OrderDate = new DateOnly(2026, 2, 9),
                DiagnosisCode = "E11.9",
                ClinicalIndication = "Type 2 diabetes monitoring",
                Notes = "Fasting glucose follow-up",
                LabPanelName = "BMP",
                FastingRequired = true,
                SpecimenType = "Blood",
                CreatedByUserId = 2,
                CreatedAt = DateTime.UtcNow
            };
            context.Orders.Add(order);
            context.SaveChanges();

            var results = new List<OrderResult>
            {
                new() { OrderId = order.OrderId, TestName = "Glucose", ResultValue = "142", ResultUnit = "mg/dL", ReferenceRange = "70-100", IsAbnormal = true, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new() { OrderId = order.OrderId, TestName = "BUN", ResultValue = "15", ResultUnit = "mg/dL", ReferenceRange = "7-20", IsAbnormal = false, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new() { OrderId = order.OrderId, TestName = "Creatinine", ResultValue = "1.0", ResultUnit = "mg/dL", ReferenceRange = "0.7-1.3", IsAbnormal = false, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new() { OrderId = order.OrderId, TestName = "Sodium", ResultValue = "140", ResultUnit = "mEq/L", ReferenceRange = "136-145", IsAbnormal = false, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new() { OrderId = order.OrderId, TestName = "Potassium", ResultValue = "4.2", ResultUnit = "mEq/L", ReferenceRange = "3.5-5.0", IsAbnormal = false, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new() { OrderId = order.OrderId, TestName = "Chloride", ResultValue = "102", ResultUnit = "mEq/L", ReferenceRange = "98-106", IsAbnormal = false, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new() { OrderId = order.OrderId, TestName = "CO2", ResultValue = "25", ResultUnit = "mEq/L", ReferenceRange = "23-29", IsAbnormal = false, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new() { OrderId = order.OrderId, TestName = "Calcium", ResultValue = "9.5", ResultUnit = "mg/dL", ReferenceRange = "8.5-10.5", IsAbnormal = false, ResultDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow }
            };
            context.OrderResults.AddRange(results);
            context.SaveChanges();
        }

        return "Seeding complete.";

    //    // Seed CPT Codes
    //    if (!context.Cptcodes.Any()) 
    //    {
    //        var cptCodes = new List<Cptcode>
    //        {
    //            new() { Code = "97110", Description = "Therapeutic exercises", Category = "Therapeutic", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97112", Description = "Neuromuscular reeducation", Category = "Therapeutic", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97116", Description = "Gait training", Category = "Therapeutic", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97140", Description = "Manual therapy", Category = "Therapeutic", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97530", Description = "Therapeutic activities", Category = "Therapeutic", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97535", Description = "Self-care/home management training", Category = "Therapeutic", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97542", Description = "Wheelchair management training", Category = "Therapeutic", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97161", Description = "PT evaluation - low complexity", Category = "Evaluation", DefaultMinutes = 20, IsTimeBased = false },
    //            new() { Code = "97162", Description = "PT evaluation - moderate complexity", Category = "Evaluation", DefaultMinutes = 30, IsTimeBased = false },
    //            new() { Code = "97163", Description = "PT evaluation - high complexity", Category = "Evaluation", DefaultMinutes = 45, IsTimeBased = false },
    //            new() { Code = "97164", Description = "PT re-evaluation", Category = "Evaluation", DefaultMinutes = 20, IsTimeBased = false },
    //            new() { Code = "97010", Description = "Hot/cold packs", Category = "Modalities", DefaultMinutes = 15, IsTimeBased = false },
    //            new() { Code = "97012", Description = "Mechanical traction", Category = "Modalities", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97014", Description = "Electrical stimulation (unattended)", Category = "Modalities", DefaultMinutes = 15, IsTimeBased = false },
    //            new() { Code = "97032", Description = "Electrical stimulation (attended)", Category = "Modalities", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97035", Description = "Ultrasound", Category = "Modalities", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97150", Description = "Group therapy", Category = "Group", DefaultMinutes = 15, IsTimeBased = true },
    //            new() { Code = "97545", Description = "Work hardening", Category = "Work Conditioning", DefaultMinutes = 120, IsTimeBased = true }
    //        };
    //        context.Cptcodes.AddRange(cptCodes);
    //        context.SaveChanges();
    //    }

    //    // Seed ICD Codes
    //    if (!context.Icdcodes.Any())
    //    {
    //        var icdCodes = new List<Icdcode>
    //        {
    //            new() { Code = "M54.5", Description = "Low back pain", Category = "Spine" },
    //            new() { Code = "M54.2", Description = "Cervicalgia", Category = "Spine" },
    //            new() { Code = "M54.6", Description = "Pain in thoracic spine", Category = "Spine" },
    //            new() { Code = "M54.16", Description = "Radiculopathy, lumbar region", Category = "Spine" },
    //            new() { Code = "M54.12", Description = "Radiculopathy, cervical region", Category = "Spine" },
    //            new() { Code = "M25.511", Description = "Pain in right shoulder", Category = "Upper Extremity" },
    //            new() { Code = "M25.512", Description = "Pain in left shoulder", Category = "Upper Extremity" },
    //            new() { Code = "M25.561", Description = "Pain in right knee", Category = "Lower Extremity" },
    //            new() { Code = "M25.562", Description = "Pain in left knee", Category = "Lower Extremity" },
    //            new() { Code = "M25.571", Description = "Pain in right ankle", Category = "Lower Extremity" },
    //            new() { Code = "M25.572", Description = "Pain in left ankle", Category = "Lower Extremity" },
    //            new() { Code = "M79.3", Description = "Panniculitis, unspecified", Category = "Soft Tissue" },
    //            new() { Code = "S13.4XXA", Description = "Sprain of ligaments of cervical spine, initial", Category = "Injury" },
    //            new() { Code = "S33.5XXA", Description = "Sprain of ligaments of lumbar spine, initial", Category = "Injury" },
    //            new() { Code = "S83.401A", Description = "Sprain of unspecified collateral ligament of right knee, initial", Category = "Injury" },
    //            new() { Code = "M75.101", Description = "Unspecified rotator cuff tear of right shoulder", Category = "Upper Extremity" },
    //            new() { Code = "M75.102", Description = "Unspecified rotator cuff tear of left shoulder", Category = "Upper Extremity" },
    //            new() { Code = "G89.29", Description = "Other chronic pain", Category = "Pain" },
    //            new() { Code = "R26.89", Description = "Other abnormalities of gait and mobility", Category = "Gait" },
    //            new() { Code = "M62.81", Description = "Muscle weakness (generalized)", Category = "Muscle" },
    //            new() { Code = "Z96.641", Description = "Presence of right artificial hip joint", Category = "Status" },
    //            new() { Code = "Z96.642", Description = "Presence of left artificial hip joint", Category = "Status" },
    //            new() { Code = "Z96.651", Description = "Presence of right artificial knee joint", Category = "Status" },
    //            new() { Code = "Z96.652", Description = "Presence of left artificial knee joint", Category = "Status" }
    //        };
    //        context.Icdcodes.AddRange(icdCodes);
    //        context.SaveChanges();
    //    }

    //    // Seed Payers
    //    if (!context.Payers.Any())
    //    {
    //        var payers = new List<Payer>
    //        {
    //            new() { Name = "Medicare", PayerIdCode = "CMS" },
    //            new() { Name = "Medicaid", PayerIdCode = "MDCD" },
    //            new() { Name = "Blue Cross Blue Shield", PayerIdCode = "BCBS" },
    //            new() { Name = "Aetna", PayerIdCode = "AETNA" },
    //            new() { Name = "United Healthcare", PayerIdCode = "UHC" },
    //            new() { Name = "Cigna", PayerIdCode = "CIGNA" },
    //            new() { Name = "Humana", PayerIdCode = "HUMANA" },
    //            new() { Name = "Kaiser Permanente", PayerIdCode = "KAISER" },
    //            new() { Name = "Workers Compensation", PayerIdCode = "WC" },
    //            new() { Name = "Self Pay", PayerIdCode = "SELF" }
    //        };
    //        context.Payers.AddRange(payers);
    //        context.SaveChanges();
    //    }

    //    // Create Super Admin if not exists
    //    if (!context.Users.Any(u => u.Role == (int)UserRole.SuperAdmin))
    //    {
    //        var superAdmin = new User
    //        {
    //            TenantId = null, // Super admin has no tenant
    //            Email = "admin@ptehr.com",
    //            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
    //            FirstName = "System",
    //            LastName = "Administrator",
    //            Role = (int)UserRole.SuperAdmin,
    //            IsActive = true,
    //            CreatedAt = DateTime.UtcNow
    //        };
    //        context.Users.Add(superAdmin);
    //    }

    //    context.SaveChanges();

    //    // Create demo tenant with sample data if needed
    //    if (!context.Tenants.Any())
    //    {
    //        CreateDemoTenant(context);
    //    }
    //}

    //private static void CreateDemoTenant(EhrDbContext context)
    //{
    //    // Create demo clinic
    //    var tenant = new Tenant
    //    {
    //        Name = "Peak Performance Physical Therapy",
    //        Subdomain = "peakpt",
    //        Phone = "555-123-4567",
    //        Email = "info@peakpt.demo",
    //        Address = "123 Main Street",
    //        City = "Springfield",
    //        State = "IL",
    //        ZipCode = "62701",
    //        TaxId = "12-3456789",
    //        Npi = "1234567890",
    //        Plan = (int)SubscriptionPlan.Professional,
    //        Status = (int)TenantStatus.Active,
    //        MaxUsers = 20,
    //        MaxPatients = 5000,
    //        SubscriptionStartDate = DateTime.UtcNow,
    //        SubscriptionEndDate = DateTime.UtcNow.AddYears(1),
    //        CreatedAt = DateTime.UtcNow
    //    };
    //    context.Tenants.Add(tenant);
    //    context.SaveChanges();

    //    // Add location
    //    var location = new Location
    //    {
    //        TenantId = tenant.TenantId,
    //        Name = "Main Clinic",
    //        Address = "123 Main Street",
    //        City = "Springfield",
    //        State = "IL",
    //        ZipCode = "62701",
    //        Phone = "555-123-4567",
    //        IsPrimary = true,
    //        IsActive = true
    //    };
    //    context.Locations.Add(location);

    //    // Add clinic admin
    //    var clinicAdmin = new User
    //    {
    //        TenantId = tenant.TenantId,
    //        Email = "admin@peakpt.demo",
    //        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
    //        FirstName = "John",
    //        LastName = "Admin",
    //        Role = (int)UserRole.ClinicAdmin,
    //        IsActive = true,
    //        CreatedAt = DateTime.UtcNow
    //    };
    //    context.Users.Add(clinicAdmin);

    //    // Add providers
    //    var providers = new List<Provider>
    //    {
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Npi = "1111111111",
    //            FirstName = "Sarah",
    //            LastName = "Johnson",
    //            Credentials = "PT, DPT",
    //            Specialty = "Orthopedic",
    //            Email = "sjohnson@peakpt.demo",
    //            Phone = "555-123-4568",
    //            Color = "#4CAF50",
    //            CredentialStatus = (int)CredentialStatus.Approved,
    //            CredentialExpiry = DateTime.UtcNow.AddYears(1),
    //            LicenseNumber = "PT12345",
    //            LicenseState = "IL",
    //            LicenseExpiry = DateTime.UtcNow.AddYears(2),
    //            IsActive = true,
    //            DefaultAppointmentDuration = 30
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Npi = "2222222222",
    //            FirstName = "Michael",
    //            LastName = "Chen",
    //            Credentials = "PT, DPT, OCS",
    //            Specialty = "Sports Medicine",
    //            Email = "mchen@peakpt.demo",
    //            Phone = "555-123-4569",
    //            Color = "#2196F3",
    //            CredentialStatus = (int)CredentialStatus.Approved,
    //            CredentialExpiry = DateTime.UtcNow.AddYears(1),
    //            LicenseNumber = "PT12346",
    //            LicenseState = "IL",
    //            LicenseExpiry = DateTime.UtcNow.AddYears(2),
    //            IsActive = true,
    //            DefaultAppointmentDuration = 30
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Npi = "3333333333",
    //            FirstName = "Emily",
    //            LastName = "Davis",
    //            Credentials = "PTA",
    //            Specialty = "General",
    //            Email = "edavis@peakpt.demo",
    //            Phone = "555-123-4570",
    //            Color = "#FF9800",
    //            CredentialStatus = (int)CredentialStatus.Approved,
    //            CredentialExpiry = DateTime.UtcNow.AddYears(1),
    //            LicenseNumber = "PTA5678",
    //            LicenseState = "IL",
    //            LicenseExpiry = DateTime.UtcNow.AddYears(2),
    //            IsActive = true,
    //            DefaultAppointmentDuration = 30
    //        }
    //    };
    //    context.Providers.AddRange(providers);
    //    context.SaveChanges();

    //    // Add clinician users for providers
    //    foreach (var provider in providers.Take(2))
    //    {
    //        var clinicianUser = new User
    //        {
    //            TenantId = tenant.TenantId,
    //            Email = provider.Email!,
    //            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
    //            FirstName = provider.FirstName,
    //            LastName = provider.LastName,
    //            Role = (int)UserRole.Clinician,
    //            ProviderId = provider.ProviderId,
    //            IsActive = true,
    //            CreatedAt = DateTime.UtcNow
    //        };
    //        context.Users.Add(clinicianUser);
    //    }

    //    // Add front desk user
    //    var frontDeskUser = new User
    //    {
    //        TenantId = tenant.TenantId,
    //        Email = "frontdesk@peakpt.demo",
    //        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
    //        FirstName = "Lisa",
    //        LastName = "Smith",
    //        Role = (int)UserRole.FrontDesk,
    //        IsActive = true,
    //        CreatedAt = DateTime.UtcNow
    //    };
    //    context.Users.Add(frontDeskUser);

    //    // Add biller user
    //    var billerUser = new User
    //    {
    //        TenantId = tenant.TenantId,
    //        Email = "billing@peakpt.demo",
    //        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
    //        FirstName = "Robert",
    //        LastName = "Brown",
    //        Role = (int)UserRole.Biller,
    //        IsActive = true,
    //        CreatedAt = DateTime.UtcNow
    //    };
    //    context.Users.Add(billerUser);

    //    context.SaveChanges();

    //    // Add sample patients
    //    var patients = new List<Patient>
    //    {
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Mrn = "PT-001",
    //            FirstName = "James",
    //            LastName = "Wilson",
    //            DateOfBirth = DateOnly.FromDateTime(new DateTime(1975, 3, 15)),
    //            Gender = "Male",
    //            Phone = "555-111-1111",
    //            Email = "jwilson@email.com",
    //            Address = "456 Oak Ave",
    //            City = "Springfield",
    //            State = "IL",
    //            ZipCode = "62702",
    //            EmergencyContactName = "Mary Wilson",
    //            EmergencyContactPhone = "555-111-1112",
    //            EmergencyContactRelation = "Spouse",
    //            Status = (int)PatientStatus.Active
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Mrn = "PT-002",
    //            FirstName = "Linda",
    //            LastName = "Martinez",
    //            DateOfBirth = DateOnly.FromDateTime(new DateTime(1982, 7, 22)),
    //            Gender = "Female",
    //            Phone = "555-222-2222",
    //            Email = "lmartinez@email.com",
    //            Address = "789 Elm St",
    //            City = "Springfield",
    //            State = "IL",
    //            ZipCode = "62703",
    //            EmergencyContactName = "Carlos Martinez",
    //            EmergencyContactPhone = "555-222-2223",
    //            EmergencyContactRelation = "Husband",
    //            Status = (int)PatientStatus.Active
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Mrn = "PT-003",
    //            FirstName = "David",
    //            LastName = "Thompson",
    //            DateOfBirth = DateOnly.FromDateTime(new DateTime(1968, 11, 8)),
    //            Gender = "Male",
    //            Phone = "555-333-3333",
    //            Email = "dthompson@email.com",
    //            Address = "321 Pine Rd",
    //            City = "Springfield",
    //            State = "IL",
    //            ZipCode = "62704",
    //            EmergencyContactName = "Susan Thompson",
    //            EmergencyContactPhone = "555-333-3334",
    //            EmergencyContactRelation = "Wife",
    //            Status = (int)PatientStatus.Active
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Mrn = "PT-004",
    //            FirstName = "Jennifer",
    //            LastName = "Garcia",
    //            DateOfBirth = DateOnly.FromDateTime(new DateTime(1990, 5, 30)),
    //            Gender = "Female",
    //            Phone = "555-444-4444",
    //            Email = "jgarcia@email.com",
    //            Address = "654 Maple Dr",
    //            City = "Springfield",
    //            State = "IL",
    //            ZipCode = "62705",
    //            EmergencyContactName = "Maria Garcia",
    //            EmergencyContactPhone = "555-444-4445",
    //            EmergencyContactRelation = "Mother",
    //            Status = (int)PatientStatus.Active
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            Mrn = "PT-005",
    //            FirstName = "Robert",
    //            LastName = "Anderson",
    //            DateOfBirth = DateOnly.FromDateTime(new DateTime(1955, 9, 12)),
    //            Gender = "Male",
    //            Phone = "555-555-5555",
    //            Email = "randerson@email.com",
    //            Address = "987 Cedar Ln",
    //            City = "Springfield",
    //            State = "IL",
    //            ZipCode = "62706",
    //            EmergencyContactName = "Nancy Anderson",
    //            EmergencyContactPhone = "555-555-5556",
    //            EmergencyContactRelation = "Wife",
    //            Status = (int)PatientStatus.Active
    //        }
    //    };
    //    context.Patients.AddRange(patients);
    //    context.SaveChanges();

    //    // Add insurance for patients
    //    var insurances = new List<Insurance>
    //    {
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[0].PatientId,
    //            PayerName = "Blue Cross Blue Shield",
    //            PayerId = "BCBS",
    //            PolicyNumber = "BCB123456",
    //            GroupNumber = "GRP001",
    //            SubscriberName = "James Wilson",
    //            SubscriberRelationship = "Self",
    //            Type = (int)InsuranceType.Primary,
    //            Copay = 30,
    //            IsActive = true,
    //            EligibilityStatus = (int)EligibilityStatus.Eligible
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[1].PatientId,
    //            PayerName = "Aetna",
    //            PayerId = "AETNA",
    //            PolicyNumber = "AET789012",
    //            GroupNumber = "GRP002",
    //            SubscriberName = "Carlos Martinez",
    //            SubscriberRelationship = "Spouse",
    //            Type = (int)InsuranceType.Primary,
    //            Copay = 25,
    //            IsActive = true,
    //            EligibilityStatus = (int)EligibilityStatus.Eligible
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[2].PatientId,
    //            PayerName = "Medicare",
    //            PayerId = "CMS",
    //            PolicyNumber = "MED345678",
    //            SubscriberName = "David Thompson",
    //            SubscriberRelationship = "Self",
    //            Type = (int)InsuranceType.Primary,
    //            IsActive = true,
    //            EligibilityStatus = (int)EligibilityStatus.Eligible
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[3].PatientId,
    //            PayerName = "United Healthcare",
    //            PayerId = "UHC",
    //            PolicyNumber = "UHC901234",
    //            GroupNumber = "GRP003",
    //            SubscriberName = "Jennifer Garcia",
    //            SubscriberRelationship = "Self",
    //            Type = (int)InsuranceType.Primary,
    //            Copay = 35,
    //            IsActive = true,
    //            EligibilityStatus = (int)EligibilityStatus.Eligible
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[4].PatientId,
    //            PayerName = "Cigna",
    //            PayerId = "CIGNA",
    //            PolicyNumber = "CIG567890",
    //            GroupNumber = "GRP004",
    //            SubscriberName = "Robert Anderson",
    //            SubscriberRelationship = "Self",
    //            Type = (int)InsuranceType.Primary,
    //            Copay = 40,
    //            IsActive = true,
    //            EligibilityStatus = (int)EligibilityStatus.Eligible
    //        }
    //    };
    //    context.Insurances.AddRange(insurances);
    //    context.SaveChanges();

    //    // Add consents for patients
    //    foreach (var patient in patients)
    //    {
    //        var consents = new List<Consent>
    //        {
    //            new()
    //            {
    //                TenantId = tenant.TenantId,
    //                PatientId = patient.PatientId,
    //                Type = (int)ConsentType.TreatmentConsent,
    //                SignedBy = patient.FirstName + " " + patient.LastName,
    //                SignedAt = DateTime.UtcNow.AddDays(-30),
    //                IsActive = true
    //            },
    //            new()
    //            {
    //                TenantId = tenant.TenantId,
    //                PatientId = patient.PatientId,
    //                Type = (int)ConsentType.HIPAA,
    //                SignedBy = patient.FirstName + " " + patient.LastName,
    //                SignedAt = DateTime.UtcNow.AddDays(-30),
    //                IsActive = true
    //            },
    //            new()
    //            {
    //                TenantId = tenant.TenantId,
    //                PatientId = patient.PatientId,
    //                Type = (int)ConsentType.FinancialResponsibility,
    //                SignedBy = patient.FirstName + " " + patient.LastName,
    //                SignedAt = DateTime.UtcNow.AddDays(-30),
    //                IsActive = true
    //            }
    //        };
    //        context.Consents.AddRange(consents);
    //    }
    //    context.SaveChanges();

    //    // Add care episodes for some patients
    //    var careEpisodes = new List<CareEpisode>
    //    {
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[0].PatientId,
    //            PrimaryProviderId = providers[0].ProviderId,
    //            InsuranceId = insurances[0].InsuranceId,
    //            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
    //            PrimaryDiagnosisCode = "M54.5",
    //            PrimaryDiagnosisDescription = "Low back pain",
    //            AuthorizedVisits = 12,
    //            VisitsUsed = 3,
    //            AuthorizationNumber = "AUTH001",
    //            AuthorizationExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
    //            ExpectedVisits = 12,
    //            VisitFrequency = 2,
    //            Status = (int)CareEpisodeStatus.Active
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[1].PatientId,
    //            PrimaryProviderId = providers[1].ProviderId,
    //            InsuranceId = insurances[1].InsuranceId,
    //            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-45)),
    //            PrimaryDiagnosisCode = "M25.511",
    //            PrimaryDiagnosisDescription = "Pain in right shoulder",
    //            AuthorizedVisits = 20,
    //            VisitsUsed = 8,
    //            AuthorizationNumber = "AUTH002",
    //            AuthorizationExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)),
    //            ExpectedVisits = 16,
    //            VisitFrequency = 3,
    //            Status = (int)CareEpisodeStatus.Active
    //        }
    //    };
    //    context.CareEpisodes.AddRange(careEpisodes);
    //    context.SaveChanges();

    //    // Add some appointments
    //    var today = DateTime.Today;
    //    var appointments = new List<Appointment>
    //    {
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[0].PatientId,
    //            ProviderId = providers[0].ProviderId,
    //            LocationId = location.LocationId,
    //            CareEpisodeId = careEpisodes[0].CareEpisodeId,
    //            Type = (int)AppointmentType.FollowUp,
    //            StartTime = today.AddHours(9),
    //            EndTime = today.AddHours(9).AddMinutes(30),
    //            Status = (int)AppointmentStatus.Scheduled,
    //            Reason = "Follow-up for LBP",
    //            CopayDue = 30,
    //            InsuranceVerified = true
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[1].PatientId,
    //            ProviderId = providers[1].ProviderId,
    //            LocationId = location.LocationId,
    //            CareEpisodeId = careEpisodes[1].CareEpisodeId,
    //            Type = (int)AppointmentType.FollowUp,
    //            StartTime = today.AddHours(10),
    //            EndTime = today.AddHours(10).AddMinutes(30),
    //            Status = (int)AppointmentStatus.Scheduled,
    //            Reason = "Shoulder rehabilitation",
    //            CopayDue = 25,
    //            InsuranceVerified = true
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[3].PatientId,
    //            ProviderId = providers[0].ProviderId,
    //            LocationId = location.LocationId,
    //            Type = (int)AppointmentType.InitialEvaluation,
    //            StartTime = today.AddHours(11),
    //            EndTime = today.AddHours(12),
    //            Status = (int)AppointmentStatus.Scheduled,
    //            Reason = "Initial evaluation - knee pain",
    //            CopayDue = 40,
    //            InsuranceVerified = true
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[2].PatientId,
    //            ProviderId = providers[2].ProviderId,
    //            LocationId = location.LocationId,
    //            Type = (int)AppointmentType.FollowUp,
    //            StartTime = today.AddHours(14),
    //            EndTime = today.AddHours(14).AddMinutes(30),
    //            Status = (int)AppointmentStatus.Scheduled,
    //            Reason = "Therapeutic exercise",
    //            InsuranceVerified = true
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[4].PatientId,
    //            ProviderId = providers[1].ProviderId,
    //            LocationId = location.LocationId,
    //            Type = (int)AppointmentType.ReEvaluation,
    //            StartTime = today.AddHours(15),
    //            EndTime = today.AddHours(15).AddMinutes(45),
    //            Status = (int)AppointmentStatus.Scheduled,
    //            Reason = "30-day re-evaluation",
    //            InsuranceVerified = true
    //        }
    //    };

    //    // Add appointments for tomorrow
    //    appointments.AddRange(new List<Appointment>
    //    {
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[0].PatientId,
    //            ProviderId = providers[0].ProviderId,
    //            LocationId = location.LocationId,
    //            CareEpisodeId = careEpisodes[0].CareEpisodeId,
    //            Type = (int)AppointmentType.FollowUp,
    //            StartTime = today.AddDays(1).AddHours(9),
    //            EndTime = today.AddDays(1).AddHours(9).AddMinutes(30),
    //            Status = (int)AppointmentStatus.Scheduled,
    //            Reason = "Follow-up",
    //            CopayDue = 30,
    //            InsuranceVerified = true
    //        },
    //        new()
    //        {
    //            TenantId = tenant.TenantId,
    //            PatientId = patients[1].PatientId,
    //            ProviderId = providers[1].ProviderId,
    //            LocationId = location.LocationId,
    //            CareEpisodeId = careEpisodes[1].CareEpisodeId,
    //            Type = (int)AppointmentType.FollowUp,
    //            StartTime = today.AddDays(1).AddHours(10),
    //            EndTime = today.AddDays(1).AddHours(10).AddMinutes(30),
    //            Status = (int)AppointmentStatus.Scheduled,
    //            Reason = "Shoulder rehabilitation",
    //            CopayDue = 25,
    //            InsuranceVerified = true
    //        }
    //    });

    //    context.Appointments.AddRange(appointments);
    //    context.SaveChanges();

        // Seed Payers from App_Data/payers_import.json if table is empty
        if (!context.Payers.Any())
        {
            var payersFile = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "payers_import.json");
            if (File.Exists(payersFile))
            {
                var json = File.ReadAllText(payersFile);
                var items = JsonSerializer.Deserialize<List<PayerImportItem>>(json);
                if (items != null && items.Count > 0)
                {
                    var payers = items.Select(p => new Payer
                    {
                        Name = (p.name?.Length > 100 ? p.name[..100] : p.name) ?? "",
                        PayerIdCode = (p.payerIdCode?.Length > 50 ? p.payerIdCode[..50] : p.payerIdCode) ?? "",
                        IsActive = true
                    }).ToList();

                    context.Payers.AddRange(payers);
                    context.SaveChanges();
                }
            }
        }
    }

    private class PayerImportItem
    {
        public string? name { get; set; }
        public string? payerIdCode { get; set; }
    }
}
