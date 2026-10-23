# CLAUDE.md — JobSearch Copilot for Android

Guidance for working on the Android app in `android/`. The repo-wide rules in [`../CLAUDE.md`](../CLAUDE.md) apply
(principles, working agreement, conventions, languages); this file adds what is specific to Android. Scope and
acceptance criteria per phase: [`docs/PLAN.md`](docs/PLAN.md).

## Rules specific to the Android port

- **Do not modify existing files outside `android/`**, except the ones the owner allowed: short Android sections in
  `CLAUDE.md`, `README.md`, `docs/PLAN.md`, the new `.github/workflows/android.yml`, the owner's
  `.github/workflows/android-release.yml` (Google Play), and the web app's export/import
  feature in phase A10 (scope confirmed by the owner on 2026-10-07: `Features/Transfer/`, its registration and tab, tests,
  and the deletion record in the web delete actions with its `DeletedRecords` table). `src/JobPilot.Domain` is referenced,
  never changed.
- **Ask, don't assume.** The owner asked explicitly: any doubt (behaviour, platform API, library support) goes to the
  owner before it becomes code.
- One phase at a time with the owner's approval in between (`docs/PLAN.md`).
- The app never sends messages: `tests/JobPilot.Mobile.Tests/NoSendingTests.cs` scans `android/src` (the web app's test
  only scans the repo's `src/`). Sharing goes through Android's share sheet, where the owner sends it themselves.
- Secrets (the Gemini key) go to Android's secure storage, never to files, preferences or the repo. There is no Google token
  to store: Android's authorization API hands out a short-lived access token each session (kept in memory only). The signing
  keystore never enters the repo (`scripts/check-no-signing-keys.sh`, `.gitignore`).
- The only thing that runs without the owner opening the app is local notifications (agreed for A5): alarms scheduled
  from the pipeline, restored after a reboot. They never use the network, Gemini or any account.

## Commands

```bash
# EF Core migrations of the phone database (dotnet-ef is the repo's local tool: dotnet tool restore)
dotnet ef migrations add <Name> --project android/src/JobPilot.Mobile.Core --startup-project android/src/JobPilot.Mobile.Core \
  --output-dir Infrastructure/Persistence/Migrations

# One-time setup (container or PC)
dotnet workload install maui-android
dotnet build android/src/JobPilot.Mobile -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory=$HOME/android-sdk -p:AcceptAndroidSDKLicenses=True

dotnet build android/JobPilot.Mobile.slnx -p:AndroidSdkDirectory=$HOME/android-sdk   # warnings are errors; builds the APK
dotnet test --solution android/JobPilot.Mobile.slnx
bash scripts/check-no-personal-data.sh && bash android/scripts/check-no-signing-keys.sh
```

## Layout

```
JobPilot.Mobile.slnx         solution (includes ../src/JobPilot.Domain)
Directory.Build.props        own copy of the repo settings without TargetFramework (MSBuild stops at the nearest file)
Directory.Packages.props     central package versions for android/ (the root file is not inherited either)
src/JobPilot.Mobile/         MAUI host, net10.0-android: MauiProgram (DI: Core services, MudBlazor, BlazorWebView, data folder
                             = FileSystem.AppDataDirectory), Services/ (MauiSecretStore over SecureStorage,
                             MauiFilePickerService over FilePicker with Android MIME types, MauiShareService over Share and
                             Clipboard with files in cache/sharing-root), Platforms/Android/Notifications
                             (AndroidNotificationScheduler over AlarmManager, ReminderReceiver shows a notification,
                             RescheduleReceiver restores alarms after boot/update), Platforms/Android/Google
                             (AndroidGoogleAuthorizer over Play services AuthorizationClient, AndroidAppSigningInfo over
                             PackageManager signing info), MainActivity (single top, opens
                             the page of a tapped notification, receives Google's consent result), App, MainPage
                             (BlazorWebView → Core Routes), Platforms/Android (MainActivity, MainApplication, AndroidManifest),
                             Platforms/Android/Resources/xml (FileProvider limited to cache/sharing-root), Resources (icon,
                             splash), wwwroot/index.html + app.css (safe areas, error UI, payload preview)
src/JobPilot.Mobile.Core/    Razor class library, net10.0:
  MobileServiceCollectionExtensions  AddJobPilotMobile(configuration, paths): the app's own services
  Routes.razor               waits for AppInitializer (data folder + migrations), shows an error instead of crashing
  Layout/, Shared/, Pages/   MainLayout (temporary drawer), NavMenu, AppSections (Android phase per section),
                             PendingSection, LlmPayloadPreview, LlmFormat, placeholder pages
  Infrastructure/            MobilePaths, MobileConfiguration (embedded web appsettings.json), AppInitializer
    Persistence/             MobileDbContext (web table names and mappings, added per phase), migrations, SettingsDocumentStore,
                             DomainJson, design-time factory for dotnet-ef
    Llm/                     port of the web client: GeminiLlmClient (key from GeminiKeyStore), LlmJson, contracts, options,
                             PromptLibrary.LoadEmbedded (web prompts), LlmSettingsStore, DbLlmCallLog, LlmUseCases (implemented)
    Secrets/                 ISecretStore (host implements it), GeminiKeyStore
    Files/                   IFilePickerService and IShareService (host implements them), PickedFile, PickResult
  Features/Settings/         SettingsPage (/configuracion: Gemini / Modelos / Registro), GeminiConnectionPanel (key entry,
                             connection test), UseCaseModelsPanel, LlmCallLogPanel, ConnectionTestHandler
  Features/Profile/          port of the web slice: ProfilePage (/perfil: Perfil / CV maestro), ProfileForm, ProfileStore
                             (starts empty, no seed), MasterCvStore (versions), MasterCvPanel, MasterCvEditor,
                             CvImportPanel (Android picker), Import/ (CvTextExtractor with PdfPig/Open XML, ImportedCv,
                             CvImportHandler); Shared/ PartialDateField, StringListEditor
  Features/Jobs/             port of the web slice: JobsPage (/ofertas?seccion=ofertas|fuentes|preferencias), JobDetailPage
                             (/ofertas/{id}: Empresa, Outreach and Pipeline buttons, tailoring), SourcesPanel
                             (cards instead of tables), Sources/ (8 IJobSource providers, web settings and intervals),
                             JobIngestion, JobSearchService (no search without keywords), FollowedCompanyStore and
                             JobPreferencesStore (both start empty), ManualJobImport, JobListService, Analysis/
                             (job-analysis, AnalysisVerifier)
  Infrastructure/Text/       HtmlText (AngleSharp): third-party HTML → plain text
  Features/Tailoring/        port of the web slice: TailoringSection (offer page), TailoredDocumentPage (share instead of
                             download), editors, TailoringHandler, TailoringContext, TailoredDocumentStore, TechnologyCatalog
                             (embedded web seed), Export/ (DocumentContent, DocxWriter as in the web app, PdfWriter on PDFsharp
                             with its own line layout, DocumentExporter)
  Fonts/                     Lato Regular/Bold (OFL.txt), embedded in exported PDFs
  Features/Pipeline/         port of the web slice: PipelinePage (/pipeline: stage chips + one list, "Mover a" menu),
                             ApplicationPage (/pipeline/{id}: section chips; share instead of download, no Google parts),
                             PipelineButton (offer page), ApplicationStore, PipelineSettingsStore, CalendarExport (Ical.Net),
                             FollowUpHandler + FollowUpPanel (copy or share the draft), PipelineFormat; ReminderNotifications
                             (settings, planner, sync after every change), NotificationsPanel (Configuración → Avisos)
  Features/Dashboard/        DashboardPage (/, lists instead of tables), DashboardService (port), FirstStepsPanel (key, profile,
                             CV and import until they are in place)
  Features/Companies/        port of the web slice: CompaniesPage (/empresas, cards instead of a table), CompanyPage
                             (/empresas/{id}), CompanyButton (offer page), CompanyStore, CompanyResearchHandler (URL context +
                             CompanyFactVerifier), CompanyFormat
  Features/Interviews/       port of the web slice: InterviewsPage (/entrevistas, section chips), PracticeSessionPage
                             (/practica/{id}, chat; rubric as a list), InterviewPrep (question bank, STAR stories), PracticeHandler
                             + PracticeStore, ScoreChart (inline SVG, larger text on the phone), StarStoryCard, InterviewFormat
  Features/Negotiation/      port of the web slice: NegotiationPage (/negociacion, section chips, comparison as cards), OfferStore,
                             NegotiationScriptHandler
  Features/Outreach/         port of the web slice: OutreachPage (/outreach?oferta=|empresa=), OutreachMessagesPanel (copy or
                             share, no Gmail drafts), LinkedInReviewPanel (copy), OutreachHandler, OutreachStore,
                             LinkedInReviewHandler; OutreachRecord in Persistence/SupportRecords.cs
  Infrastructure/Google/     IGoogleAuthorizer and IAppSigningInfo (host implements them; package-only default for tests and
                             previews), SigningFingerprint (SHA-1 as keytool shows it), GoogleAuthService (no stored tokens;
                             google-connection.json keeps granted scopes, e-mail and date), GoogleApiClient (401 → clear token,
                             retry once), GmailClient, GoogleCalendarClient, IcsInviteParser (ports), GoogleOptions + GoogleScopes
  Features/Google/           GooglePanel (Configuración → Google: connect, scopes, disconnect), GoogleSetupWizard (5 steps:
                             project, APIs, consent screen, Android client with this install's package and SHA-1 to copy,
                             connect), GoogleUi (confirmation dialogs, port); Pipeline/ApplicationGoogleService + ApplicationMailPanel ("Correo")
  Features/Mail/             port of the web slice: TodayPanel ("Hoy" on the Panel; filter examples on tap), MailCheckService,
                             LinkedInAlertParser, MailRulesStore + MailRulesSeed (embedded web seed), MailRulesPanel
                             (Configuración → Reglas de correo); MailCheckRecord/MailWaitingRecord in Persistence/SupportRecords.cs
  Features/Transfer/         port of the web slice: TransferSchema, TransferFile, TransferMerger, TransferDatabase, DeletionLog
                             (used by the 11 delete actions), TransferService (backup in the app folder, reminders rescheduled
                             after combining), TransferPanel (share sheet / picker), SummaryList (lines instead of the web table)
  Infrastructure/Notifications/ INotificationScheduler (host implements it), ScheduledNotification, AppNavigationRequests
                             (routes from tapped notifications, only /pipeline/{guid})
tests/JobPilot.Mobile.Tests/ xUnit v3 + bUnit: layout, architecture, NoSendingTests, manifest, Gemini client and profile/CV
                             (ports of the web tests), prompts, configuration, migrations, settings and profile UI;
                             TestSupport/ (LlmTestHost with the app's real registrations, TestDatabase, JobsTestContext,
                             FakeGeminiHandler, FakeLlmClient, InMemorySecretStore, FakeFilePicker, FakeShareService,
                             FakeNotificationScheduler); the web's fictional
                             fixtures are linked into Fixtures/Cv and Fixtures/Sources; WebSchemaTests compares both EF
                             model snapshots; AndroidFolder helper
scripts/                     check-no-signing-keys.sh (keystores, packages, private keys, service account JSON),
                             Play-Wizard.ps1 (Windows wizard for Google Play, Spanish), play_upload.py (Play Developer API upload
                             used by .github/workflows/android-release.yml)
docs/PLAN.md                 Android plan
docs/PLAY.md                 Google Play internal testing: steps, automation, troubleshooting, verified facts
docs/play/                   Play Store icon (icon-512.png, 512×512, 32-bit PNG, same design as Resources/AppIcon) and feature
                             graphics (feature-graphic-1024x500.png with the Spanish tagline, -en.png with the English one;
                             24-bit PNG), phone screenshots (screenshots/es and screenshots/en, 1080×1920, captions per
                             language, UI in Spanish, fictional data)
```

## Gotchas

- The MAUI workload's SDK installer (`InstallAndroidDependencies`) fails with "CommonUtilities.Helpers.UserName must have
  a valid value" when `USER` is not set (the cloud container): run it with `USER=root`.
- In the cloud container .NET lives in `~/.dotnet` (not on `PATH`): `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`.
- There is no emulator in the container. The Core UI can be previewed at phone size with a throwaway Blazor Server host in the
  scratchpad that references `JobPilot.Mobile.Core` (Playwright with `executable_path=/opt/pw-browsers/chromium`).
- Release builds default to `aab;apk`; the project sets `AndroidPackageFormats=apk`. The `env:` prefix for signing
  passwords works only for APKs.
- A Release Android build leaves folders named after referenced projects under `obj/`: tests that scan `android/` must skip
  `bin/` and `obj/` (`AndroidFolder.SourceFiles`).
- The page is drawn edge to edge (target API 36): `wwwroot/app.css` moves the app bar, drawer and content by
  `env(safe-area-inset-*)`.
- No resources from the internet in `index.html` (no Google Fonts): Android's system font is already Roboto.
- `AddJobPilotMobile` must not register MudBlazor: its services need `NavigationManager`/`IJSRuntime`, so a test provider built
  with `ValidateOnBuild` fails. The host (and bUnit tests) call `AddMudServices()`.
- `<SupportedPlatform Include="browser" />` (Razor class library template) turns `SocketsHttpHandler` into CA1416 errors; the Core
  library does not declare it.
- Release builds trim with `TrimMode=partial`: only assemblies marked trimmable are trimmed (MudBlazor, the framework). EF Core,
  Microsoft.Data.Sqlite, the Core library and the domain are not (checked by comparing `obj/Release/.../linked` sizes), and
  `JsonSerializerIsReflectionEnabledByDefault` is `true` in MAUI. Re-check when adding packages that rely on reflection.
- Native libraries must have 16 KB ELF segment alignment for Android 15+ devices with 16 KB pages: `libe_sqlite3.so` from
  SQLitePCLRaw 2.1.12 has `0x4000` (`readelf -lW`).
- The Core library embeds `src/JobPilot.Web/Prompts/*.md`, `src/JobPilot.Web/appsettings.json` and the web seeds it needs
  (`Seed/mail-rules.seed.json`, `technology-terms.txt`; not the profile, job-search and followed-companies seeds) at build
  time (links, not copies): a web prompt, price or seed change reaches the phone with the next APK.
- The profile created on first use has `UpdatedAtUtc = DateTime.MinValue`, and search preferences are not stored until the
  owner saves them: "Combinar" keeps the newer row, so a real date would let a fresh install's empty values beat the ones in
  an older export. Tests that need profile data call `JobsTestContext.SaveProfileAsync()` (fictional);
  `JobsTestContext.CreateAsync()` saves the usual test keywords unless `keywords: false`.
- MAUI suppresses trim analysis warnings (`SuppressTrimAnalysisWarnings=true`), so "0 warnings" says nothing about trimming.
  To see them: `dotnet build android/src/JobPilot.Mobile -c Release -p:SuppressTrimAnalysisWarnings=false
  -p:TreatWarningsAsErrors=false -p:RuntimeIdentifier=android-arm64`. On 2026-10-06 the only ones were in the untrimmed
  assemblies (EF Core, SQLitePCLRaw, the Core library's System.Text.Json use) and MudBlazor; PdfPig and Open XML (marked
  trimmable) are trimmed without warnings. On 2026-10-07 (A8) the new ones were only IL2026 in the untrimmed Core Google code
  (JSON helpers, options binding); none from the Play services bindings or the host authorizer.
  A9 added only IL2026 in the untrimmed Core mail rules JSON (`MailRulesStore`, `MailRulesSeed`).
  A10 added none (the transfer code uses `JsonDocument`/`Utf8JsonWriter` and raw ADO.NET, no reflection-based serialization).
- The Core library adds the web SDK's implicit usings it needs (`Microsoft.Extensions.Configuration`, `DependencyInjection`,
  `Logging`) so ported web files compile unchanged.
- External links (`target="_blank"` or another host) open in the phone's browser: that is BlazorWebView's default
  (`UrlLoadingStrategy.OpenExternally`); the app never loads third-party pages inside the WebView.
- Tables with more than three columns do not fit a phone screen: the Android UI uses cards where the web uses tables
  (sources, followed companies).
- `WebSchemaTests` fails when a phone table is mapped differently from the web one: copy the web mapping, not a variant.
- QuestPDF (the web app's PDF library) only ships binaries for Windows, Linux and macOS: the Android app writes PDFs with
  PDFsharp (`Features/Tailoring/Export/PdfWriter.cs`). MigraDoc was tried first, but it positions every word separately and
  the PDF had no space characters (PdfPig read "AlexRivera"); the writer draws whole lines with `XGraphics.DrawString`.
- PDFsharp's platform-independent build has no system fonts: `GlobalFontSettings.FontResolver` serves the embedded Lato.
- MAUI's default FileProvider paths expose the whole cache and app data; `Platforms/Android/Resources/xml/
  microsoft_maui_essentials_fileprovider_file_paths.xml` limits sharing to `cache/sharing-root` (checked in the APK with
  `aapt2 dump xmltree --file res/xml/microsoft_maui_essentials_fileprovider_file_paths.xml <apk>`).
- `bin/Release/net10.0-android/*.apk` can be stale when a Release build used `-p:RuntimeIdentifier=...`: that build writes to
  `bin/Release/net10.0-android/<rid>/`.
- `PickResult` cannot be created with `new(null)` inside the record: it binds to the copy constructor. Use `new(File: null)`.
- MudTabs with more tabs than fit the phone scroll the tab bar too far after a tap (every label ends up off screen):
  pages with many sections (pipeline stages, an application's sections) use a wrapping `MudChipSet` instead.
- Android's `CA1416` analyzer does not understand `Build.VERSION.SdkInt` checks: guard newer APIs with
  `OperatingSystem.IsAndroidVersionAtLeast(n)`, and use string constants for newer broadcast actions in attributes.
- Alarms are matched by request code and intent action (not extras): `AndroidNotificationScheduler` cancels the previous
  ids from its stored schedule (`files/scheduled-notifications.json`, source-generated JSON) before scheduling new ones.
- NuGet audit runs on restore and its warnings are errors: when an advisory lands on a transitive package (PDFsharp's
  `System.Security.Cryptography.Pkcs`, 2026-10-15), CI fails at restore even without code changes. Pin a patched version
  in `Directory.Packages.props` and reference it from the Core library; `dotnet list package --vulnerable
  --include-transitive` shows what is flagged.
- Ical.Net is not trimmed (not marked trimmable; it reports IL2104 in the trim analysis but its linked size equals the
  package's).
- Xamarin.GooglePlayServices.Auth 122.0.0 raises AndroidX Fragment, Collection and Lifecycle above MAUI 10.0.110's
  versions; the Kotlin extension packages MAUI references only accept the old ones (NU1608 is an error here), so five of
  them are pinned to the resolved versions in `Directory.Packages.props`. Re-check them when MAUI is updated.
- Binding names: Java static `builder()` becomes `InvokeBuilder()` (`AuthorizationRequest`, `ClearTokenRequest`); Play
  services tasks are awaited with `AsAsync<T>()` (`Android.Gms.Extensions`). The package's XML docs are empty: list the API
  from the DLL metadata.
- Configuración has seven sections: chips, like every page with more sections than fit a phone's tab bar. Links open a
  section with `/configuracion?seccion=gemini|modelos|avisos|google|correo|datos|registro`.
- Transfer files arrive from Drive, mail or messaging apps with any MIME type: the picker accepts `application/json`,
  `text/plain` and `application/octet-stream` for `.json`, and `TransferFile.Read` validates the content.
- Transfer code is the web's (`src/JobPilot.Web/Features/Transfer/`) copied with the usual sed; `TransferSchema` must stay
  identical in both apps (each app's test compares it with its EF model, and `WebSchemaTests` compares the two models). The
  cross-app fixtures (`tests/JobPilot.Web.Tests/Fixtures/Transfer/pc-export.json`, `phone-export.json`, fictional) are
  imported by the other app's tests.
- Tooltips need hover: on the phone, extra details (e.g. the examples of a mail filter) open with a tap.
- bUnit: a `data-testid` on a `MudTextField` lands on the `textarea`/`input` itself (`textarea[data-testid=...]`).
- App bundles need `file:` (not `env:`) signing password properties: `android-release.yml` writes them to the runner's temp
  folder. `-p:AndroidPackageFormats=aab` on the command line overrides the project's `apk` default; the output is
  `com.vidi.jobpilot-Signed.aab`.
- The first bundle of a new Play app must be uploaded in Play Console; until a release exists the API answers "Only releases
  with status draft may be created on draft app" (`play_upload.py` prints the fix).
- Play re-signs the app with Google's app signing key: Google sign-in needs a second Android OAuth client with that SHA-1, and
  an app from Play cannot update (or be updated by) a CI APK signed with the upload key.
- Windows PowerShell 5.1: native stderr with `2>&1` under `$ErrorActionPreference = "Stop"` becomes an error, a comma in an
  argument (`--json a,b`) splits it into two, and an advanced function binds `-R` as its own parameter; the wizard uses
  `Continue` + `$LASTEXITCODE`, quotes comma lists and passes `gh` arguments through a simple function. It is saved as UTF-8
  with BOM so 5.1 reads the accents. Parse-check it with `pwsh` (`dotnet tool install --global PowerShell`).
- The installed app's signing certificate: `PackageManager.GetPackageInfo(name, PackageInfoFlags.Of(...SigningCertificates))`
  on API 33+, the `PackageInfoFlags` overload on 28-32 (obsolete on 33, CA1422) and `Signatures` before 28 (obsolete,
  CS0618); `SigningInfo.GetSigningCertificateHistory()` ends with the current signer when there is one signer.
- Android's picker filters by MIME type; many file managers report `.md` as `application/octet-stream`, so the picker also
  accepts it for Markdown and `CvTextExtractor` checks the extension afterwards.

## Decision log

| Date | Decision | Why |
|---|---|---|
| 2026-10-06 | MAUI Blazor Hybrid in `android/`, own solution, `JobPilot.Domain` referenced unchanged | Chosen by the owner; reuses the domain rules and the web app's Razor/MudBlazor knowledge. |
| 2026-10-06 | UI and services in a `net10.0` Razor class library (`JobPilot.Mobile.Core`) without MAUI references; the MAUI project is a thin host | Testable with bUnit and in CI without the Android toolchain; platform services behind interfaces. |
| 2026-10-06 | Application id `com.vidi.jobpilot`, minimum API 26, APK only | Chosen by the owner; the id and signing key cannot change without reinstalling. |
| 2026-10-06 | CI signs the APK with the owner's keystore from GitHub secrets, only on pushes, written to the runner's temp folder; `ApplicationVersion` = run number; artifact kept 3 days (30 until 2026-10-16, then 14 until 2026-10-22; lowered by the owner because the account's artifact storage filled up) | Chosen by the owner (no Android toolchain needed on the PC); every build installs as an update. |
| 2026-10-18 | The signed APK from `main` (push or manual run) is published as a GitHub release `android-v1.0.<run>` (`JobSearchCopilot-1.0.<run>.apk`, 3 newest kept, not marked latest so `releases/latest` stays the Windows installer); branch pushes keep a 14-day artifact with `continue-on-error` | Asked by the owner (a release per platform on GitHub), like the Windows installer; a release survives a full artifact quota. |
| 2026-10-06 | `allowBackup=false` | "All data stays local": the CV and applications are not copied to Google's cloud backup; moving data is the export/import file. |
| 2026-10-06 | Temporary (overlay) navigation drawer that closes after navigating | Phone screen; same sections and routes as the web app. |
| 2026-10-06 | Persistence with EF Core 10 + SQLite in `MobileDbContext`, web table names and mappings; migrations applied on start by `AppInitializer` before the router renders | Chosen by the owner over raw SQLite and sqlite-net: reuses the web model and queries in every phase; EF Core documents limitations on trimmed/AOT platforms, so the Release build keeps it untrimmed and the owner confirms it on the phone. |
| 2026-10-06 | Gemini key pasted in Configuración and kept in MAUI `SecureStorage` behind `ISecretStore`; read on each request, never shown back | Plan A1; Android's encrypted storage; testable with an in-memory store. |
| 2026-10-06 | Gemini client, JSON schema, call log and model settings ported from the web app (same behaviour, Spanish messages adapted to the phone); same `SocketsHttpHandler` with connect timeout | One behaviour in both apps; `SocketsHttpHandler` is a documented handler option on .NET for Android. |
| 2026-10-06 | Prompts and defaults (models, parameters, prices) embedded from the web app's files instead of copied | One source of truth; prices change with a new APK, as they change with an `appsettings.json` edit on the PC. |
| 2026-10-06 | `LlmUseCases` lists only the use cases implemented on Android, with the web names and labels | The models tab shows what exists on the phone; names in logs mean the same in both apps. |
| 2026-10-06 | CV files chosen with MAUI's `FilePicker` (Android document picker) behind `IFilePickerService`, read into memory with `OpenReadAsync`, never copied; `READ_EXTERNAL_STORAGE` only up to API 32, as MAUI documents; no media permissions | The plan's "selector de archivos"; works with `content://` URIs; testable with a fake. |
| 2026-10-06 | Profile and master CV ported from the web slice unchanged in behaviour (seed, versions, restore, import verifier flags, review before save); same tables as the web app | One behaviour in both apps; A10 maps the tables one to one. |
| 2026-10-06 | Offers ported from the web slice with the same 8 sources, user agent and minimum intervals (embedded web settings); each app keeps its own `SourceRuns` | Chosen by the owner ("mismos intervalos que la web"); the Sources tab tells the owner that both apps count separately against each source's limits. |
| 2026-10-06 | Phone tables mapped exactly like the web ones, enforced by comparing both EF model snapshots | A10 imports and exports rows without translation. |
| 2026-10-06 | PDF export with PDFsharp 6 (MIT) and its own line layout, Lato embedded (same look as the web PDF); DOCX with Open XML as in the web app | QuestPDF has no Android binaries; chosen by the owner among PDFsharp/MigraDoc, Android's PdfDocument, DOCX only and print-to-PDF; MigraDoc dropped because its PDFs had no real spaces. |
| 2026-10-06 | Approved documents leave the phone through Android's share sheet: written to `cache/sharing-root` right before sharing, deleted at the next share and at every app start; the FileProvider only exposes that folder | The plan's "Compartir de Android"; nothing is kept and no other app file can be reached through the provider. |
| 2026-10-06 | Own `NoSendingTests` with the web list plus Android/MAUI composers (`smsto:`, `SmsManager`, `Email.Default`, `ACTION_SENDTO`…) | The web test only scans `src/`; the same guarantee for `android/src`. |
| 2026-10-07 | Local notifications for due follow-ups/reminders (09:00 local on the due day) and interviews (60 min before), both editable; off until the owner turns them on (asks `POST_NOTIFICATIONS`) | Chosen by the owner for A5; the web app has nothing equivalent because it runs nothing unattended. |
| 2026-10-07 | `AlarmManager.SetExactAndAllowWhileIdle` when exact alarms are allowed (the owner enables "Alarmas y recordatorios" from Configuración on Android 12+), `SetAndAllowWhileIdle` otherwise; no `USE_EXACT_ALARM` | Chosen by the owner ("exactos si lo habilitás"); `SCHEDULE_EXACT_ALARM` is the user-granted permission Android documents for this. |
| 2026-10-07 | Alarms computed by a pure planner from the timeline (future times only, nearest 50) and replaced after every pipeline change, policy or settings change and on every app start; the schedule is stored in the app's private files and restored by a non-exported boot/update receiver | Same rules as the reminders; overdue items are on the Panel instead of ringing on every sync; Android cancels alarms on shutdown. |
| 2026-10-07 | Notifications are private: the lock screen shows a generic public version ("Tenés un recordatorio"); tapping opens the application's page; only `/pipeline/{guid}` routes are accepted from intents | Chosen by the owner; the launcher activity is exported, so intent extras are untrusted. |
| 2026-10-07 | Pipeline as stage chips + one list with a "Mover a…" menu instead of the drag-and-drop Kanban | Chosen by the owner: dragging with a finger is unreliable in a WebView. |
| 2026-10-07 | `.ics`, sent documents and follow-up drafts leave through the share sheet; follow-ups can also be copied (MAUI `Clipboard`); no Gmail/Calendar until A8 | No downloads on Android; the owner sends from their own mail app. |
| 2026-10-07 | Google through Android's authorization API (Play services `AuthorizationClient`, play-services-auth 22.0.0) with an "Android" OAuth client (package + SHA-1); no client id/secret in the app, no refresh token, a one-hour access token per session in memory; only granted scopes, e-mail and date in `google-connection.json`; revocation through Google's revoke endpoint | Verified procedure approved by the owner: custom URI schemes are no longer supported for Android and loopback redirects are deprecated for mobile; Google discourages refresh tokens on devices. |
| 2026-10-07 | Same Google scope as the web app (Gmail threads, responses, drafts; JobSearch Copilot calendar; "Solo Calendar") with the web confirmation dialogs; the Gmail client, calendar client and application service are ports | Chosen by the owner; one behaviour in both apps; `NoSendingTests` keep "never sends". |
| 2026-10-09 | Google Play: internal testing only (the owner's choice), Gmail stays personal (no restricted-scope verification); the CI keystore is the Play upload key with Play App Signing; `android-release.yml` (owner's file, rewritten: it pointed at a non-existent project) builds, tests and signs the `.aab` on tags `v*` or manual runs and uploads it with a service account through the Play Developer API (`play_upload.py`, no third-party action; JWT signed with `openssl`); a Windows wizard (`Play-Wizard.ps1`) automates the PC side and guides the console steps | Public release would need a 12-tester/14-day closed test and Gmail verification with a yearly security assessment; the service account key is a single secret and never leaves GitHub. |
| 2026-10-10 | The phone app starts with an empty profile (the web profile seed is no longer embedded); the Panel shows first steps (Gemini key, profile, master CV, import) until they are done; the empty profile counts as never edited for "Combinar" | Asked by the owner: other testers can install the Play build with their own Gemini key and Google account, and must not start from the owner's professional profile. |
| 2026-10-10 | No search keywords or followed companies at first either (job-search and followed-companies seeds not embedded); preferences are stored only when the owner saves them; "Buscar" is disabled (and `JobSearchService` queries nothing) until there is a keyword, with a link to Preferencias and a "Primeros pasos" item | Asked by the owner; chosen by the owner over searching without keywords, which would store offers of any field and spend the sources' request limits (Remotive ≈ 4/day). |
| 2026-10-16 | Product name "JobSearch Copilot" (was JobPilot) in everything the owner sees: titles, app bar, Android app label, messages, the Google calendar created from now on, DOCX creator, .ics product id, user agent (`JobSearchCopilot/1.0`), generated file names (`jobsearch-copilot-pc-….json`, `-celular-`, `-pipeline.ics`, `-postulacion.ics`, backups), Play listing graphics and docs. Code names (projects, namespaces, classes) and internal identifiers stay: data folder `JobPilot`, configuration section `JobPilot`, Data Protection application name and purposes, `com.vidi.jobpilot`, export format `jobpilot-transfer`, user-secrets id, notification channel, .ics UIDs, CI artifact names | Asked by the owner (visible text and docs only); internal identifiers kept so existing data, keys, exports, the installed app and Google calendars keep working. A calendar created before keeps its old name in Google. |
| 2026-10-15 | Google setup wizard on the phone (asked by the owner; the PC got one too): Google Cloud steps with links, and the Android client step shows the installed app's package name and signing SHA-1 read from Android (`IAppSigningInfo`), with copy buttons; the DEVELOPER_ERROR message points to that step | The SHA-1 differs between a CI APK and a Play install; reading it from the app removes the keytool step and the most common mistake. |
| 2026-10-07 | Export / "Combinar" import (A10) as in the web app: same file, schema and merge rules (ported code); the file leaves through the share sheet and comes back through the picker (max 100 MB); the database is copied to `backups/` in the app's private folder before combining; reminders are rescheduled afterwards | Owner's choices (combine, everything but secrets, deletions travel, undated edits take the file's value, share and pick); the backup protects the only copy of the phone's data. |
| 2026-10-07 | Mail brief ported unchanged (rules, classifier, alert roles → offers, waiting cards, stored facts only); read only on "Actualizar correo", no WorkManager job; the phone has its own rules document seeded from the web seed | Same behaviour as the web app; "nothing runs unattended"; each app keeps its own data until the A10 export/import. |
| 2026-10-07 | Interviews and negotiation ported unchanged (practice rules, rubrics, comparator, script checks); sections as chips, comparison and history as cards, the score chart's SVG text doubled (its 640-wide viewBox is drawn at about half size on a phone) | Same rules as the web app; readable on a 360-400 px screen. |
| 2026-10-07 | Companies and outreach ported unchanged (URL context research, fact verifier, three tones, only verified facts, LinkedIn review on pasted text); outreach drafts are copied or shared through the share sheet, LinkedIn proposals copied | Same "never invent" and "never send" rules as the web app; the share sheet hands the text to LinkedIn or a mail app where the owner sends it. |

## Verified external facts

Checked on 2026-10-06:

- .NET for Android build properties: `AndroidKeyStore`, `AndroidSigningKeyStore`, `AndroidSigningKeyAlias`,
  `AndroidSigningStorePass` / `AndroidSigningKeyPass` (support `env:` and `file:` prefixes; `env:` not for `aab`),
  `AndroidPackageFormats` (default `aab;apk` for Release, added in .NET 9). Source:
  `https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties`.
- `maui-android` workload 10.0.110 (SDK band 10.0.400): MAUI packages 10.0.110, target API 36, build-tools 36.0.0.
- EF Core on .NET: supported on Android through .NET, with "some limitations" on platforms that use trimming and AOT. Source:
  `https://learn.microsoft.com/en-us/ef/core/miscellaneous/platforms`.
- .NET for Android HTTP handlers: `AndroidMessageHandler` is the default of `new HttpClient()`; `SocketsHttpHandler` is a
  supported option. Source: `https://learn.microsoft.com/en-us/dotnet/api/xamarin.android.net.androidmessagehandler`,
  `https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties` (`AndroidHttpClientHandlerType`).
- MAUI file picker: Android filters by MIME type, `FullPath` may be a `content://` URI (read with `OpenReadAsync`),
  `READ_EXTERNAL_STORAGE` (max SDK 32) for Android 12 and lower. Source:
  `https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-10.0`.
- Himalayas API: no CORS headers, so browsers cannot call it ("call the API from your backend"); a native app's HTTP client
  is not affected. Attribution and no resubmission to aggregators still apply; data is refreshed every 24 h. Source:
  `https://himalayas.app/api`.
- Blazor Hybrid links: external links (other host or `target="_blank"`) open in the device's handler by default. Source:
  `https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/routing?view=aspnetcore-10.0`.
- QuestPDF 2026.9.1: supported operating systems Windows, Linux and macOS (package README); runtimes only for win, linux,
  osx. PDFsharp 6.2.4: MIT, `lib/net10.0`, no native code. Lato 1.104 from Google Fonts: SIL OFL 1.1 (in the font's name
  table and `Fonts/OFL.txt`).
- MAUI Share: `ShareFileRequest` with a file in `FileSystem.CacheDirectory`; on Android the FileProvider may expose the
  whole cache and app data unless `microsoft_maui_essentials_fileprovider_file_paths.xml` overrides its paths. Source:
  `https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/data/share?view=net-maui-10.0`.
- GitHub docs use `actions/upload-artifact@v4` with `retention-days`. Source:
  `https://docs.github.com/en/actions/tutorials/store-and-share-data`.

Checked on 2026-10-07 (phase A5):

- Alarms: on Android 12+ `set()` fires within an hour of the trigger time unless battery restrictions apply;
  `setExactAndAllowWhileIdle()` fires at a nearly precise time even in battery saving; exact alarms need
  `SCHEDULE_EXACT_ALARM` (user-granted, not pre-granted to fresh installs targeting API 33+ on Android 14; check
  `canScheduleExactAlarms()`, broadcast `ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED`) or `USE_EXACT_ALARM`
  (limited use cases); all alarms are cancelled on shutdown, restore them with `RECEIVE_BOOT_COMPLETED`. Source:
  `https://developer.android.com/develop/background-work/services/alarms/schedule`.
- `POST_NOTIFICATIONS` is a runtime permission from Android 13 (API 33); notifications of newly installed apps are off
  until granted. Source: `https://developer.android.com/develop/ui/views/notifications/notification-permission`.
- MAUI `Permissions.PostNotifications` is supported on Android. Source:
  `https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/appmodel/permissions?view=net-maui-10.0`.
- A receiver with `android:exported="false"` still receives broadcasts sent by the system. Source:
  `https://developer.android.com/guide/topics/manifest/receiver-element`.
- Google OAuth for native apps: custom URI schemes are no longer supported on Android; the loopback redirect for mobile apps is
  deprecated; Android apps use the Android authorization docs. Source: `https://developers.google.com/identity/protocols/oauth2/native-app`.
- Android authorization: `AuthorizationClient.authorize()` with the requested scopes returns an access token (or a pending
  intent for consent); access tokens last one hour; later sessions call `authorize()` again without user interaction;
  offline access needs a backend and refresh tokens should not be stored on the device; setup: Android client ID with package
  name and SHA-1 of the signing certificate. Source: `https://developer.android.com/identity/authorization`.
- Android OAuth client: package name and SHA-1 (`keytool -list -v -keystore <keystore> -alias <alias>`). Source:
  `https://support.google.com/cloud/answer/15549257`.
- Xamarin.GooglePlayServices.Auth 122.0.0 (MIT binding of play-services-auth 22.0.0) targets net10.0-android36.0.
- WorkManager does not guarantee the execution time (system optimizations), so it was not used for timed notifications.
  Source: `https://developer.android.com/develop/background-work/background-tasks/persistent/getting-started/define-work`.

## Status

- [x] Phase A0 — Skeleton (pending: the owner's signing secrets and a first install on the phone)
- [x] Phase A1 — Persistence, Gemini and settings (pending: on the phone, saving the key, "Probar conexión" with the real key and
  the call log after restarting the app, which confirms EF Core and secure storage at runtime)
- [x] Phase A2 — Profile and master CV (pending: a real import on the phone with the owner's CV through Android's picker)
- [x] Phase A3 — Offers (pending: a real "Buscar", a manual entry and an analysis with the owner's key on the phone)
- [x] Phase A4 — Tailored CV and cover letter (pending: a real generation with the owner's key and opening the shared PDF and
  DOCX on the phone)
- [x] Phase A5 — Pipeline, reminders and dashboard (pending: on the phone, turning notifications on, receiving a follow-up
  and an interview notification, again after a reboot, allowing exact alarms, sharing the `.ics` to a calendar app and a
  real follow-up draft with the owner's key)
- [x] Phase A6 — Companies and outreach (pending: a real company research, an outreach draft shared to LinkedIn or the mail app
  and a LinkedIn review with the owner's key)
- [x] Phase A7 — Interviews and negotiation (pending: on the phone, a question bank, STAR stories, a mock interview with feedback, an
  offer comparison, a script and a roleplay with the owner's key)
- [x] Phase A8 — Google (pending: the owner's Android OAuth client in Google Cloud, a signed APK, and a real connection, thread
  link, Gmail draft and calendar event on the phone)
- [x] Phase A9 — Mail brief (pending: a real check on the owner's Gmail from the phone, after the Android OAuth client and
  leaving Advanced Protection, and the owner's city, pay floor and rates in the phone's rules)
- [x] Phase A10 — Export and import between PC and phone (pending: a real transfer PC → phone → PC with the owner's data,
  through the share sheet and the picker)
- [x] Google Play internal testing tooling (pending: the owner runs `Play-Wizard.ps1`, the first manual upload, the service
  account and the Play OAuth client; see `docs/PLAY.md`)
