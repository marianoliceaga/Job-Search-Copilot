# CLAUDE.md — JobPilot for Android

Guidance for working on the Android app in `android/`. The repo-wide rules in [`../CLAUDE.md`](../CLAUDE.md) apply
(principles, working agreement, conventions, languages); this file adds what is specific to Android. Scope and
acceptance criteria per phase: [`docs/PLAN.md`](docs/PLAN.md).

## Rules specific to the Android port

- **Do not modify existing files outside `android/`**, except the ones the owner allowed: short Android sections in
  `CLAUDE.md`, `README.md`, `docs/PLAN.md`, the new `.github/workflows/android.yml`, and the web app's export/import
  feature in phase A10 (confirm again when it starts). `src/JobPilot.Domain` is referenced, never changed.
- **Ask, don't assume.** The owner asked explicitly: any doubt (behaviour, platform API, library support) goes to the
  owner before it becomes code.
- One phase at a time with the owner's approval in between (`docs/PLAN.md`).
- The app never sends messages: `tests/JobPilot.Mobile.Tests/NoSendingTests.cs` scans `android/src` (the web app's test
  only scans the repo's `src/`). Sharing goes through Android's share sheet, where the owner sends it themselves.
- Secrets (Gemini key, Google token) go to Android's secure storage, never to files, preferences or the repo. The signing
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
                             RescheduleReceiver restores alarms after boot/update), MainActivity (single top, opens the page
                             of a tapped notification), App, MainPage
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
  Features/Profile/          port of the web slice: ProfilePage (/perfil: Perfil / CV maestro), ProfileForm, ProfileStore +
                             ProfileSeed (embedded web seed), MasterCvStore (versions), MasterCvPanel, MasterCvEditor,
                             CvImportPanel (Android picker), Import/ (CvTextExtractor with PdfPig/Open XML, ImportedCv,
                             CvImportHandler); Shared/ PartialDateField, StringListEditor
  Features/Jobs/             port of the web slice: JobsPage (/ofertas: Ofertas / Fuentes y empresas / Preferencias), JobDetailPage
                             (/ofertas/{id}: Empresa, Outreach and Pipeline buttons, tailoring), SourcesPanel
                             (cards instead of tables), Sources/ (8 IJobSource providers, web settings and intervals),
                             JobIngestion, JobSearchService, FollowedCompanyStore, JobPreferencesStore + JobSeedFiles (embedded
                             web seeds), ManualJobImport, JobListService, Analysis/ (job-analysis, AnalysisVerifier)
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
  Features/Dashboard/        DashboardPage (/, lists instead of tables), DashboardService (port)
  Features/Companies/        port of the web slice: CompaniesPage (/empresas, cards instead of a table), CompanyPage
                             (/empresas/{id}), CompanyButton (offer page), CompanyStore, CompanyResearchHandler (URL context +
                             CompanyFactVerifier), CompanyFormat
  Features/Outreach/         port of the web slice: OutreachPage (/outreach?oferta=|empresa=), OutreachMessagesPanel (copy or
                             share, no Gmail drafts), LinkedInReviewPanel (copy), OutreachHandler, OutreachStore,
                             LinkedInReviewHandler; OutreachRecord in Persistence/SupportRecords.cs
  Infrastructure/Notifications/ INotificationScheduler (host implements it), ScheduledNotification, AppNavigationRequests
                             (routes from tapped notifications, only /pipeline/{guid})
tests/JobPilot.Mobile.Tests/ xUnit v3 + bUnit: layout, architecture, NoSendingTests, manifest, Gemini client and profile/CV
                             (ports of the web tests), prompts, configuration, migrations, settings and profile UI;
                             TestSupport/ (LlmTestHost with the app's real registrations, TestDatabase, JobsTestContext,
                             FakeGeminiHandler, FakeLlmClient, InMemorySecretStore, FakeFilePicker, FakeShareService,
                             FakeNotificationScheduler); the web's fictional
                             fixtures are linked into Fixtures/Cv and Fixtures/Sources; WebSchemaTests compares both EF
                             model snapshots; AndroidFolder helper
scripts/                     check-no-signing-keys.sh
docs/PLAN.md                 Android plan
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
  (`Seed/profile.seed.json`) at build time (links, not copies): a web prompt, price or seed change reaches the phone with
  the next APK.
- MAUI suppresses trim analysis warnings (`SuppressTrimAnalysisWarnings=true`), so "0 warnings" says nothing about trimming.
  To see them: `dotnet build android/src/JobPilot.Mobile -c Release -p:SuppressTrimAnalysisWarnings=false
  -p:TreatWarningsAsErrors=false -p:RuntimeIdentifier=android-arm64`. On 2026-10-06 the only ones were in the untrimmed
  assemblies (EF Core, SQLitePCLRaw, the Core library's System.Text.Json use) and MudBlazor; PdfPig and Open XML (marked
  trimmable) are trimmed without warnings.
- The Core library adds the web SDK's implicit usings it needs (`Microsoft.Extensions.Configuration`, `DependencyInjection`,
  `Logging`) so ported web files compile unchanged.
- External links (`target="_blank"` or another host) open in the phone's browser: that is BlazorWebView's default
  (`UrlLoadingStrategy.OpenExternally`); the app never loads third-party pages inside the WebView.
- Tables with more than three columns do not fit a phone screen: the Android UI uses cards where the web uses tables
  (sources, followed companies).
- `JobSeedFiles` reads embedded seeds; tests pass a folder through its internal constructor.
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
- Ical.Net is not trimmed (not marked trimmable; it reports IL2104 in the trim analysis but its linked size equals the
  package's).
- Android's picker filters by MIME type; many file managers report `.md` as `application/octet-stream`, so the picker also
  accepts it for Markdown and `CvTextExtractor` checks the extension afterwards.

## Decision log

| Date | Decision | Why |
|---|---|---|
| 2026-10-06 | MAUI Blazor Hybrid in `android/`, own solution, `JobPilot.Domain` referenced unchanged | Chosen by the owner; reuses the domain rules and the web app's Razor/MudBlazor knowledge. |
| 2026-10-06 | UI and services in a `net10.0` Razor class library (`JobPilot.Mobile.Core`) without MAUI references; the MAUI project is a thin host | Testable with bUnit and in CI without the Android toolchain; platform services behind interfaces. |
| 2026-10-06 | Application id `com.vidi.jobpilot`, minimum API 26, APK only | Chosen by the owner; the id and signing key cannot change without reinstalling. |
| 2026-10-06 | CI signs the APK with the owner's keystore from GitHub secrets, only on pushes, written to the runner's temp folder; `ApplicationVersion` = run number; artifact kept 30 days | Chosen by the owner (no Android toolchain needed on the PC); every build installs as an update. |
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
- [ ] Phase A7 — Interviews and negotiation
- [ ] Phase A8 — Google
- [ ] Phase A9 — Mail brief
- [ ] Phase A10 — Export and import between PC and phone
