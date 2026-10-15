# JobPilot for Android

The Android version of [JobPilot](../README.md): the same remote job search assistant, on your phone. Built with
**.NET 10 MAUI Blazor Hybrid and MudBlazor** (the web app's UI library), using the **Gemini API** as its only LLM.

The app is independent of the PC app: it has its own database on the phone. Data moves between both apps only when
you export a file in one and import it in the other. Like the web app, it prepares, analyses and drafts;
you approve. It never sends an application, e-mail or message on your behalf.

> **Status:** all phases (A0–A10) delivered: every section works — Configuración (Gemini key, connection test, models,
> notifications, Google, mail rules, export/import, call log), Perfil y CV maestro, Ofertas (sources, manual entry,
> analysis, ranking, tailored CV and cover letter), Pipeline, the Panel with the "Hoy" mail brief, Empresas, Outreach,
> Entrevistas and Negociación, plus the optional Google connection and moving data between the PC and the phone. See
> [`docs/PLAN.md`](docs/PLAN.md). The UI is in Spanish.

## Install on your phone

Android 8.0 (API 26) or later. The app is installed from an APK signed with your own key, or from Google Play's internal
testing track (see [Google Play](#google-play-internal-testing)).

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

The phone app starts without anyone's data (anyone you add as a tester can install it): empty profile, no search
keywords and no followed companies. The Panel lists the first steps (Gemini key, profile, master CV, search keywords) until
they are done, and offers **Importar y combinar** for data exported from the PC or another phone. **Buscar** stays disabled
until there is at least one keyword (without one the search would store offers of any field and spend the sources' limits).

**Perfil y CV maestro → CV maestro → Elegir archivo** opens Android's document picker (PDF, DOCX or Markdown, up to
10 MB). The text is extracted on the phone and shown before anything is sent; **Estructurar con Gemini** turns it into a
draft that you review (values not found in the document are flagged) and save. The file is read once and not copied
into the app. On Android 12 and lower the app asks for permission to read files the first time.

## Offers

**Ofertas → Buscar** reads the same sources as the PC app, with the same minimum interval per source. The phone and the
PC keep separate histories, so if you search from both on the same day, keep each source's limit in mind (Remotive
recommends at most 4 requests per day). Original postings open in the phone's browser.

## Tailored CV and cover letter

On an offer, **CV a medida** and **Carta** work as in the PC app (every text cites the master CV and is checked). Once a
document is approved, **Compartir PDF** and **Compartir DOCX** open Android's share sheet: you choose where it goes (Files,
Drive, an e-mail you write yourself...). The app keeps no copy of the file. The PDF is made with PDFsharp (the PC app uses
QuestPDF, which does not run on Android); it follows the same rules: one column, real text, Lato font embedded.

## Pipeline, reminders and notifications

**Agregar al pipeline** on an offer starts its application. **Pipeline** shows one stage at a time (tap a stage chip);
the card menu moves an application to another stage, and every change is recorded in its timeline. An application's page
has its stage, reminder, notes, contacts, the approved documents you sent, interviews and **Follow-up** (a Gemini draft
you copy or share to your mail app and send yourself; **Ya lo envié** restarts the reminder). **Compartir calendario
(.ics)** hands interviews and reminders to a calendar app through the share sheet.

Notifications are off until you turn them on in **Configuración → Avisos** (Android asks for permission). The phone then
notifies you at 9:00 on the day a follow-up or reminder is due and one hour before each interview (both editable). On
Android 12 and later, **Permitir avisos exactos** opens the system setting "Alarmas y recordatorios"; without it Android
may deliver notifications late, more so in battery saver. With the phone locked a notification only says "Tenés un
recordatorio"; the offer appears once unlocked. Notifications are local: nothing is sent and no network is used. They are
restored after a reboot.

## Companies and outreach

**Empresas** (or **Crear ficha de la empresa** on an offer) builds a company profile only from the sources you choose:
public URLs, which Gemini reads from Google's servers, and texts you paste. Every fact keeps its source and quote; facts
that cannot be verified are marked **No verificado** and are never used in messages. **Outreach** (also from an offer or a
company) drafts a LinkedIn invitation note, LinkedIn message or e-mail in three tones; **Copiar** puts it on the
clipboard and **Compartir** opens Android's share sheet (LinkedIn, your mail app...), where you review and send it
yourself. **Ya lo envié** records it, and in the offer's timeline when it is in the pipeline. **Perfil de LinkedIn**
reviews the headline and About you paste (the app never opens LinkedIn) and proposes texts that cite your CV. Gmail
drafts arrive with phase A8.

## Interviews and negotiation

**Entrevistas** works as in the PC app: for an offer in your pipeline, a question bank (each question quotes the part of
the posting that motivates it) and STAR stories built only from your master CV; **Simulacros** is a chat where Gemini
interviews you one question at a time, with rubric feedback and a score history. **Negociación** compares the offers you
enter in annual USD with your exchange rates and contractor costs (nothing is estimated), drafts a negotiation script
with your numbers and runs a recruiter roleplay. On the phone, sections are chips that wrap instead of tabs, and wide
tables are cards.

## Google (optional)

With Google connected the phone can link Gmail threads to an application, record a company's reply in the timeline,
create Gmail **drafts** (never sends) and add interviews to a secondary "JobPilot" calendar, as the PC app does. Every
action shows what is sent before it happens.

The phone uses Android's own authorization (Google Play services), which Google documents for Android apps; the PC app's
web OAuth client does not work on a phone. **Configuración → Google** has a step-by-step setup wizard with links to each
Google Cloud page (same project, consent screen, test users, Gmail API and Google Calendar API as the PC app):

1. Project, APIs and consent screen (skip them if the PC app is already set up).
2. **Google Auth Platform → Clients → Create client**, application type **Android**, with the package name
   (`com.vidi.jobpilot`) and the SHA-1 the wizard shows with copy buttons. The app reads that SHA-1 from its own
   signature, so it is the right one for this install: your keystore's for an APK from GitHub, Google's app signing key's
   for an install from Google Play (each needs its own client). Without it, `keytool -list -v -keystore jobpilot.keystore
   -alias jobpilot` prints it.

Nothing is pasted into the app: Google recognises it by package and signature. Then the wizard's last step,
**Conectar con Google**, shows Google's consent screen ("Solo Calendar" asks
only for the calendar, for accounts with Advanced Protection, where Gmail is blocked). The phone keeps no Google token:
Android gives the app a one-hour access token each session, silently while the grant lasts; **Desconectar y revocar**
revokes it. Phones without Google Play services cannot connect.

## Mail brief ("Hoy")

With Google connected with **Gmail y Calendar**, the Panel starts with **Hoy**, as on the PC: tap **Actualizar correo**
and JobPilot reads your job-search mail since the last check (the first time, the last day) with the same rules as the PC
app. It shows the threads waiting for you ("Te esperan", with "Registrar en <empresa>" to add the reply to an
application, and "Listo" to hide a card until a new message), the roles from LinkedIn job alerts (added to Ofertas without
description: paste it on the offer page before analysing), interviews in the next 48 hours and what was filtered (tap a
chip to see examples).

Mail is read only when you tap the button: nothing runs in the background, and nothing is sent, archived, labelled or
marked as read. Only sender, subject and dates are kept; message texts are never stored or sent to Gemini. The rules are
edited as JSON in **Configuración → Reglas de correo** (city, companies, pay floor and exchange rates are yours to fill
in; pay is never estimated). The phone keeps its own rules: changes on the PC do not reach it.

## Moving data between the PC and the phone

**Configuración → Exportar / Importar** in both apps:

1. On the device with the data, **Exportar** (on the phone, "Exportar y compartir" opens Android's share sheet: Drive,
   your own mail, a USB cable…). The file has all your JobPilot data, never the Gemini key or the Google connection.
2. On the other device, **Importar y combinar** → choose the file. You see what would be added, updated, deleted or left
   out, per section; nothing changes until you tap **Combinar**.
3. Combining adds what is missing, keeps the most recent change when something changed on both devices, takes the file's
   value for edits without a date (contacts, comparator offers, followed companies), and deletes what you deleted on the
   other device. Your data is copied first (on the phone, inside the app's private folder; the last five copies are kept).

Importing the same file twice changes nothing, so you can go back and forth. The file is **not encrypted**: send it only
to yourself and delete it after importing.

## Google Play (internal testing)

The app can also be installed and updated from Google Play, on the **internal testing** track (only you and testers you
add by e-mail; no public listing). On Windows, run the wizard from the repository folder; it automates the tools, signing
secrets, fingerprints, bundle builds and uploads, and opens each Play Console / Google Cloud page for the few clicks that
must be done by hand:

```powershell
powershell -ExecutionPolicy Bypass -File android\scripts\Play-Wizard.ps1
```

After the first upload (by hand, once), every new version is built, tested, signed and uploaded by
**Actions → Android release (Google Play)** or by pushing a tag `v1.2.3`. Full guide: [`docs/PLAY.md`](docs/PLAY.md).

The app from Play and the APK from GitHub are signed with different keys and cannot update each other: export your data
first (Configuración → Exportar / Importar), then switch.

Other people can install the same app as testers and use their own Gemini key and Google account; their data stays on
their phone. See "Other testers" in [`docs/PLAY.md`](docs/PLAY.md).

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

The same rules as the web app: data stays on the phone, the Gemini key lives in Android's secure storage (no Google
token is kept), the UI shows what is sent to Gemini before each action, and the export file used to move data between
the PC and the phone is not encrypted (keep it private and delete it after importing).

## License

[MIT](../LICENSE), like the rest of the repository.
