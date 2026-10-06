# JobPilot

A local, single-user assistant for a remote job search, built with **.NET 10, Blazor (Interactive Server)
and MudBlazor**, using the **Gemini API** as its only LLM.

JobPilot prepares, analyses and drafts; you approve. It never sends an application, e-mail or message on
your behalf. All data stays on your machine.

> **Status:** all 8 phases delivered (Google integration included). Pending: checks with your own Gemini key and Google account. See [`docs/PLAN.md`](docs/PLAN.md) for the roadmap and
> acceptance criteria per phase. The UI is in Spanish.

## Planned features

- **Master CV**: import a PDF, DOCX or Markdown CV into a structured, editable model. It is the single
  source of truth.
- **Job ingestion** from sources whose terms allow it (public APIs, RSS feeds and public ATS job
  boards), plus manual entry. No LinkedIn scraping.
- **Analysis and ranking** of each offer (fit score, requirements met or missing, compensation only when
  published, contract model, eligibility, red flags) with configurable weights.
- **Tailored CV and cover letter** where every bullet traces back to the master CV, exported to
  ATS-friendly DOCX and PDF.
- **Pipeline** Kanban, follow-up reminders and `.ics` export.
- **Company research** with cited sources, **outreach drafts**, **interview practice** and a
  **negotiation** helper.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100 or later)
- A Gemini API key (from phase 1). A paid-tier key is recommended: on the free tier Google may use
  your prompts, which contain your CV, to improve its products.

## Run

```bash
dotnet run --project src/JobPilot.Web
```

Then open <http://localhost:5080>. The app listens on the loopback interface only. Change the port with
`JobPilot:Port` in `appsettings.json` or the `JobPilot__Port` environment variable.

## Test

```bash
dotnet build JobPilot.slnx
dotnet test --solution JobPilot.slnx
```

Warnings are treated as errors. Tests use xUnit v3 on Microsoft.Testing.Platform (configured in
`global.json`).

## Configuration

### Gemini API key

Store it with [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (outside the
repository), then restart the app:

```bash
dotnet user-secrets set "Gemini:ApiKey" "<your key>" --project src/JobPilot.Web
```

Alternatively set the `GEMINI_API_KEY` (or `Gemini__ApiKey`) environment variable. The key is sent in an HTTP
header and is never shown in the UI or written to logs. Check it in **Configuración → Conexión con Gemini →
Probar conexión**.

### Models, parameters and prices

`src/JobPilot.Web/appsettings.json`:

- `Llm:DefaultModel` and `Llm:UseCases:<use-case>` (model, thinking level, temperature, max output tokens).
  They can be overridden from **Configuración → Modelos por caso de uso** (stored in the local database).
- `Llm:Pricing:<model>` in USD per million tokens, used for cost estimates. Update it when Google changes its
  prices; models without a price show "sin precio configurado".
- `Gemini:*`: base URL, retries and timeouts. `ConnectTimeoutSeconds` (default 10) limits how long opening the connection
  may take: if your network cannot reach Google (broken IPv6, firewall, VPN, proxy), the action fails with
  "No se pudo conectar con Gemini" after that time instead of waiting for Gemini's answer timeout (120 s per attempt).

### Local data

The SQLite database lives in `%LocalAppData%\JobPilot` (Windows), `~/.local/share/JobPilot` (Linux) or
`~/Library/Application Support/JobPilot` (macOS). Change it with `JobPilot:DataDirectory`.
Migrations are applied automatically on startup. To create a new one (developers):

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/JobPilot.Web --output-dir Infrastructure/Persistence/Migrations
```

### Profile and master CV

**Perfil y CV maestro → Perfil** starts with the values in `src/JobPilot.Web/Seed/profile.seed.json` and is
edited from the app. **CV maestro → Elegir archivo** imports a PDF, DOCX or Markdown CV: the text is extracted
locally, sent to Gemini only after you confirm (the full prompt is shown first), and comes back as a draft.
Values that cannot be found in your document are flagged; you review and edit everything before saving. Every
save creates a version you can restore. Your CV file is not stored; only the structured CV, in the local
database.

### Job offers

**Ofertas → Buscar** reads the enabled sources (Remotive, Remote OK, Himalayas, Jobicy, We Work Remotely and the
public Greenhouse, Lever and Ashby boards of followed companies). Only offers matching your keywords are kept;
duplicates across sources are merged and every offer keeps its original link and source. Each source is queried at
most as often as its terms allow. **Agregar oferta** imports an ATS URL through its public API or extracts the
fields of a pasted text. Open an offer and use **Analizar con Gemini** for the fit score, requirements, missing
keywords, compensation (only when published), contract model, eligibility from your country and red flags.
Ranking weights, the compensation floor/target and exchange rates are in **Ofertas → Preferencias**.

### Tailored CV and cover letter

On an offer page, **Postulación** generates a CV variant and a cover letter from your master CV. Every text cites the
master CV elements it comes from; numbers and technologies that cannot be traced to them are flagged (the model gets
one retry with the reasons first). Company, titles and dates always come from the master CV. Review the differences,
edit, and approve; flagged texts must be fixed, removed or explicitly confirmed by you. After approval you can download
ATS-friendly **DOCX** (one column, no tables or images) and **PDF** (embedded font, selectable text) files.

### Pipeline and reminders

On an offer page, **Agregar al pipeline** adds it to the **Pipeline** Kanban (Detectada, Interesa, Postulada, Entrevista,
Oferta, Rechazada, Descartada). Drag cards between columns or use their menu; every change is recorded in the application's
timeline, together with notes, contacts, interviews and which approved CV and cover letter you sent (a copy of what was
sent is kept and can be downloaded again). Follow-up reminders are computed when you open the app (by default 7 days
without news after applying, 3 after an interview, at most 2 follow-ups per stage; editable in **Reglas de seguimiento**),
or on a date you choose. **Follow-up** drafts a short e-mail with Gemini from your CV and the application's facts; you
review it, copy it and send it yourself, then mark it as sent. **Exportar calendario (.ics)** downloads upcoming interviews
and reminders for any calendar app.

### Company profiles

**Empresas** keeps a profile per company, built only from sources you choose: public URLs (Gemini reads them with its URL
context tool) and texts you paste. Every fact keeps its source URL and a verbatim quote. A fact is **verificado** when
Gemini reports reading its URL or its quote is in your pasted text; otherwise it is shown as **no verificado** with the
reason (including numbers that are not in the quote). Grounding with Google Search is not used: its terms do not allow
storing or annotating its results. The profile links to the company's offers.

### Outreach

**Outreach → Mensajes** drafts a LinkedIn invitation note (200 characters by default, `Outreach:ConnectionNoteMaxLength`),
a LinkedIn message or an e-mail for a recruiter or hiring manager, in three tones. Claims about you must come from your
master CV and claims about the company from verified facts or the posting; anything else is flagged. Copy the text, send
it yourself and mark it as sent (it is added to the offer's timeline). **Perfil de LinkedIn** reviews the headline and
About you paste and proposes versions that cite your CV. JobPilot never sends messages and never accesses LinkedIn.

### Interviews

**Entrevistas → Preparación**: for an offer, a bank of likely questions (each with the sentence of the posting that
motivates it; quotes not found in the posting are flagged) and STAR stories built only from your master CV (every part
cites its CV elements; untraceable numbers or technologies are flagged and can be edited or confirmed).
**Simulacros**: a mock interview in English with Gemini as the interviewer, one question per message and a bounded number
of follow-ups (rules enforced by the app). At the end, rubric feedback (structure, relevance, evidence, impact,
communication; overall score computed by the app) and model answers from your CV. The score history is charted.

### Negotiation

**Negociación → Comparador**: enter the offers you received (base per year, month or hour, bonus, your own valuation of
benefits and equity, contract model). They are normalized to annual USD with your exchange rates and your contractor
costs; nothing is estimated. **Guion**: a negotiation script from your numbers and your CV, with numbers not found in
them flagged. **Simulacro**: a roleplay with Gemini as the recruiter, starting from your offer, with rubric feedback.

### Dashboard

**Panel** shows offers detected, applications, response rate, average time to the first response and per stage, pending
follow-ups, upcoming interviews and the estimated LLM cost by use case.

### Google (optional)

**Configuración → Google** explains the one-time setup: a Google Cloud project with Gmail API and Google Calendar API
enabled, an OAuth consent screen (External, Testing, your account as test user) and an OAuth client of type
**Web application** with the redirect URI `http://localhost:5080/google/callback`. Store its id and secret with
user-secrets (`Google:ClientId`, `Google:ClientSecret`) and restart. JobPilot asks for three scopes:

- `gmail.readonly`: search and read threads that you link to an application (only the thread id and subject are stored;
  messages are read on demand and never sent to Gemini).
- `gmail.compose`: create **drafts** of follow-ups and e-mail outreach, optionally as a reply in a linked thread. Google
  describes this scope as "manage drafts and send emails"; JobPilot never sends (a test fails if any send call appears).
- `calendar.app.created`: create a separate "JobPilot" calendar and add interviews to it, after you confirm each one.
  JobPilot cannot see or change your primary calendar.

Choose **Solo Calendar** before connecting to ask only for `calendar.app.created`. Use it when Gmail access is blocked:
Google's Advanced Protection Program only lets verified third-party apps read Gmail, and JobPilot uses your own
unverified OAuth client, so Google answers "Error 400: policy_enforced". Without Gmail, copy drafts with **Copiar** and
record replies by hand. **Agregar Gmail** reconnects with all three scopes later.

Every action shows exactly what will be sent to Google first. The token is stored encrypted in your data folder. With
the app in "Testing" status Google expires the permission after 7 days; JobPilot asks you to reconnect.
**Desconectar y revocar** revokes the permission at Google and deletes the local token.

### LLM call log

Every request sent to Gemini (including retries, failures and cancellations) is recorded with its use case,
model, prompt version, tokens, latency and estimated cost: **Configuración → Registro de llamadas**.

## Privacy

- Your database, CVs, exports and secrets are stored outside the repository and are git-ignored.
- `scripts/check-no-personal-data.sh` runs in CI and fails if a database, a secret, a document outside
  the test fixtures, an API key or a real e-mail address is about to be committed. To run it before
  every commit, and to add your own denylist of personal strings in a git-ignored `.pii-denylist` file
  (one per line):

  ```bash
  git config core.hooksPath .githooks
  ```

- Before every Gemini call, the UI will show what is being sent.

## Job data sources

Job listings belong to their publishers. JobPilot shows the source and the original link for every
listing and respects each provider's terms and rate limits (for example Remotive, Remote OK, Himalayas
and Jobicy require attribution and a link back).

## License

[MIT](LICENSE)
