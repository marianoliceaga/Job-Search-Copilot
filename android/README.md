# JobPilot for Android

The Android version of [JobPilot](../README.md): the same remote job search assistant, on your phone. Built with
**.NET 10 MAUI Blazor Hybrid and MudBlazor** (the web app's UI library), using the **Gemini API** as its only LLM.

The app is independent of the PC app: it has its own database on the phone. Data moves between both apps only when
you export a file in one and import it in the other (phase A10). Like the web app, it prepares, analyses and drafts;
you approve. It never sends an application, e-mail or message on your behalf.

> **Status:** phases A0–A2 delivered: the APK opens with the same sections as the web app (each one marked with the phase
> that implements it); Configuración (Gemini key, connection test, models, call log) and Perfil y CV maestro (profile,
> CV import from a file on the phone, editor, versions) work. See [`docs/PLAN.md`](docs/PLAN.md). The UI is in Spanish.

## Install on your phone

Android 8.0 (API 26) or later. The app is installed from an APK signed with your own key, not from Google Play.

1. Download the APK from the latest successful run of the **Android** workflow (GitHub → Actions → Android → run →
   Artifacts → `JobPilot-android-<n>`). Artifacts are kept for 30 days. The download is a zip with the `.apk` inside.
2. Copy it to the phone and open it. Android asks to allow installing apps from that source (the browser or file
   manager); allow it for that app only.
3. Later builds install as updates (CI numbers each build with the run number) as long as they are signed with the same
   key. An APK signed with another key does not install over it: you would have to uninstall, which deletes the app's
   data.

The app's data is excluded from Android's cloud backup (`allowBackup=false`).

## Gemini API key

Create a key in Google AI Studio (a paid-tier key is recommended, as for the web app), then in the app open
**Configuración → Gemini**, paste it and tap **Guardar clave**. It is stored encrypted with Android's secure storage
(`SecureStorage`), never in the database or a file, and never shown again; **Borrar clave** removes it. **Probar
conexión** sends a minimal request without personal data and records it, with its estimated cost, in **Registro**.

Models, parameters and prices are the web app's (`src/JobPilot.Web/appsettings.json`, embedded in the APK); per use case
overrides are edited in **Configuración → Modelos** and stored on the phone.

## Profile and master CV

**Perfil y CV maestro → CV maestro → Elegir archivo** opens Android's document picker (PDF, DOCX or Markdown, up to
10 MB). The text is extracted on the phone and shown before anything is sent; **Estructurar con Gemini** turns it into a
draft that you review (values not found in the document are flagged) and save. The file is read once and not copied
into the app. On Android 12 and lower the app asks for permission to read files the first time.

## Signing key (once)

CI signs the APK with your key, read from GitHub secrets. The key never goes into the repository
(`android/scripts/check-no-signing-keys.sh` fails CI if a keystore is committed).

1. Create a keystore **outside the repository** with `keytool` (part of any JDK, for example the Microsoft Build of
   OpenJDK):

   ```bash
   keytool -genkeypair -v -keystore jobpilot.keystore -alias jobpilot -keyalg RSA -keysize 2048 -validity 10000
   ```

   It asks for a keystore password and your name (the name is stored in the certificate, which anyone with the APK
   can read; any value works).
2. Encode it in Base64:

   ```powershell
   # Windows PowerShell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("jobpilot.keystore")) | Set-Clipboard
   ```

   ```bash
   # Linux / macOS
   base64 -w0 jobpilot.keystore     # macOS: base64 -i jobpilot.keystore
   ```
3. In GitHub → the repository → Settings → Secrets and variables → Actions, add these repository secrets:

   | Secret | Value |
   |---|---|
   | `ANDROID_KEYSTORE_BASE64` | the Base64 text |
   | `ANDROID_KEYSTORE_PASSWORD` | the keystore password |
   | `ANDROID_KEY_ALIAS` | `jobpilot` (the `-alias` above) |
   | `ANDROID_KEY_PASSWORD` | the key password (the same as the keystore password unless you set another one) |

4. Keep a backup of `jobpilot.keystore` and its password somewhere private (not in the repo). Without them no new
   build can update the installed app.

Until the secrets exist, CI still builds and tests the app but does not publish an APK. Pull requests never publish one.

## Build locally (optional)

Not needed to use the app; CI builds it. Requirements: .NET 10 SDK, the MAUI Android workload and the Android SDK.

```bash
dotnet workload install maui-android
# Installs the Android SDK components the project needs (platform, build tools) into the given folder
dotnet build android/src/JobPilot.Mobile -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory=<android-sdk-folder> -p:AcceptAndroidSDKLicenses=True

dotnet build android/JobPilot.Mobile.slnx -p:AndroidSdkDirectory=<android-sdk-folder>   # warnings are errors
dotnet test --solution android/JobPilot.Mobile.slnx
```

A signed APK built locally (the passwords are read from environment variables so they do not appear in build logs):

```bash
dotnet publish android/src/JobPilot.Mobile -f net10.0-android -c Release \
  -p:ApplicationVersion=<number higher than the installed build> \
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<path>/jobpilot.keystore -p:AndroidSigningKeyAlias=jobpilot \
  -p:AndroidSigningStorePass=env:ANDROID_SIGNING_STORE_PASS -p:AndroidSigningKeyPass=env:ANDROID_SIGNING_KEY_PASS
```

The signed file is `com.vidi.jobpilot-Signed.apk` in the publish folder.

## Privacy

The same rules as the web app: data stays on the phone, secrets (Gemini key, Google token) will live in Android's
secure storage, the UI shows what is sent to Gemini before each action, and the export file used to move data between
the PC and the phone is not encrypted (keep it private and delete it after importing).

## License

[MIT](../LICENSE), like the rest of the repository.
