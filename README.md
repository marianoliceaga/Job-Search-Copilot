# JobPilot

A local, single-user assistant for a remote job search, built with **.NET 10, Blazor (Interactive Server)
and MudBlazor**, using the **Gemini API** as its only LLM.

JobPilot prepares, analyses and drafts; you approve. It never sends an application, e-mail or message on
your behalf. All data stays on your machine.

> **Status:** phase 2 of 8 (profile and master CV import/editing). See [`docs/PLAN.md`](docs/PLAN.md) for the roadmap and
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
- `Gemini:*`: base URL, retries and timeouts.

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
