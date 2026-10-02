# JobPilot

A local, single-user assistant for a remote job search, built with **.NET 10, Blazor (Interactive Server)
and MudBlazor**, using the **Gemini API** as its only LLM.

JobPilot prepares, analyses and drafts; you approve. It never sends an application, e-mail or message on
your behalf. All data stays on your machine.

> **Status:** phase 0 (skeleton) of 8. See [`docs/PLAN.md`](docs/PLAN.md) for the roadmap and
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

Coming in phase 1: the Gemini API key will be read from
[user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) or an environment
variable, never from a committed file. Model names and parameters are set in configuration. Database
migrations will also be documented here.

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
