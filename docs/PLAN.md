# JobPilot — Delivery plan

Approved on 2026-10-02. This is the reference for scope and acceptance criteria per phase.
Changes to the plan must be agreed with the owner and recorded in the decision log in `CLAUDE.md`.

## Agreed decisions

| Topic | Decision |
|---|---|
| UI language | Spanish. Code, commits and technical docs in English. Generated application content (CV, cover letters, outreach) in English. |
| UI library | MudBlazor (MIT). |
| LLM | Gemini API, **paid tier** (free tier allows Google to use prompts to improve its products). Model per use case in configuration: `gemini-3.8-flash` for reasoning-heavy use cases, `gemini-3.5-flash-lite` for cheap extraction. No preview models by default. |
| Google integration (Gmail/Calendar) | Decided when phase 8 starts. |
| Job sources | Remotive, Remote OK, Himalayas, Jobicy (APIs); We Work Remotely RSS only after the owner confirms its terms; Greenhouse, Lever and Ashby public job boards per followed company; HN "Who is hiring?" optional. No LinkedIn, Indeed, Glassdoor or Wellfound. |
| DOCX / PDF | DocumentFormat.OpenXml (MIT) / QuestPDF (Community license). CV reading: PdfPig (Apache-2.0) and OpenXml; Markdown is read as-is (Markdig turned out unnecessary). |
| Repository | Treated as public: MIT license, fictional sample data, CI check against personal data. |
| CV import | PDF, DOCX and Markdown; text extracted locally, then structured by the LLM. |
| Name | JobPilot (`JobPilot.slnx`). |
| OS | Cross-platform; PDF embeds its own OFL font. |
| Currency | Manual exchange rates; contractor costs configurable. |
| Ranking defaults | Compensation 40%, technical fit 30%, eligibility/contract model 20%, red flags 10%. |
| Access | No login; Kestrel listens on loopback only. |
| Extras | GitHub Actions CI (build + tests + personal data check); bUnit for components with logic. |

## Architecture

Vertical slices plus a pure domain project:

```
src/
  JobPilot.Domain/        Entities and pure logic: scoring, dedup, USD normalization,
                          follow-up rules, traceability validator. No dependencies.
  JobPilot.Web/           Blazor Web App (Interactive Server) + MudBlazor
    Features/<Feature>/   Pages, use-case handlers, prompts and schemas per module
    Infrastructure/       EF Core/SQLite, Gemini client, job sources, exporters
    Prompts/<case>.vN.md  One versioned prompt per use case
tests/
  JobPilot.Domain.Tests/  xUnit
  JobPilot.Web.Tests/     Use cases with FakeLlmClient, parsers with recorded fixtures,
                          EF on in-memory SQLite, bUnit
```

Why: a local single-user app whose modules (5.1–5.9) are mostly independent. Slices keep each module
cohesive without Clean Architecture ceremony, and the logic that matters most (ranking, dedup,
"never invent") lives in a pure, easily tested domain. No MediatR (commercial license since 2025):
plain handler classes.

Gemini client: `ILlmClient` (`GenerateAsync<T>`; `ChatAsync` arrives with its first consumer in phase 7) implemented with a typed
`HttpClient` + `Microsoft.Extensions.Http.Resilience` (retry with jitter on 429/5xx honouring
`Retry-After`, timeout, circuit breaker). JSON schema generated from C# records with
`JsonSchemaExporter`, adapted to the subset Gemini accepts. Two-step validation (strict
deserialization + domain rules); one bounded retry, then a visible error. Every call logged in
`LlmCalls` (use case, model, prompt version, tokens, latency, cost from a configurable price
table, success/error).

Privacy and security: a "Qué se envía a Gemini" panel before every LLM action; external text is
wrapped as `<untrusted_data>` with explicit instructions not to follow it, and output is constrained
by schema. Loopback only. DB, exports and secrets are git-ignored.

## Phases and acceptance criteria

Every phase closes with: `dotnet build` with 0 warnings, tests green, commit + push, and a summary.

### Phase 0 — Skeleton
.NET 10 solution, `Directory.Build.props` (nullable, warnings as errors), central package management,
`.editorconfig`, `.gitignore`, MudBlazor layout with Spanish navigation and placeholder sections,
`CLAUDE.md`, README, LICENSE, this plan, CI (build, tests, personal data check).

- `dotnet build` 0 warnings; `dotnet test` green.
- `dotnet run` serves on `localhost` only.
- CI green.

### Phase 1 — Domain, persistence, Gemini
EF Core + SQLite + migrations; base entities (`Profile`, `LlmUseCaseSetting` for UI overrides, `LlmCallRecord`). Gemini client
with structured output, versioned prompt loader, cost calculation. Settings page: model per use case,
API key status (never displayed), "Probar conexión", LLM call log. Confirm the current REST syntax for
structured output.

- Tests with a fake `HttpMessageHandler`: success, 429 then success, invalid JSON → retry → error,
  timeout, cancellation, cost.
- With the owner's key, "Probar conexión" works and the call is logged.

### Phase 2 — Profile and master CV (5.1)
Editable profile (seeded on first run from a seed file with the initial user context, not from code).
Import PDF/DOCX/MD → local extraction → LLM structuring → review screen → save. Every element has a
stable ID (used for traceability in phase 4). Master CV is versioned on change.

- Extraction tests with fictional CVs in all three formats; import test with the fake LLM.
- The owner imports their real CV and can edit everything.

### Phase 3 — Ingestion and analysis (5.2, 5.3)
`IJobSource` with the approved sources, per-source rate policy (e.g. Remotive ≤ 4/day, Jobicy ≤ 1/hour),
attribution and canonical link always visible. Manual entry: URL (downloaded only for ATS/allowlisted
sites) or pasted text → LLM extraction. "Buscar" button with per-source progress and cancellation;
dedup by canonical URL and normalized company + title; filters; followed companies. Analysis covers
everything in 5.3, marking uncertainty explicitly; compensation requires a quote from the posting,
otherwise "no informada". Configurable ranking weights.

- Parser tests per source with recorded fixtures; dedup and ranking tests.
- Test: compensation without a quote becomes "no informada".
- A real search lists offers with their original link; analysing one shows every field.
- Note: HN "Who is hiring?" (optional) was not implemented; it can be added later as another source.

### Phase 4 — Tailored CV and cover letter (5.4)
Each generated bullet carries the IDs of the master CV elements it comes from. A deterministic validator
rejects bullets without a source and numbers, dates or technologies not present in those elements or in
the skills list. One retry, then the bullet is shown flagged (never silently accepted). Diff view against
the master CV, editing and approval. Export to DOCX (OpenXml) and PDF (QuestPDF): single column, embedded
font.

- Test: an invented metric is rejected by the validator.
- DOCX test: no tables or images in the XML. PDF test: PdfPig extracts the text in reading order.
- Notes: exports use US Letter; the font bundled with QuestPDF (Lato, OFL) is embedded; export is enabled after approval.

### Phase 5 — Pipeline, reminders and dashboard (5.8, 5.10)
Drag-and-drop Kanban; per application: timeline, notes, contacts, which CV and cover letter versions were
sent. Reminders computed on load (no background process) and follow-up drafts. `.ics` export with
Ical.Net (MIT). Dashboard with metrics including LLM usage and cost.

- Every status change appears in the timeline.
- Tests for follow-up rules and metric calculations; the `.ics` round-trips without errors.
- Notes: follow-up drafts are a new LLM use case (`follow-up-email`); sent documents keep a snapshot of what was sent;
  defaults 7/3 days and 2 follow-ups per stage, editable in the UI.

### Phase 6 — Companies and outreach (5.5, 5.7)
Company profile built with Gemini's URL context tool from public URLs the owner chooses, plus pasted texts; every fact
stores its source URL and a quote; facts whose URL Gemini did not report as read (or whose quote is not in the pasted
text, or whose numbers are not in the quote) are marked "no verificado". Outreach drafts with tone and length variants;
LinkedIn headline/About review. Copy to clipboard only; the app never sends anything.

- Test for the "no verificado" rule.
- No code path sends messages.
- Change agreed with the owner on 2026-10-02: Grounding with Google Search was replaced by URL context because its terms
  forbid storing, analysing or modifying grounded results (see the decision log in `CLAUDE.md`).

### Phase 7 — Interviews and negotiation (5.6, 5.9)
Question bank per offer; STAR stories traceable to the master CV. Mock interview chat (one question at a
time, follow-ups, rubric feedback and a model answer at the end). History and score evolution chart.
Offer comparator normalized to annual USD (owner-provided exchange rates and contractor costs).
Negotiation script and recruiter-role chat simulation.

- Conversation and persistence tests with the fake LLM.
- Rubric validation and comparator calculation tests.
- Notes: the simulators share one engine (`PracticeSession`, `ILlmClient.ChatAsync`); the app enforces one question per
  message and the follow-up limit, and computes the overall score from the rubric; the recruiter's room to move is a
  practice parameter chosen by the owner.

### Phase 8 — Google integration
Local-app OAuth with minimal scopes. Gmail: read threads linked to applications and create drafts, never send. Calendar:
create interview events after explicit confirmation.

- Confirmed by the owner on 2026-10-03: scopes `gmail.readonly` + `gmail.compose` (no drafts-only scope exists; the app
  never sends, enforced by tests) and `calendar.app.created` (a dedicated "JobPilot" calendar, no access to the primary one).
- Tests with a fake Google server: OAuth state, code exchange, refresh, revocation, granular consent, Gmail search/read,
  draft MIME (reply headers, no header injection), calendar creation and events.

### Phase 9 — Mail inbox and daily brief (from JobSearchDesk)
Port the job-search parts of the owner's JobSearchDesk ("LinkedMail Job Finder") into JobPilot: rule-based, no LLM, on
demand, nothing sent. Agreed with the owner on 2026-10-06: the brief lives only inside JobPilot (no e-mail), mail is
read only when the owner asks (no scheduler), only job-search mail is classified (no banks, bills or security notices),
and the owner unenrols from Advanced Protection to use Gmail.

- Secrets removed from `appsettings.json`; the personal data check rejects `GOCSPX-` client secrets and non-empty
  `ApiKey`/`ClientSecret`/`ClientId`/token values in JSON files.
- Domain: LinkedIn job-alert parser (the e-mail only, linkedin.com is never fetched), duplicate merging, role scoring
  (Strong/Maybe/Weak with the rules that fired, on-site and US-only filters), thread classification into LinkedIn
  alerts / Waiting on you / Filtered out with the invariant that the buckets add up to the threads read.
- "Actualizar correo" on the Panel reads Gmail with the existing `gmail.readonly` scope since the last check (at most
  4 days back, 24 h the first time). Only facts are stored (thread id, sender, subject, date, bucket, rule); bodies are
  never stored or sent to Gemini.
- Alert roles feed Ofertas as the source "LinkedIn (alertas por mail)" with dedup, attribution and the canonical link.
- Waiting-on-you threads are matched to applications by company; one click links the thread or records a response.
- Panel "Hoy": replies needed, new strong roles, interviews in the next 48 h.
- Rules are a settings document seeded from a generic file; personal word lists stay local.
- README: publishing the OAuth app "In production" (unverified) avoids the 7-day refresh token expiry of "Testing".
- Tests with fictional alert, recruiter and invite e-mails; `NoSendingTests` unchanged.

## Android app

Approved on 2026-10-06: an Android version in `android/` (.NET MAUI Blazor Hybrid, Android 8.0+, own signed APK) with
all features, delivered in phases A0–A10 with the owner's approval between them. It references `JobPilot.Domain`
unchanged and keeps its own data; phase A10 adds an export/import file to both apps. Plan and acceptance criteria:
[`android/docs/PLAN.md`](../android/docs/PLAN.md).

Phase A10 in the web app (confirmed by the owner on 2026-10-07): Configuración → Exportar / Importar in
`src/JobPilot.Web/Features/Transfer/`, a `DeletedRecords` table written by every delete action, and "Combinar" imports
(details in `android/docs/PLAN.md`).
