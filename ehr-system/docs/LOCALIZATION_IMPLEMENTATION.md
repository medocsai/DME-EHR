# Implement Multi-Language Localization System for IMEHR

## Goal
Add multi-language support to our EHR system. Users select their preferred language from: English, Turkmen (Türkmen dili), Russian (Русский), and optionally Uzbek (Oʻzbekcha). All are LTR languages — no RTL support needed.

## Key Architecture Decisions
- Use ENGLISH TEXT as translation keys (not abstract keys like "save_btn")
  - `t("Save Patient")` not `t("save_patient")`
  - If translation is missing, English shows automatically — no ugly broken keys
- Static JSON locale files loaded once on page load — NO API calls at runtime for translations
- Gemini is ONLY used as a developer/admin tool to batch-generate translations — NEVER called at runtime for UI strings
- Per-user language preference stored in User model
- Gemini clinical note generation (already exists) gets a language parameter added

## How Translation Works

### At Development Time (Rare — only when UI changes)
1. Developer builds new pages/features using `t("English text here")`
2. Admin opens a "Sync Translations" admin tool
3. Tool scans all t() calls → finds strings missing from translation JSON files
4. Missing strings sent to Gemini in a BATCH → Gemini returns translations
5. Admin reviews translations (especially medical terms) → saves to static JSON files
6. Done. Gemini not called again for those strings.

### At Runtime (Every Page Load — fast, free, no API calls)
1. User logs in → their PreferredLanguage is known ("en", "ru", "tk", "uz")
2. Browser loads the matching static JSON file (e.g., ru.json) — cached like CSS
3. `t("Save Patient")` → looks up in the loaded JSON → returns "Сохранить пациента"
4. If translation missing → returns the English string as fallback
5. Zero Gemini calls. Zero API calls. Just an in-memory dictionary lookup.

## Implementation Steps

### Step 1: Locale System (Frontend Core)
Create `wwwroot/js/core/LocaleService.js`:
- `loadLocale(lang)` — fetches `wwwroot/locales/{lang}.json`, caches in `window._locale`
- `t(englishText)` — if lang is "en" return as-is, otherwise lookup in `window._locale`, fallback to English
- Store selected language in `localStorage.preferredLanguage`
- Load locale on app startup BEFORE modules initialize
- Make `t()` available globally via `window.t`

Create static JSON locale files:
- `wwwroot/locales/en.json` — empty or `{}` (English is the key itself, no file needed)
- `wwwroot/locales/ru.json` — Russian translations
- `wwwroot/locales/tk.json` — Turkmen translations
- `wwwroot/locales/uz.json` — Uzbek translations (optional)

Format:
```json
{
  "Save Patient": "Сохранить пациента",
  "Sign Note": "Подписать запись",
  "Vitals": "Витальные показатели",
  "Cancel": "Отмена"
}
```

### Step 2: Language Switcher UI
- Add a globe icon dropdown in the navbar (top right, near user profile)
- Options show: English, Türkmen dili, Русский, Oʻzbekcha
- Display currently selected language name in the toggle
- On selection: save to backend via API, update localStorage, reload page

### Step 3: Database & Backend
- Add `PreferredLanguage` string field to User model (default "en")
- Add API endpoint `POST /api/users/language` to update user's preference
- Return `PreferredLanguage` in the user object on login so frontend knows which locale to load

### Step 4: Extract Strings from Existing UI
- Go through existing pages and wrap all hardcoded English text with `t()`
- Start with the most-used pages: Dashboard, Patients, Encounters, Clinical Notes, Billing
- Then GlobalBridge.js — toast messages, alerts, modal titles, button labels
- Then each module: DashboardModule.js, PatientModule.js, EncounterModule.js, BillingModule.js
- Then Razor views: for server-rendered text, use ASP.NET Core IViewLocalizer or render with JS after page load

### Step 5: Admin Translation Sync Tool
Create an admin-only page or button that:
1. Scans all JS files for `t("...")` patterns → extracts list of all English strings
2. Compares against existing ru.json, tk.json, uz.json
3. Identifies MISSING translations
4. Sends missing strings to Gemini in a batch with prompt:
   - "Translate these UI labels for a medical EHR system into [Russian/Turkmen/Uzbek]. Return JSON format. Use proper medical terminology."
5. Shows results for admin review/edit before saving
6. Saves approved translations back to the JSON files

This tool is used ONLY by developers/admins after building new features. It is NOT part of the normal user workflow.

### Step 6: Gemini Clinical Notes Language
- In GeminiService.cs, accept a language parameter
- Prepend language instruction to the note generation prompt:
  - Russian: "Напишите клиническую записку на русском языке"
  - Turkmen: "Kliniki belligi türkmen dilinde ýazyň"
  - Uzbek: "Klinik yozuvni o'zbek tilida yozing"
  - English: no extra instruction (default)
- Pass the current user's PreferredLanguage when generating notes

## Important Notes
- All 4 languages are LEFT-TO-RIGHT — no RTL layout changes needed
- Cyrillic text (Russian, Uzbek) can be WIDER than English — ensure buttons, table headers, and labels don't overflow. Test with Russian strings.
- Medical terminology in Turkmenistan is heavily Russian-based — Gemini translations need human review for medical terms
- The t() function must be available globally before any module initializes
- Do NOT add complex i18n frameworks — keep it simple with flat JSON + one helper function
- Do NOT refactor or restructure existing code beyond what's needed for localization
- Razor views: prefer wrapping text with JS t() on client side to keep one translation system (JSON files) rather than maintaining separate .resx files. But if server-side rendering is needed for SEO or performance, use .resx as well.
