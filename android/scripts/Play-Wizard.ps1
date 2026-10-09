<#
.SYNOPSIS
    Asistente para publicar JobPilot (Android) en Google Play, en la prueba interna (solo vos).

.DESCRIPTION
    Te lleva paso a paso y automatiza lo que se puede desde tu PC:
      - instala y verifica GitHub CLI y Java (keytool) con winget;
      - crea la clave de firma (o usa la que ya tenés) y carga los secretos de GitHub;
      - muestra y copia las huellas SHA-1/SHA-256;
      - genera el paquete .aab en GitHub Actions y lo descarga;
      - te guía en Play Console y Google Cloud (abre cada página);
      - carga la cuenta de servicio y publica versiones nuevas en Play.

    Nunca guarda contraseñas: GitHub CLI te las pide directamente, y para leer las huellas la contraseña se usa en memoria.
    El progreso se guarda en %LOCALAPPDATA%\JobPilot\play-wizard.json (sin secretos). Guía completa: android/docs/PLAY.md.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File android\scripts\Play-Wizard.ps1
#>
[CmdletBinding()]
param(
    [string]$Repo = "marianoliceaga/Job-Search-Copilot",
    [string]$Package = "com.vidi.jobpilot"
)

Set-StrictMode -Version 2.0
# Native tools (gh, keytool, winget) write progress to stderr; with "Stop", Windows PowerShell 5.1 would turn that into
# errors. Every native call checks $LASTEXITCODE instead.
$ErrorActionPreference = "Continue"

$StateDir = Join-Path $env:LOCALAPPDATA "JobPilot"
$StatePath = Join-Path $StateDir "play-wizard.json"
$SecretNames = @("ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD")
$Workflow = "android-release.yml"

# ---------------------------------------------------------------- estado y utilidades

function Get-State {
    if (Test-Path $StatePath) {
        try { return (Get-Content -Raw -Path $StatePath -Encoding UTF8 | ConvertFrom-Json) } catch { }
    }
    return [pscustomobject]@{}
}

function Save-State($State) {
    if (-not (Test-Path $StateDir)) { New-Item -ItemType Directory -Path $StateDir | Out-Null }
    $State | ConvertTo-Json -Depth 5 | Set-Content -Path $StatePath -Encoding UTF8
}

function Set-StateValue($State, [string]$Name, $Value) {
    if ($null -ne $State.PSObject.Properties[$Name]) { $State.$Name = $Value }
    else { $State | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
    Save-State $State
}

function Get-StateValue($State, [string]$Name) {
    if ($null -ne $State.PSObject.Properties[$Name]) { return $State.$Name }
    return $null
}

function Write-Title([string]$Text) {
    Write-Host ""
    Write-Host ("== " + $Text + " ") -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step([string]$Text) { Write-Host ("  - " + $Text) }
function Write-Ok([string]$Text) { Write-Host ("  OK  " + $Text) -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host ("  !!  " + $Text) -ForegroundColor Yellow }
function Write-Bad([string]$Text) { Write-Host ("  XX  " + $Text) -ForegroundColor Red }

function Confirm-Yes([string]$Question, [bool]$Default = $true) {
    $suffix = if ($Default) { "[S/n]" } else { "[s/N]" }
    $answer = Read-Host "$Question $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim().ToLowerInvariant().StartsWith("s")
}

function Wait-Enter([string]$Text = "Cuando termines, apretá Enter para seguir") { [void](Read-Host "  $Text") }

function Open-Url([string]$Url) {
    Write-Host ("  Abriendo " + $Url) -ForegroundColor DarkGray
    Start-Process $Url
}

function Copy-Text([string]$Text, [string]$What) {
    try { Set-Clipboard -Value $Text; Write-Ok "$What copiado al portapapeles." } catch { }
}

function Read-Secret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# A simple function (no param block) so that gh options such as -R reach gh unchanged in $args.
function Invoke-Gh {
    $output = & gh @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw ("gh " + ($args -join " ") + " falló: " + ($output | Out-String).Trim()) }
    return $output
}

function Find-Keytool {
    $command = Get-Command keytool -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += (Join-Path $env:JAVA_HOME "bin\keytool.exe") }
    $candidates += "C:\Program Files\Android\Android Studio\jbr\bin\keytool.exe"
    foreach ($root in @("C:\Program Files\Microsoft", "C:\Program Files\Eclipse Adoptium", "C:\Program Files\Java")) {
        if (Test-Path $root) {
            $candidates += (Get-ChildItem -Path $root -Filter keytool.exe -Recurse -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty FullName)
        }
    }
    foreach ($candidate in $candidates) { if ($candidate -and (Test-Path $candidate)) { return $candidate } }
    return $null
}

function Get-RepoSecrets {
    $json = Invoke-Gh secret list -R $Repo --json name
    return @(($json | Out-String | ConvertFrom-Json) | ForEach-Object { $_.name })
}

function Get-Fingerprints([string]$Keytool, [string]$Keystore, [string]$Alias) {
    $password = Read-Secret "  Contraseña del keystore (no se guarda)"
    $env:JOBPILOT_STOREPASS = $password
    try {
        $output = & $Keytool -list -v -keystore $Keystore -alias $Alias -storepass:env JOBPILOT_STOREPASS 2>&1 |
            ForEach-Object { "$_" } | Out-String
    }
    finally {
        Remove-Item Env:\JOBPILOT_STOREPASS -ErrorAction SilentlyContinue
        $password = $null
    }
    if ($LASTEXITCODE -ne 0) { throw ("keytool no pudo leer la clave: " + $output.Trim()) }
    $sha1 = [regex]::Match($output, "SHA1:\s*([0-9A-F:]{59})").Groups[1].Value
    $sha256 = [regex]::Match($output, "SHA256:\s*([0-9A-F:]{95})").Groups[1].Value
    if (-not $sha1) { throw "No encontré la huella SHA-1 en la salida de keytool." }
    return [pscustomobject]@{ Sha1 = $sha1; Sha256 = $sha256 }
}

function Get-LatestRunId {
    $runs = Invoke-Gh run list -R $Repo --workflow $Workflow --limit 1 --json "databaseId,createdAt" | Out-String | ConvertFrom-Json
    if (-not $runs) { return $null }
    return @($runs)[0].databaseId
}

function Start-ReleaseRun([string[]]$Fields) {
    $before = Get-LatestRunId
    $arguments = @("workflow", "run", $Workflow, "-R", $Repo, "--ref", "main")
    foreach ($field in $Fields) { $arguments += @("-f", $field) }
    Invoke-Gh @arguments | Out-Null
    Write-Step "Esperando que GitHub arranque la ejecución..."
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 3
        $id = Get-LatestRunId
        if ($id -and $id -ne $before) { return $id }
    }
    throw "GitHub no mostró la ejecución nueva. Mirala en https://github.com/$Repo/actions"
}

function Wait-Run($RunId) {
    Write-Step "Siguiendo la ejecución $RunId (tarda 10-20 minutos; podés dejar esta ventana abierta)."
    & gh run watch $RunId -R $Repo --exit-status --interval 30
    if ($LASTEXITCODE -ne 0) {
        Write-Bad "La ejecución falló. Detalle: https://github.com/$Repo/actions/runs/$RunId"
        return $false
    }
    Write-Ok "Ejecución terminada."
    return $true
}

# ---------------------------------------------------------------- pasos

function Step-Tools($State) {
    Write-Title "1. Herramientas"
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Warn "Falta GitHub CLI (gh)."
        if (Confirm-Yes "¿Lo instalo con winget?") {
            & winget install --id GitHub.cli -e --source winget
            Write-Warn "Cerrá y volvé a abrir PowerShell para que aparezca 'gh', y corré el asistente de nuevo."
            return
        }
        Write-Step "Instalalo desde https://cli.github.com y volvé a correr el asistente."
        return
    }
    Write-Ok "GitHub CLI instalado."
    & gh auth status -h github.com *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Step "Iniciá sesión en GitHub (se abre el navegador)."
        & gh auth login -h github.com -p https -w
        if ($LASTEXITCODE -ne 0) { Write-Bad "No se pudo iniciar sesión en GitHub."; return }
    }
    Write-Ok "Sesión de GitHub iniciada."
    try { Invoke-Gh repo view $Repo --json name | Out-Null; Write-Ok "Acceso al repositorio $Repo." }
    catch { Write-Bad "No tenés acceso a $Repo con esta sesión de gh."; return }

    $keytool = Find-Keytool
    if (-not $keytool) {
        Write-Warn "Falta Java (keytool), que lee y crea la clave de firma."
        if (Confirm-Yes "¿Instalo Microsoft Build of OpenJDK 21 con winget?") {
            & winget install --id Microsoft.OpenJDK.21 -e --source winget
            $keytool = Find-Keytool
        }
    }
    if (-not $keytool) { Write-Bad "No encontré keytool. Instalá un JDK y volvé a correr el asistente."; return }
    Write-Ok "keytool: $keytool"
    Set-StateValue $State "keytool" $keytool
    Set-StateValue $State "step1" $true
}

function Step-SigningKey($State) {
    Write-Title "2. Clave de firma (upload key)"
    Write-Step "Es la clave con la que GitHub firma tus APK y los paquetes para Play. Google Play la usa como 'clave de subida'."
    $secrets = Get-RepoSecrets
    $missing = @($SecretNames | Where-Object { $secrets -notcontains $_ })
    if ($missing.Count -eq 0) {
        Write-Ok "Los cuatro secretos ANDROID_* ya están en GitHub (CI ya firma tus APK)."
        $current = Get-StateValue $State "keystore"
        $path = Read-Host "  Ruta de tu archivo .keystore (solo para leer las huellas)$(if ($current) { " [$current]" })"
        if ([string]::IsNullOrWhiteSpace($path)) { $path = $current }
        if (-not $path -or -not (Test-Path $path)) {
            Write-Warn "No encontré el archivo. Sin él no podés leer las huellas desde acá (las ves igual en Play Console)."
            Write-Warn "Si lo perdiste: guardá tus datos (Exportar), y elegí 'crear una nueva' corriendo este paso de nuevo."
            if (Confirm-Yes "¿Crear una clave nueva y reemplazar los secretos?" $false) { New-SigningKey $State }
            return
        }
        Set-StateValue $State "keystore" (Resolve-Path $path).Path
        $alias = Read-Host "  Alias de la clave [jobpilot]"
        if ([string]::IsNullOrWhiteSpace($alias)) { $alias = "jobpilot" }
        Set-StateValue $State "alias" $alias
        Set-StateValue $State "step2" $true
        return
    }
    Write-Warn ("Faltan secretos: " + ($missing -join ", "))
    if (Confirm-Yes "¿Creo una clave nueva y cargo los secretos?") { New-SigningKey $State }
}

function New-SigningKey($State) {
    $keytool = Get-StateValue $State "keytool"
    if (-not $keytool) { $keytool = Find-Keytool }
    if (-not $keytool) { Write-Bad "Primero completá el paso 1."; return }
    $folder = Join-Path $env:USERPROFILE "JobPilot-firma"
    if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }
    $keystore = Join-Path $folder "jobpilot.keystore"
    if (Test-Path $keystore) {
        $keystore = Join-Path $folder ("jobpilot-" + (Get-Date -Format "yyyyMMdd-HHmm") + ".keystore")
    }
    Write-Step "Se crea en $keystore (fuera del repositorio)."
    Write-Step "keytool te va a pedir una contraseña (dos veces) y datos para el certificado (cualquier valor sirve)."
    & $keytool -genkeypair -v -keystore $keystore -alias jobpilot -keyalg RSA -keysize 2048 -validity 10000
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $keystore)) { Write-Bad "keytool no creó la clave."; return }
    Write-Ok "Clave creada."

    $base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($keystore))
    $base64 | & gh secret set ANDROID_KEYSTORE_BASE64 -R $Repo
    if ($LASTEXITCODE -ne 0) { Write-Bad "No se pudo cargar ANDROID_KEYSTORE_BASE64."; return }
    & gh secret set ANDROID_KEY_ALIAS -R $Repo --body jobpilot
    Write-Step "Ahora GitHub CLI te pide la contraseña del keystore (pegala o escribila; no se muestra)."
    & gh secret set ANDROID_KEYSTORE_PASSWORD -R $Repo
    Write-Step "Y la contraseña de la clave (la misma, salvo que hayas puesto otra)."
    & gh secret set ANDROID_KEY_PASSWORD -R $Repo
    Write-Ok "Secretos cargados en GitHub."
    Write-Warn "Guardá una copia de $keystore y de su contraseña en un lugar privado (no en el repositorio)."
    Set-StateValue $State "keystore" $keystore
    Set-StateValue $State "alias" "jobpilot"
    Set-StateValue $State "step2" $true
}

function Step-Fingerprints($State) {
    Write-Title "3. Huellas de tu clave de subida"
    $keystore = Get-StateValue $State "keystore"
    $keytool = Get-StateValue $State "keytool"
    if (-not $keystore -or -not $keytool) { Write-Bad "Primero completá los pasos 1 y 2."; return }
    $alias = Get-StateValue $State "alias"
    if (-not $alias) { $alias = "jobpilot" }
    $prints = Get-Fingerprints $keytool $keystore $alias
    Write-Host ""
    Write-Host ("  SHA-1   " + $prints.Sha1) -ForegroundColor White
    Write-Host ("  SHA-256 " + $prints.Sha256) -ForegroundColor White
    Copy-Text $prints.Sha1 "SHA-1"
    Write-Step "Esta SHA-1 es la del cliente OAuth Android para los APK de GitHub (instalados a mano)."
    Write-Step "La app instalada desde Play usa otra clave (la de Google): su SHA-1 la cargás en el paso 7."
    Set-StateValue $State "uploadSha1" $prints.Sha1
    Set-StateValue $State "step3" $true
}

function Step-FirstBundle($State) {
    Write-Title "4. Primer paquete para Play (.aab)"
    Write-Step "GitHub Actions compila, prueba y firma el paquete (workflow '$Workflow'), sin subirlo a Play."
    if (-not (Confirm-Yes "¿Lo genero ahora?")) { return }
    $runId = Start-ReleaseRun @("upload=false")
    if (-not (Wait-Run $runId)) { return }
    $dest = Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads\JobPilot-play"
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
    Invoke-Gh run download $runId -R $Repo -D $dest | Out-Null
    $aab = Get-ChildItem -Path $dest -Filter *.aab -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $aab) { Write-Bad "No encontré el .aab descargado en $dest."; return }
    Write-Ok ("Paquete listo: " + $aab.FullName)
    Set-StateValue $State "firstBundle" $aab.FullName
    Set-StateValue $State "step4" $true
    Start-Process explorer.exe "/select,`"$($aab.FullName)`""
}

function Step-PlayConsole($State) {
    Write-Title "5. Play Console: crear la app y subir el primer paquete (a mano, una sola vez)"
    $aab = Get-StateValue $State "firstBundle"
    Write-Step "Google exige que el primer paquete se suba desde Play Console. Te abro cada página."
    Write-Host ""
    Write-Host "  5.1 Crear la app" -ForegroundColor White
    Open-Url "https://play.google.com/console/developers/app-list"
    Write-Step "'Crear app' > Nombre: JobPilot > Idioma predeterminado: Español (Latinoamérica) – es-419"
    Write-Step "App (no juego) > Gratis > aceptá las dos declaraciones > 'Crear app'."
    Wait-Enter
    Write-Host "  5.2 Testers" -ForegroundColor White
    Write-Step "Menú: Probar y publicar (Test and release) > Prueba interna (Internal testing) > pestaña Verificadores (Testers)."
    Write-Step "'Crear lista de correo electrónico': nombre 'Yo', agregá el mail de la cuenta Google de tu teléfono > Guardar."
    Write-Step "Marcá la lista y 'Guardar cambios'."
    Wait-Enter
    Write-Host "  5.3 Primera versión" -ForegroundColor White
    Write-Step "En Prueba interna: 'Crear versión nueva' (Create new release)."
    Write-Step "Firma de apps de Google Play (Play App Signing): dejá la opción recomendada (clave generada por Google)."
    if ($aab) { Write-Step "Subí el paquete: $aab"; Copy-Text $aab "Ruta del .aab" }
    Write-Step "Nombre y notas: podés dejar lo que propone. 'Siguiente' > 'Guardar y publicar' (o 'Lanzar')."
    Write-Step "Si Play Console pide completar algo antes, seguí sus indicaciones (la guía android/docs/PLAY.md tiene las respuestas)."
    Wait-Enter
    Write-Host "  5.4 Link para instalar" -ForegroundColor White
    Write-Step "En Prueba interna > Verificadores: copiá el 'link para unirse' (Join on the web / Android)."
    $link = Read-Host "  Pegá el link acá (Enter para saltar)"
    if ($link) { Set-StateValue $State "testerLink" $link.Trim() }
    Write-Host "  5.5 Huella de la clave de Google" -ForegroundColor White
    Write-Step "Menú: Integridad de la app (App integrity) > Firma de apps (App signing), dentro de Probar y publicar."
    Write-Step "Copiá la huella SHA-1 de la 'Clave de firma de apps' (App signing key), no la de subida (Upload key)."
    $sha1 = Read-Host "  Pegá la SHA-1 de la clave de firma de apps (Enter para saltar)"
    if ($sha1) { Set-StateValue $State "playSha1" $sha1.Trim().ToUpperInvariant() }
    Set-StateValue $State "step5" $true
    Write-Ok "Primera versión subida. Las siguientes las sube GitHub (pasos 6 y 8)."
}

function Step-ServiceAccount($State) {
    Write-Title "6. Cuenta de servicio para que GitHub publique solo"
    Write-Step "Usá el mismo proyecto de Google Cloud que el de Google/Gmail de la app."
    Write-Host "  6.1 Activar la API" -ForegroundColor White
    Open-Url "https://console.cloud.google.com/apis/library/androidpublisher.googleapis.com"
    Write-Step "Elegí el proyecto arriba y tocá 'Habilitar' (Google Play Android Developer API)."
    Wait-Enter
    Write-Host "  6.2 Crear la cuenta de servicio y su clave" -ForegroundColor White
    Open-Url "https://console.cloud.google.com/iam-admin/serviceaccounts/create"
    Write-Step "Nombre: jobpilot-play. 'Crear y continuar' > sin roles > 'Listo'."
    Write-Step "Abrila > pestaña 'Claves' > 'Agregar clave' > 'Crear clave nueva' > JSON > se descarga un archivo."
    Wait-Enter
    Write-Host "  6.3 Darle permiso en Play Console" -ForegroundColor White
    Open-Url "https://play.google.com/console/developers/users-and-permissions"
    Write-Step "'Invitar usuarios nuevos' > mail de la cuenta de servicio (termina en .iam.gserviceaccount.com)."
    Write-Step "Permisos de la app: agregá JobPilot con 'Publicar apps en segmentos de prueba' (Release apps to testing tracks)."
    Write-Step "'Invitar usuario' y confirmá."
    Wait-Enter
    Write-Host "  6.4 Cargar la clave en GitHub" -ForegroundColor White
    $default = Get-ChildItem -Path (Join-Path $env:USERPROFILE "Downloads") -Filter *.json -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $hint = if ($default) { " [$($default.FullName)]" } else { "" }
    $path = Read-Host "  Ruta del JSON descargado$hint"
    if ([string]::IsNullOrWhiteSpace($path) -and $default) { $path = $default.FullName }
    if (-not $path -or -not (Test-Path $path)) { Write-Bad "No encontré el archivo."; return }
    $raw = Get-Content -Raw -Path $path -Encoding UTF8
    try { $key = $raw | ConvertFrom-Json } catch { Write-Bad "El archivo no es JSON."; return }
    if ($key.type -ne "service_account" -or -not $key.client_email -or -not $key.private_key) {
        Write-Bad "No es una clave de cuenta de servicio (falta type/client_email/private_key)."
        return
    }
    $raw | & gh secret set PLAY_SERVICE_ACCOUNT_JSON -R $Repo
    if ($LASTEXITCODE -ne 0) { Write-Bad "No se pudo cargar el secreto."; return }
    Write-Ok ("Secreto PLAY_SERVICE_ACCOUNT_JSON cargado (" + $key.client_email + ").")
    if (Confirm-Yes "¿Borro el JSON de tu PC? (recomendado: si lo necesitás, creás otra clave)") {
        Remove-Item -Path $path -Force
        Write-Ok "Archivo borrado."
    }
    Set-StateValue $State "step6" $true
}

function Step-GoogleSignIn($State) {
    Write-Title "7. Google (Gmail y Calendar) en la app instalada desde Play"
    Write-Step "Play vuelve a firmar la app con la clave de Google, así que necesita su propio cliente OAuth Android."
    $sha1 = Get-StateValue $State "playSha1"
    if (-not $sha1) {
        $sha1 = Read-Host "  SHA-1 de la 'Clave de firma de apps' de Play Console (paso 5.5)"
        if ($sha1) { $sha1 = $sha1.Trim().ToUpperInvariant(); Set-StateValue $State "playSha1" $sha1 }
    }
    if ($sha1) { Copy-Text $sha1 "SHA-1 de Play" }
    Open-Url "https://console.cloud.google.com/auth/clients/create"
    Write-Step "Tipo de aplicación: Android. Nombre: JobPilot (Play)."
    Write-Step "Nombre del paquete: $Package"
    Write-Step "Huella SHA-1: la de Play (ya está en el portapapeles). 'Crear'."
    Write-Step "Dejá el cliente que ya tenías: sirve para los APK de GitHub."
    Wait-Enter
    Open-Url "https://console.cloud.google.com/auth/audience"
    Write-Step "Público (Audience): si está en 'Prueba', agregá tu mail como usuario de prueba."
    Write-Step "Es una app personal sin verificar: Google muestra 'app no verificada'; tocá 'Avanzado' > 'Ir a JobPilot'."
    Wait-Enter
    Set-StateValue $State "step7" $true
}

function Step-Publish($State) {
    Write-Title "8. Publicar una versión nueva"
    $secrets = Get-RepoSecrets
    if ($secrets -notcontains "PLAY_SERVICE_ACCOUNT_JSON") { Write-Bad "Falta el secreto PLAY_SERVICE_ACCOUNT_JSON (paso 6)."; return }
    Write-Step "GitHub compila, prueba, firma y sube la versión a la prueba interna. El número sube solo."
    $notes = Read-Host "  Notas de la versión (opcional)"
    $fields = @("upload=true", "track=internal", "status=completed")
    if ($notes) { $fields += "notes=$notes" }
    $runId = Start-ReleaseRun $fields
    if (Wait-Run $runId) {
        Write-Ok "Versión enviada a Play. En unos minutos aparece como actualización en tu teléfono."
        Set-StateValue $State "step8" $true
    }
}

function Step-Install($State) {
    Write-Title "9. Instalar en tu teléfono"
    Write-Warn "Si ya tenés el APK de GitHub instalado, Play no lo puede actualizar (otra firma). Antes de desinstalarlo:"
    Write-Step "En la app: Configuración > Exportar / Importar > 'Exportar y compartir' y guardá el archivo (Drive o tu mail)."
    Write-Step "Desinstalá la app vieja."
    $link = Get-StateValue $State "testerLink"
    if ($link) { Write-Step "En el teléfono, abrí este link con tu cuenta de tester: $link"; Copy-Text $link "Link de prueba" }
    else { Write-Step "En el teléfono, abrí el 'link para unirse' de Play Console (Prueba interna > Verificadores)." }
    Write-Step "'Aceptar invitación' > 'Descargar en Google Play' > Instalar."
    Write-Step "Abrí JobPilot: cargá la clave de Gemini, conectá Google otra vez e importá tu archivo (Importar y combinar)."
    Wait-Enter "Cuando la tengas instalada, apretá Enter"
    Set-StateValue $State "step9" $true
}

# ---------------------------------------------------------------- menú

$Steps = @(
    @{ Key = "step1"; Name = "Herramientas (GitHub CLI y Java)"; Run = { param($s) Step-Tools $s } },
    @{ Key = "step2"; Name = "Clave de firma y secretos de GitHub"; Run = { param($s) Step-SigningKey $s } },
    @{ Key = "step3"; Name = "Huellas SHA-1/SHA-256 de tu clave"; Run = { param($s) Step-Fingerprints $s } },
    @{ Key = "step4"; Name = "Generar el primer paquete (.aab)"; Run = { param($s) Step-FirstBundle $s } },
    @{ Key = "step5"; Name = "Play Console: crear la app y subir el paquete (guiado)"; Run = { param($s) Step-PlayConsole $s } },
    @{ Key = "step6"; Name = "Cuenta de servicio para publicar desde GitHub"; Run = { param($s) Step-ServiceAccount $s } },
    @{ Key = "step7"; Name = "Google (Gmail/Calendar) con la clave de Play"; Run = { param($s) Step-GoogleSignIn $s } },
    @{ Key = "step8"; Name = "Publicar una versión nueva"; Run = { param($s) Step-Publish $s } },
    @{ Key = "step9"; Name = "Instalar en tu teléfono"; Run = { param($s) Step-Install $s } }
)

$state = Get-State
while ($true) {
    Write-Title "JobPilot > Google Play (prueba interna, solo vos)"
    $next = $null
    for ($i = 0; $i -lt $Steps.Count; $i++) {
        $done = [bool](Get-StateValue $state $Steps[$i].Key)
        if (-not $done -and $null -eq $next) { $next = $i + 1 }
        $mark = if ($done) { "[x]" } else { "[ ]" }
        $color = if ($done) { "DarkGray" } else { "White" }
        Write-Host ("  {0} {1}. {2}" -f $mark, ($i + 1), $Steps[$i].Name) -ForegroundColor $color
    }
    Write-Host "      0. Salir"
    Write-Host ""
    $prompt = if ($next) { "Paso a seguir [$next]" } else { "Todo listo. Paso (8 para publicar otra versión, 0 para salir)" }
    $choice = Read-Host $prompt
    if ([string]::IsNullOrWhiteSpace($choice)) { $choice = if ($next) { "$next" } else { "0" } }
    if ($choice -eq "0") { break }
    $number = 0
    if (-not [int]::TryParse($choice, [ref]$number) -or $number -lt 1 -or $number -gt $Steps.Count) {
        Write-Warn "Elegí un número de la lista."
        continue
    }
    try { & $Steps[$number - 1].Run $state }
    catch { Write-Bad $_.Exception.Message }
}
