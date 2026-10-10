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
- Same behaviour as the web phase 2: profile seeded once from the web seed, master CV versions and restore, local text
  extraction, `cv-import` with the payload preview, `CvImportVerifier` flags, review before saving. (Changed on
  2026-10-10 at the owner's request: the phone app starts with an empty profile, since other testers can install it.)
- Files come from Android's document picker and are read in memory (10 MB limit), never copied into the app.
- Acceptance on the phone (owner): import the real CV (PDF or DOCX), review the flags, save, edit, save again and restore
  version 1.

### Phase A3 — Offers
The eight sources with their rate limits, manual entry, filters, analysis with Gemini and ranking.
- Same sources, user agent and minimum intervals as the web app (owner's choice, 2026-10-06); each app keeps its own run
  history and the Sources tab says so.
- Same ingestion, dedup, filters, eligibility hint, compensation rules, analysis verifier and ranking as the web app;
  tables identical to the web ones (checked against the web model snapshot).
- Original postings open in the phone's browser.
- Acceptance on the phone (owner): "Buscar" with the default sources, a manual entry (ATS URL and pasted text) and an
  analysis of one offer with the real CV.

### Phase A4 — Tailored CV and cover letter
Generation with traceability, approval, DOCX/PDF export shared through Android's share sheet. QuestPDF on Android is
verified before relying on it; if it does not work, the owner is asked.
- QuestPDF has no Android binaries; the owner chose PDFsharp (2026-10-06). Same PDF rules as the web app (Letter, one
  column, Lato embedded, real text with spaces, no tables or images), with tests for wrapping, bullets and page breaks.
- Same generation, traceability validator, flags, approval and DOCX as the web app; tables identical to the web ones.
- Export only after approval, through Android's share sheet; the file is not kept.
- Acceptance on the phone (owner): generate a CV and a letter for an offer, approve them, share the PDF and DOCX (e.g. to
  Files or Drive) and open them.

### Phase A5 — Pipeline, reminders and dashboard
Stages, timeline, follow-up rules, `.ics`, Panel. Whether to use Android notifications is asked first (the web app runs
nothing unattended).
- Owner's choices (2026-10-07): local notifications; exact alarms when the owner allows them, inexact otherwise; generic
  text on the lock screen; follow-ups at 09:00 on the due day and interviews 1 hour before (editable, with an on/off
  switch in Configuración); stages as one list per stage with a "Mover a…" menu instead of the drag-and-drop Kanban.
- Same stages, timeline, follow-up rule, metrics, sent-document snapshots and `follow-up-email` drafts as the web app;
  tables identical to the web ones. Gmail drafts and Google Calendar stay for phase A8.
- `.ics`, sent documents and follow-up drafts leave the phone through the share sheet (or the clipboard); nothing is sent
  by the app.
- Acceptance on the phone (owner): add an offer to the pipeline, move it, schedule an interview, turn notifications on and
  receive one (and again after restarting the phone), share the `.ics` to a calendar app, and a real follow-up draft.

### Phase A6 — Companies and outreach
Company research with cited sources; outreach drafts copied or shared, never sent.
- Same company profiles (owner-chosen URLs read with Gemini's URL context, pasted texts), fact verifier, outreach in three
  tones with only verified facts, history, "Ya lo envié" into the timeline, and LinkedIn review on pasted texts, as the web
  app; tables identical to the web ones.
- Drafts and proposals are copied (Android clipboard) or shared (share sheet); Gmail drafts stay for phase A8.
- Acceptance on the phone (owner): create a profile from an offer, add URLs and a pasted text, research it, share an
  outreach draft to LinkedIn or the mail app, mark it as sent, and copy a LinkedIn proposal.

### Phase A7 — Interviews and negotiation
Question bank, STAR stories, practice sessions with rubric feedback, offer comparator, negotiation script and roleplay.
- Ported unchanged from the web app (question quotes checked against the posting, STAR stories traced to the CV,
  practice rules enforced by the app, fixed rubrics with the score computed by the app, comparator only with the owner's
  rates and costs, script numbers checked); tables identical to the web ones.
- Phone layout: section chips instead of tabs, cards instead of the comparison and history tables, rubric as a list, the
  score chart's text enlarged for the phone.
- Acceptance on the phone (owner): a question bank and STAR stories for a real offer, a mock interview with feedback, an
  offer comparison, a negotiation script and a recruiter roleplay with the owner's key.

### Phase A8 — Google
Needs an Android OAuth client in Google Cloud; the current procedure is verified and given to the owner before coding.
- Verified on 2026-10-07 and approved by the owner: Google's authorization API for Android (Play services
  `AuthorizationClient`); custom URI schemes are no longer supported for Android clients and loopback redirects are
  deprecated for mobile apps. Android OAuth client (package `com.vidi.jobpilot` + SHA-1 of the signing keystore) in the
  same Google Cloud project; no client id or secret in the app; no tokens stored on the phone.
- Same scope as the web app (owner's choice): Gmail threads linked to applications, responses recorded, Gmail drafts
  (never sent), the "JobPilot" calendar for interviews, and the "Solo Calendar" option.
- Acceptance on the phone (owner): create the Android client, connect, link a thread, record a response, create a
  follow-up draft in Gmail, add an interview to the JobPilot calendar, and disconnect.

### Phase A9 — Mail brief ("Hoy")
The phase 9 brief on the phone, with the same rules and read-only Gmail access.
- Delivered on 2026-10-07: the web mail check (rules store, LinkedIn alert parser, `MailCheckService`, `MailChecks` and
  `MailWaiting` tables) ported unchanged; "Hoy" at the top of the Panel; "Reglas de correo" in Configuración; the generic
  rules seed embedded from the web app. Mail is read only on "Actualizar correo" (no background work on the phone).
- Phone layout: the update button on its own line, long subjects wrap, filter examples open with a tap instead of a
  tooltip; Configuración sections can be opened from links (`/configuracion?seccion=...`).
- Acceptance on the phone (owner): with Google connected with Gmail, update the mail, register a reply in an
  application, mark a card "Listo", open an alert role in Ofertas and edit the rules.

### Phase A10 — Export and import between PC and phone
One JSON file format exported and imported by both apps (the web app gets this feature too, confirmed again with the
owner when the phase starts). Not encrypted, by the owner's choice.
- Owner's choices (2026-10-07): import **combines** (no replace); everything except secrets, histories included; deletions
  are recorded and travel; edits without a date take the file's value; on the phone the file leaves through the share
  sheet and comes back through the file picker. Web scope: a new Configuración tab and `Features/Transfer/`, its
  registration, tests, docs, plus the deletion record in the existing delete actions and a `DeletedRecords` table.
- Delivered on 2026-10-07 in both apps (web code in `src/JobPilot.Web/Features/Transfer/`, ported to the phone):
  `TransferSchema` (all 24 tables), `TransferFile` (JSON, format version 1), `TransferMerger` (pure plan shown before
  applying), raw SQLite read/write, a database copy before combining, `DeletionLog` in the 11 delete actions of each app.
- Combining rules: rows matched by identity, not local ids (offer URL, posting of an application, company name, dates of
  events and records); keys and references translated; dated rows keep the latest change; undated edits take the file's
  value ("sent" outreach stays sent); a pasted offer description is never lost; application stage and reminder follow
  the combined timeline; colliding master CV versions are renumbered; mail checks only when newer than the device's last;
  a deletion removes rows not changed after it, and a row added again later is kept. Importing the same file twice
  changes nothing.
- Acceptance (owner): export on the PC, import on the phone; change and delete something on the phone, export, combine
  on the PC; check the summary, the pipeline, the deleted items and the backup.
