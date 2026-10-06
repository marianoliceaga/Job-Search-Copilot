# JobPilot for Android — Delivery plan

Approved by the owner on 2026-10-06. This is the reference for scope and acceptance criteria of the Android app.
The web app's plan ([`../../docs/PLAN.md`](../../docs/PLAN.md)) still defines what each feature does; this plan says how
it reaches the phone. Changes must be agreed with the owner and recorded in the decision log in
[`../CLAUDE.md`](../CLAUDE.md).

## Agreed decisions (2026-10-06)

| Topic | Decision |
|---|---|
| Technology | .NET 10 MAUI Blazor Hybrid (BlazorWebView) with MudBlazor, C#. |
| Location | `android/` at the repo root with its own solution, tests and docs. Existing files are not modified, except: short Android sections in `CLAUDE.md`, `README.md` and `docs/PLAN.md`, and the export/import feature of the web app (phase A10). |
| Reuse | `src/JobPilot.Domain` is referenced unchanged. Web-specific code (pages, Gemini client, Google, persistence, sources) is adapted inside `android/`. |
| Data | Independent SQLite database on the phone. Moved between PC and phone by hand with an export/import file implemented in both apps (JSON, not encrypted). |
| Scope | All web features, in phases with the owner's approval between them. |
| Distribution | Own APK for Android 8.0+ (API 26), application id `com.vidi.jobpilot`, signed with the owner's key; not Google Play. |
| CI | New workflow `.github/workflows/android.yml` (personal data check, signing material check, build with warnings as errors, tests, signed APK as an artifact from GitHub secrets on pushes). `ci.yml` is not touched. |
| Principles | Same as the web app: human in the loop, never invent, privacy, untrusted input, allowed sources only, verify instead of assuming. The app never sends messages (own `NoSendingTests` over `android/src`). |

## Architecture

```
android/
  JobPilot.Mobile.slnx                 also lists ../src/JobPilot.Domain
  Directory.Build.props                same settings as the repo (warnings as errors, code style)
  Directory.Packages.props             central package versions for android/
  src/JobPilot.Mobile/                 MAUI host (net10.0-android): MauiProgram, MainPage (BlazorWebView), Platforms/Android,
                                       icons, wwwroot/index.html; implementations of platform services
  src/JobPilot.Mobile.Core/            Razor class library (net10.0): Routes, layout, pages and services; no MAUI reference
                                       (platform services behind interfaces), so it is tested without the Android toolchain
  tests/JobPilot.Mobile.Tests/         xUnit v3 + bUnit
  scripts/check-no-signing-keys.sh     fails if a keystore or APK could be committed
```

## Phases and acceptance criteria

Each phase closes with: build with 0 warnings, tests green, personal data and signing checks, commit + push, CI green
and a summary for the owner. There is no emulator in the development container: the APK is built and the logic tested
there, and the owner checks the UI on the phone.

### Phase A0 — Skeleton
- `android/` with its own solution, MAUI Blazor Hybrid app (API 26+, `com.vidi.jobpilot`) referencing `JobPilot.Domain`,
  MudBlazor, navigation with the web app's sections and routes, each one showing the phase that implements it.
- Tests: navigation, one page per section, the core does not reference MAUI, `NoSendingTests` over `android/src`,
  manifest (network permissions only, no cloud backup, minimum API and id).
- `.github/workflows/android.yml`; docs in `android/` and short sections in the existing docs.

### Phase A1 — Persistence, Gemini and settings
SQLite on the phone, Gemini client with the key in Android's secure storage, connection test, call log with costs,
models per use case.
- EF Core + SQLite chosen by the owner (2026-10-06); migrations applied on start, with a visible error instead of a crash.
- The key is pasted in Configuración → Gemini, stored with `SecureStorage`, never shown, removable with confirmation.
- Same Gemini behaviour as the web app (structured output, bounded retry, resilience, connect timeout, one log row per
  request with cost), tests ported from the web app; prompts and defaults embedded from the web app's files.
- Acceptance on the phone (owner): save the key, "Probar conexión" succeeds, the call appears in Registro and is still there
  after closing and reopening the app.

### Phase A2 — Profile and master CV
Profile and master CV with versions; import of PDF, DOCX or Markdown chosen with the file picker.

### Phase A3 — Offers
The eight sources with their rate limits, manual entry, filters, analysis with Gemini and ranking.

### Phase A4 — Tailored CV and cover letter
Generation with traceability, approval, DOCX/PDF export shared through Android's share sheet. QuestPDF on Android is
verified before relying on it; if it does not work, the owner is asked.

### Phase A5 — Pipeline, reminders and dashboard
Stages, timeline, follow-up rules, `.ics`, Panel. Whether to use Android notifications is asked first (the web app runs
nothing unattended).

### Phase A6 — Companies and outreach
Company research with cited sources; outreach drafts copied or shared, never sent.

### Phase A7 — Interviews and negotiation
Question bank, STAR stories, practice sessions with rubric feedback, offer comparator, negotiation script and roleplay.

### Phase A8 — Google
Needs an Android OAuth client in Google Cloud; the current procedure is verified and given to the owner before coding.

### Phase A9 — Mail brief ("Hoy")
The phase 9 brief on the phone, with the same rules and read-only Gmail access.

### Phase A10 — Export and import between PC and phone
One JSON file format exported and imported by both apps (the web app gets this feature too, confirmed again with the
owner when the phase starts). Not encrypted, by the owner's choice.
