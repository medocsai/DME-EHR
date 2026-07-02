/**
 * HistoryPresets - Static data for clinical history quick-entry
 * Used by EncounterWorkspaceModule for autocomplete and quick-add chips
 */
const HistoryPresets = {

    // ─── COMMON ALLERGENS ─────────────────────────────────────────
    // Type: 0=Drug, 1=Food, 2=Environmental, 3=Other
    ALLERGENS: [
        // Top drug allergens
        { name: 'Penicillin', type: 0 },
        { name: 'Amoxicillin', type: 0 },
        { name: 'Ampicillin', type: 0 },
        { name: 'Sulfonamides', type: 0 },
        { name: 'Trimethoprim-Sulfamethoxazole', type: 0 },
        { name: 'Aspirin', type: 0 },
        { name: 'Ibuprofen', type: 0 },
        { name: 'Naproxen', type: 0 },
        { name: 'Codeine', type: 0 },
        { name: 'Morphine', type: 0 },
        { name: 'Hydrocodone', type: 0 },
        { name: 'Cephalosporins', type: 0 },
        { name: 'Erythromycin', type: 0 },
        { name: 'Azithromycin', type: 0 },
        { name: 'Ciprofloxacin', type: 0 },
        { name: 'Levofloxacin', type: 0 },
        { name: 'Tetracycline', type: 0 },
        { name: 'Doxycycline', type: 0 },
        { name: 'Vancomycin', type: 0 },
        { name: 'Metronidazole', type: 0 },
        { name: 'Nitrofurantoin', type: 0 },
        { name: 'Phenytoin', type: 0 },
        { name: 'Carbamazepine', type: 0 },
        { name: 'Lamotrigine', type: 0 },
        { name: 'Allopurinol', type: 0 },
        { name: 'ACE Inhibitors', type: 0 },
        { name: 'Insulin', type: 0 },
        { name: 'Heparin', type: 0 },
        { name: 'Warfarin', type: 0 },
        { name: 'Statins', type: 0 },
        { name: 'Metformin', type: 0 },
        { name: 'Gabapentin', type: 0 },
        { name: 'Lisinopril', type: 0 },
        // Common food allergens
        { name: 'Peanuts', type: 1 },
        { name: 'Tree Nuts', type: 1 },
        { name: 'Milk', type: 1 },
        { name: 'Eggs', type: 1 },
        { name: 'Wheat', type: 1 },
        { name: 'Soy', type: 1 },
        { name: 'Fish', type: 1 },
        { name: 'Shellfish', type: 1 },
        { name: 'Sesame', type: 1 },
        // Environmental
        { name: 'Latex', type: 2 },
        { name: 'Iodine', type: 2 },
        { name: 'Contrast Dye', type: 2 },
        { name: 'Bee Stings', type: 2 },
        { name: 'Dust Mites', type: 2 },
        { name: 'Pollen', type: 2 },
        { name: 'Mold', type: 2 },
        { name: 'Pet Dander', type: 2 }
    ],

    // ─── COMMON VACCINES ──────────────────────────────────────────
    VACCINES: [
        { name: 'Influenza (Flu)', cvx: '141' },
        { name: 'COVID-19 (Pfizer-BioNTech)', cvx: '208' },
        { name: 'COVID-19 (Moderna)', cvx: '207' },
        { name: 'Tdap (Tetanus, Diphtheria, Pertussis)', cvx: '115' },
        { name: 'Td (Tetanus, Diphtheria)', cvx: '113' },
        { name: 'Pneumococcal (PCV20)', cvx: '216' },
        { name: 'Pneumococcal (PPSV23)', cvx: '33' },
        { name: 'Shingrix (Zoster/Shingles)', cvx: '187' },
        { name: 'Hepatitis A', cvx: '83' },
        { name: 'Hepatitis B', cvx: '45' },
        { name: 'MMR (Measles, Mumps, Rubella)', cvx: '3' },
        { name: 'Varicella (Chickenpox)', cvx: '21' },
        { name: 'HPV (Gardasil 9)', cvx: '165' },
        { name: 'Meningococcal ACWY', cvx: '114' },
        { name: 'Meningococcal B', cvx: '162' },
        { name: 'Polio (IPV)', cvx: '10' },
        { name: 'Rotavirus', cvx: '116' },
        { name: 'RSV (Arexvy/Abrysvo)', cvx: '230' },
        { name: 'Japanese Encephalitis', cvx: '134' },
        { name: 'Rabies', cvx: '18' },
        { name: 'Yellow Fever', cvx: '25' },
        { name: 'Typhoid', cvx: '91' }
    ],

    // ─── DEFAULT SOCIAL HISTORY CATEGORIES ───────────────────────
    SOCIAL_HISTORY_CATEGORIES: [
        'Tobacco Use',
        'Alcohol Use',
        'Drug Use',
        'Exercise',
        'Diet',
        'Occupation',
        'Sexual Activity'
    ],

    // ─── QUICK-ADD CHIP CONFIGS ───────────────────────────────────
    // Simplified: each chip now uses primary field + notes
    // autoSave: true = POST immediately without user filling form
    CHIPS: {
        allergies: [
            { label: 'NKDA', autoSave: true, data: { allergenName: 'NKDA', notes: 'No known drug allergies' } },
            { label: 'Penicillin', data: { allergenName: 'Penicillin' } },
            { label: 'Sulfa', data: { allergenName: 'Sulfonamides' } },
            { label: 'Aspirin', data: { allergenName: 'Aspirin' } },
            { label: 'Latex', data: { allergenName: 'Latex' } },
            { label: 'Codeine', data: { allergenName: 'Codeine' } },
            { label: 'Iodine', data: { allergenName: 'Iodine' } }
        ],
        medications: [
            { label: 'Lisinopril 10mg', autoSave: true, data: { drugName: 'Lisinopril', notes: '10mg, oral, once daily, tablet' } },
            { label: 'Metformin 500mg', autoSave: true, data: { drugName: 'Metformin', notes: '500mg, oral, twice daily, tablet' } },
            { label: 'Atorvastatin 20mg', autoSave: true, data: { drugName: 'Atorvastatin', notes: '20mg, oral, once daily, tablet' } },
            { label: 'Amlodipine 5mg', autoSave: true, data: { drugName: 'Amlodipine', notes: '5mg, oral, once daily, tablet' } },
            { label: 'Omeprazole 20mg', autoSave: true, data: { drugName: 'Omeprazole', notes: '20mg, oral, once daily, capsule' } },
            { label: 'Metoprolol 25mg', autoSave: true, data: { drugName: 'Metoprolol Tartrate', notes: '25mg, oral, twice daily, tablet' } }
        ],
        problems: [
            { label: 'HTN', autoSave: true, data: { description: 'Essential hypertension', notes: 'ICD-10: I10' } },
            { label: 'Type 2 DM', autoSave: true, data: { description: 'Type 2 diabetes mellitus', notes: 'ICD-10: E11.9' } },
            { label: 'Hyperlipidemia', autoSave: true, data: { description: 'Hyperlipidemia, unspecified', notes: 'ICD-10: E78.5' } },
            { label: 'Obesity', autoSave: true, data: { description: 'Obesity, unspecified', notes: 'ICD-10: E66.9' } },
            { label: 'GERD', autoSave: true, data: { description: 'Gastro-esophageal reflux disease', notes: 'ICD-10: K21.0' } },
            { label: 'Anxiety', autoSave: true, data: { description: 'Anxiety disorder, unspecified', notes: 'ICD-10: F41.9' } }
        ],
        familyHx: [
            { label: 'Diabetes', data: { condition: 'Diabetes' } },
            { label: 'Heart Disease', data: { condition: 'Heart Disease' } },
            { label: 'Cancer', data: { condition: 'Cancer' } },
            { label: 'Hypertension', data: { condition: 'Hypertension' } },
            { label: 'Stroke', data: { condition: 'Stroke' } },
            { label: 'Depression', data: { condition: 'Depression' } }
        ],
        socialHx: [
            { label: 'Non-Smoker', autoSave: true, data: { category: 'Tobacco Use', notes: 'Never smoked' } },
            { label: 'Former Smoker', autoSave: true, data: { category: 'Tobacco Use', notes: 'Former smoker' } },
            { label: 'Social Drinker', autoSave: true, data: { category: 'Alcohol Use', notes: 'Social drinker' } },
            { label: 'No Alcohol', autoSave: true, data: { category: 'Alcohol Use', notes: 'Does not drink alcohol' } },
            { label: 'No Drugs', autoSave: true, data: { category: 'Drug Use', notes: 'Denies recreational drug use' } },
            { label: 'Exercises', autoSave: true, data: { category: 'Exercise', notes: 'Exercises regularly' } }
        ],
        immunizations: [
            { label: 'Flu Shot Today', autoSave: true, data: { vaccineName: 'Influenza (Flu)' } },
            { label: 'COVID Booster', autoSave: true, data: { vaccineName: 'COVID-19 Booster' } },
            { label: 'Tdap Today', autoSave: true, data: { vaccineName: 'Tdap' } },
            { label: 'Pneumovax', autoSave: true, data: { vaccineName: 'Pneumococcal (PPSV23)' } }
        ]
    },

    // ─── HELPER: Search allergens ─────────────────────────────────
    searchAllergens(query) {
        const q = query.toLowerCase();
        return this.ALLERGENS.filter(a => a.name.toLowerCase().includes(q));
    },

    // ─── HELPER: Search vaccines ──────────────────────────────────
    searchVaccines(query) {
        const q = query.toLowerCase();
        return this.VACCINES.filter(v => v.name.toLowerCase().includes(q));
    },

    // ─── HELPER: Get allergen type from name ──────────────────────
    getAllergenType(name) {
        const found = this.ALLERGENS.find(a => a.name.toLowerCase() === name.toLowerCase());
        return found ? found.type : 0; // Default to Drug
    }
};

// Export for global access
window.HistoryPresets = HistoryPresets;
