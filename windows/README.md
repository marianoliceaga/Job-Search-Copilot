# JobSearch Copilot for Windows

A desktop version of the web app: its own window, Start menu shortcut and installer, no browser tab and no .NET SDK
needed. Inside, it is the same app as `src/JobPilot.Web` (every feature, the same screens), running on
`http://localhost:5080` and shown in a [WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) window.

## Install

1. Open the latest Windows release, https://github.com/marianoliceaga/Job-Search-Copilot/releases/latest, and download
   `JobSearchCopilot-Setup-1.0.<n>.exe` from **Assets**.
2. Run it. The installer is not signed, so Windows SmartScreen says "Windows protegió
   su PC": choose **Más información → Ejecutar de todas formas**.
3. The app installs for your user only (no administrator rights) in `%LocalAppData%\Programs\JobSearch Copilot`, with a
   Start menu shortcut (and a desktop one if you tick it).

Requirements: Windows 10 (1809 or later) or 11, 64-bit, and the Microsoft Edge WebView2 Runtime, which comes with Windows 11
and most Windows 10 PCs. If it is missing, the window says so and links to Microsoft's download page.

To update, run a newer installer: it asks to close the app first and replaces the program files. To uninstall, use
**Configuración de Windows → Aplicaciones**.

## Your data

The desktop app uses the same data folder as the web app, `%LocalAppData%\JobPilot` (database, Google connection,
Gemini key saved in the app, backups). Moving between `dotnet run` and the installed app needs nothing. Uninstalling never
deletes that folder; delete it yourself if you want to remove your data.

- If the web app is already running (`dotnet run`), the window uses it instead of starting another one, and leaves it
  running when you close the window.
- If the window started the app, `dotnet run` fails while the window is open (the port is in use): close the window first.

## First steps

- **Gemini API key:** Configuración → Conexión con Gemini → paste the key from
  [Google AI Studio](https://aistudio.google.com/apikey) → **Guardar clave**. It is stored encrypted in the data folder.
  It takes priority over a key in `secrets.json` (user-secrets); the screen shows which one is in use and lets you replace
  it at any time. Environment variables are not read.
- **Google (optional):** Configuración → Google, same wizard and OAuth client as the web app (redirect URI
  `http://localhost:5080/google/callback`). When you connect, the window opens your default browser: Google does not allow
  signing in from embedded browsers. Finish there and come back to the window, which refreshes by itself.
- **Exported files** (CV, cover letter, `.ics`, transfer file) go to your Downloads folder.
- Links to job postings and other sites open in your default browser; only the app's own pages load in the window.

## Build

On Windows with the .NET 10 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
dotnet test --solution windows/JobPilot.Desktop.slnx
dotnet publish windows/src/JobPilot.Desktop -c Release -r win-x64 --self-contained -o publish
dotnet publish src/JobPilot.Web -c Release -r win-x64 --self-contained -o publish/server
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" windows\installer\JobSearchCopilot.iss
```

The installer lands in `windows\installer\bin` (git-ignored, like `publish`).

CI (`.github/workflows/windows.yml`) does the same on every push and pull request. On `main` (push, or a manual run from
**Actions → Windows → Run workflow**) it publishes the installer as a release `windows-v1.0.<n>` and keeps the 3 newest; on
other branches it uploads it as a 14-day artifact, which is skipped when the account's artifact storage is full.
