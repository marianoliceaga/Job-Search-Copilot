# CLAUDE.md — JobSearch Copilot for Windows

Guidance for the Windows desktop app in `windows/`. The repo-wide rules in [`../CLAUDE.md`](../CLAUDE.md) apply
(principles, working agreement, conventions, languages); this file adds what is specific to the desktop host. Install and
build steps for the owner: [`README.md`](README.md).

## Rules specific to the desktop app

- **The window is a host, not a port.** Every feature lives in the web app (`src/JobPilot.Web`); the desktop app starts it
  and shows it. A change the owner asks for in the app's screens goes to the web app, not here.
- Web app changes made for the desktop app stay minimal (agreed with the owner on 2026-10-16). So far: the Gemini key can be
  pasted in Configuración (`GeminiKeyStore`), because an installed app has no `dotnet user-secrets`.
- Only the app's own pages load in the window (`NavigationPolicy`): third-party pages open in the default browser, never
  inside WebView2, and the Google connection (`/google/connect`) always opens in the browser.
- The app never sends messages: the desktop host adds no sending path (the web app's `NoSendingTests` still apply to `src`).
- Same data folder as the web app (`%LocalAppData%\JobPilot`, chosen by the owner); the uninstaller never deletes it.
- **Ask, don't assume**, as in the rest of the repo.

## Commands

```bash
dotnet build windows/JobPilot.Desktop.slnx          # also on Linux (EnableWindowsTargeting); warnings are errors
dotnet test --solution windows/JobPilot.Desktop.slnx # Core logic + the real web app started from its build output
```

Publishing and the installer need Windows (see `README.md` and `.github/workflows/windows.yml`).

## Layout

```
JobPilot.Desktop.slnx
Directory.Packages.props      imports the repo's central versions and adds Microsoft.Web.WebView2
src/JobPilot.Desktop.Core/    net10.0, no Windows APIs: DesktopIdentity (names shared with the installer, port from
                              JobPilot__Port), ServerProbe (recognizes the app by /app.js), ServerHost (start the published
                              web app from server\, wait, attach to a running one, stop), ServerOutput (last lines, memory
                              only), NavigationPolicy (window / browser / blocked)
src/JobPilot.Desktop/         net10.0-windows WPF host (AssemblyName JobSearchCopilot): App (single instance: mutex +
                              activation event), MainWindow (WebView2, status and error panel, Google banner), ChildProcessJob
                              (job object: the server dies with the window), app.ico (from the Android icon), app.manifest
installer/JobSearchCopilot.iss Inno Setup: per-user install, Spanish, AppMutex, server\ replaced on every update, data kept
tests/JobPilot.Desktop.Tests/ xUnit v3: navigation rules, ServerHost with a fake process and port, RealServerTests (real web
                              app, temporary data folder, free port), PackagingTests (project, installer and workflow agree)
```

## Gotchas

- WPF projects build on Linux with `EnableWindowsTargeting`, but nothing Windows-specific can run in the cloud container:
  keep logic in `JobPilot.Desktop.Core` (net10.0) so it is tested everywhere. Inno Setup is not available on Linux either;
  the installer is only built by CI.
- `SupportedOSPlatformVersion` higher than 7.0 needs a versioned target framework (`net10.0-windows10.0.x`); the project does
  not set it (NETSDK1135).
- The published web app runs in Production from its own folder: `ServerHost` sets the working directory (content root for
  `appsettings.json`, prompts, seeds) and `ASPNETCORE_ENVIRONMENT=Production`. Tests start the build output instead, which
  needs Development for static assets (see the root gotcha about `--no-launch-profile`).
- `JobPilot.Desktop.slnx` lists `src/JobPilot.Web` and the domain: a project reached only through a `ProjectReference` from
  outside the solution builds in Debug even with `-c Release`, and `RealServerTests` look for the Release output.
- The child process's stdout/stderr are redirected and must be drained (`BeginOutputReadLine`), or the server blocks.
- Google rejects OAuth sign-in from embedded browsers, so `/google/connect` opens in the default browser; the callback lands
  there too (same server on localhost) and the window reloads when it is activated again.
- WebView2's password autosave and autofill are off: the Gemini key and the Google client secret are typed into the app.
- The WebView2 user data folder is `%LocalAppData%\JobPilot\desktop\WebView2`, never the install folder.
- `ServerProbe` uses a client without proxy: a system proxy must not intercept `localhost`.
- Installer output defaults to `windows\installer\bin` (git-ignored): never inside `publish`, which the installer packs.

## Decision log

| Date | Decision | Why |
|---|---|---|
| 2026-10-16 | Desktop app = WPF window with WebView2 over the published web app, started as a child process on localhost (or attached to one already running); self-contained x64 host and web app (`server\`), per-user Inno Setup installer built by CI on `windows-2025` | Chosen by the owner over a MAUI Windows build of the phone app and a browser launcher: every web feature from day one, no duplicated code, no .NET install needed. |
| 2026-10-16 | Same data folder as the web app; uninstall keeps it | Chosen by the owner: one CV, offers, key and Google connection on the PC. |
| 2026-10-16 | Gemini key pasted in Configuración, stored in `gemini-key.bin` (Data Protection, like the Google client); user-secrets and environment still win; never shown back | Chosen by the owner: the installed app has no SDK for user-secrets; the key stays out of the repo, the database and exports. |
| 2026-10-16 | Third-party pages and the Google connection open in the default browser; the window reloads after the owner comes back | Google's policy against embedded-browser sign-in; untrusted pages never run inside the app's window. |
| 2026-10-16 | Job object with kill-on-close for the server; single instance by named mutex, also used as the installer's `AppMutex` | No orphan server keeping the port and the database; updates never replace files in use. |
| 2026-10-17 | The installer from `main` is published as a GitHub release `windows-v1.0.<run>` (3 newest kept; job with `contents: write`); branch pushes keep a 14-day artifact with `continue-on-error` | Chosen by the owner: the account's artifact storage stayed full hours after deleting every artifact, and a release gives a stable download link. |
| 2026-10-16 | Installer not code-signed | No certificate; the README explains the SmartScreen prompt. |

## Verified external facts

Checked on 2026-10-16:

- Microsoft.Web.WebView2 1.0.4258.31 is the latest stable package (published 2026-09-28); it covers WPF
  (`Microsoft.Web.WebView2.Wpf`).
- WebView2 Runtime: part of Windows 11, installed on the vast majority of Windows 10 devices; apps should check for it
  (`GetAvailableBrowserVersionString`) and can send users to `https://developer.microsoft.com/microsoft-edge/webview2/consumer/`.
  Source: `https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution`.
- GitHub `windows-2025` runner image lists InnoSetup 6.7.1. Source:
  `https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md`.
- Google blocks OAuth requests from embedded webviews (`disallowed_useragent`) and asks apps to use the system browser.
  Source: `https://developers.googleblog.com/upcoming-security-changes-to-googles-oauth-20-authorization-endpoint-in-embedded-webviews/`.
- Gemini API keys are created at `https://aistudio.google.com/apikey`. Source: `https://ai.google.dev/gemini-api/docs/api-key`.

## Status

- [x] W1 — Desktop host, installer and CI (pending: a real install on the owner's PC, the WebView2 check, the Google connection
  through the browser and an export to Downloads)
