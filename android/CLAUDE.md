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

## Commands

```bash
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
src/JobPilot.Mobile/         MAUI host, net10.0-android: MauiProgram (DI), App, MainPage (BlazorWebView → Core Routes),
                             Platforms/Android (MainActivity, MainApplication, AndroidManifest), Resources (icon, splash),
                             wwwroot/index.html + app.css (safe areas, error UI)
src/JobPilot.Mobile.Core/    Razor class library, net10.0: Routes, Layout (MainLayout with a temporary drawer, NavMenu,
                             AppSections with the Android phase of each section), Shared (PendingSection), Pages
tests/JobPilot.Mobile.Tests/ xUnit v3 + bUnit: layout, architecture, NoSendingTests, manifest; AndroidFolder helper
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

## Decision log

| Date | Decision | Why |
|---|---|---|
| 2026-10-06 | MAUI Blazor Hybrid in `android/`, own solution, `JobPilot.Domain` referenced unchanged | Chosen by the owner; reuses the domain rules and the web app's Razor/MudBlazor knowledge. |
| 2026-10-06 | UI and services in a `net10.0` Razor class library (`JobPilot.Mobile.Core`) without MAUI references; the MAUI project is a thin host | Testable with bUnit and in CI without the Android toolchain; platform services behind interfaces. |
| 2026-10-06 | Application id `com.vidi.jobpilot`, minimum API 26, APK only | Chosen by the owner; the id and signing key cannot change without reinstalling. |
| 2026-10-06 | CI signs the APK with the owner's keystore from GitHub secrets, only on pushes, written to the runner's temp folder; `ApplicationVersion` = run number; artifact kept 30 days | Chosen by the owner (no Android toolchain needed on the PC); every build installs as an update. |
| 2026-10-06 | `allowBackup=false` | "All data stays local": the CV and applications are not copied to Google's cloud backup; moving data is the export/import file. |
| 2026-10-06 | Temporary (overlay) navigation drawer that closes after navigating | Phone screen; same sections and routes as the web app. |
| 2026-10-06 | Own `NoSendingTests` with the web list plus Android/MAUI composers (`smsto:`, `SmsManager`, `Email.Default`, `ACTION_SENDTO`…) | The web test only scans `src/`; the same guarantee for `android/src`. |

## Verified external facts

Checked on 2026-10-06:

- .NET for Android build properties: `AndroidKeyStore`, `AndroidSigningKeyStore`, `AndroidSigningKeyAlias`,
  `AndroidSigningStorePass` / `AndroidSigningKeyPass` (support `env:` and `file:` prefixes; `env:` not for `aab`),
  `AndroidPackageFormats` (default `aab;apk` for Release, added in .NET 9). Source:
  `https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties`.
- `maui-android` workload 10.0.110 (SDK band 10.0.400): MAUI packages 10.0.110, target API 36, build-tools 36.0.0.
- GitHub docs use `actions/upload-artifact@v4` with `retention-days`. Source:
  `https://docs.github.com/en/actions/tutorials/store-and-share-data`.

## Status

- [x] Phase A0 — Skeleton (pending: the owner's signing secrets and a first install on the phone)
- [ ] Phase A1 — Persistence, Gemini and settings
- [ ] Phase A2 — Profile and master CV
- [ ] Phase A3 — Offers
- [ ] Phase A4 — Tailored CV and cover letter
- [ ] Phase A5 — Pipeline, reminders and dashboard
- [ ] Phase A6 — Companies and outreach
- [ ] Phase A7 — Interviews and negotiation
- [ ] Phase A8 — Google
- [ ] Phase A9 — Mail brief
- [ ] Phase A10 — Export and import between PC and phone
