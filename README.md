Aquí tienes la traducción al español del archivo "README.md":

# JobSearch Copilot



Un asistente local de un solo usuario para la búsqueda de empleo remoto, construido con **.NET 10, Blazor (Interactive Server) y MudBlazor**, usando la **API de Gemini** como su único LLM.

JobSearch Copilot prepara, analiza y redacta; tú apruebas. Nunca envía una postulación, correo electrónico o mensaje en tu nombre. Todos los datos permanecen en tu máquina.

> **Estado:** las 9 fases han sido entregadas (integración con Google y resumen de correo incluidos). Pendiente: verificaciones con tu propia clave de Gemini y cuenta de Google. Consulta [`docs/PLAN.md`](https://www.google.com/search?q=docs/PLAN.md) para ver la hoja de ruta y los criterios de aceptación por fase. La interfaz de usuario está en español.
> 
> 

## Funciones planificadas



* **CV Maestro**: importa un CV en PDF, DOCX o Markdown a un modelo estructurado y editable. Es la única fuente de la verdad.


* **Ingesta de empleos** desde fuentes cuyos términos lo permiten (API públicas, feeds RSS y portales de empleo ATS públicos), además de entrada manual. Sin web scraping de LinkedIn.


* **Análisis y clasificación** de cada oferta (puntuación de idoneidad, requisitos cumplidos o faltantes, compensación solo cuando se publica, modelo de contrato, elegibilidad, banderas rojas o señales de alerta) con pesos configurables.


* **CV y carta de presentación adaptados** donde cada viñeta se remonta al CV maestro, exportados a DOCX y PDF amigables para ATS.


* **Pipeline** Kanban, recordatorios de seguimiento y exportación `.ics`.


* **Investigación de empresas** con fuentes citadas, **borradores de contacto (outreach)**, **práctica de entrevistas** y un asistente de **negociación**.


* **Resumen diario de tu correo**: las alertas de empleo de LinkedIn se convierten en ofertas, el correo de los reclutadores y las invitaciones se convierten en tarjetas de "esperando tu respuesta" vinculadas a tus postulaciones. Basado en reglas, de solo lectura, bajo demanda.



## Requisitos



* [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100 o posterior).


* Una clave de la API de Gemini (de la fase 1). Se recomienda una clave de nivel de pago: en el nivel gratuito, Google puede usar tus prompts, que contienen tu CV, para mejorar sus productos.



## Ejecución



```bash
dotnet run --project src/JobPilot.Web

```

Luego abre [http://localhost:5080](http://localhost:5080). La aplicación escucha únicamente en la interfaz loopback. Cambia el puerto con `JobPilot:Port` en `appsettings.json` o la variable de entorno `JobPilot__Port`.

## Pruebas



```bash
dotnet build JobPilot.slnx
dotnet test --solution JobPilot.slnx

```

Las advertencias se tratan como errores. Las pruebas usan xUnit v3 en Microsoft.Testing.Platform (configurado en `global.json`).

## Configuración



### Clave de la API de Gemini



Pégala en **Configuración → Conexión con Gemini** y elige **Guardar clave** (crea una en [Google AI Studio](https://aistudio.google.com/apikey)). Se almacena encriptada en la carpeta de datos (`gemini-key.bin`), nunca en la base de datos o en archivos de exportación, y nunca se vuelve a mostrar. Podés reemplazarla o borrarla desde la misma pantalla en cualquier momento.

O guárdala con [secretos de usuario](https://learn.microsoft.com/aspnet/core/security/app-secrets) (`%APPDATA%\Microsoft\UserSecrets\jobpilot-web-local\secrets.json`, fuera del repositorio). La clave guardada desde la app tiene prioridad; la de `secrets.json` se usa cuando no hay otra, y la pantalla muestra cuál se está usando:

```bash
dotnet user-secrets set "Gemini:ApiKey" "<tu clave>" --project src/JobPilot.Web

```

Las variables de entorno (`GEMINI_API_KEY`, `Gemini__ApiKey`) no se leen. La clave se envía en un encabezado HTTP y nunca se muestra en la interfaz de usuario ni se escribe en los registros. Compruébala en **Configuración → Conexión con Gemini → Probar conexión**.

### Modelos, parámetros y precios



`src/JobPilot.Web/appsettings.json`:

* `Llm:DefaultModel` y `Llm:UseCases:<use-case>` (modelo, nivel de razonamiento, temperatura, tokens de salida máximos). Pueden ser anulados desde **Configuración → Modelos por caso de uso** (almacenado en la base de datos local).


* `Llm:Pricing:<model>` en USD por millón de tokens, usado para estimaciones de costos. Actualízalo cuando Google cambie sus precios; los modelos sin precio muestran "sin precio configurado".


* `Gemini:*`: URL base, reintentos y tiempos de espera. `ConnectTimeoutSeconds` (por defecto 10) limita cuánto tiempo puede tomar abrir la conexión: si tu red no puede alcanzar a Google (IPv6 roto, firewall, VPN, proxy), la acción falla con "No se pudo conectar con Gemini" después de ese tiempo en lugar de esperar el tiempo de espera de respuesta de Gemini (120 s por intento).



### Datos locales



La base de datos SQLite se encuentra en `%LocalAppData%\JobPilot` (Windows), `~/.local/share/JobPilot` (Linux) o `~/Library/Application Support/JobPilot` (macOS). Cámbiala con `JobPilot:DataDirectory`. Las migraciones se aplican automáticamente al inicio. Para crear una nueva (desarrolladores):

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/JobPilot.Web --output-dir Infrastructure/Persistence/Migrations

```

### Perfil y CV maestro



**Perfil y CV maestro → Perfil** comienza con los valores en `src/JobPilot.Web/Seed/profile.seed.json` y se edita desde la aplicación. **CV maestro → Elegir archivo** importa un CV en PDF, DOCX o Markdown: el texto se extrae localmente, se envía a Gemini solo después de que confirmes (el prompt completo se muestra primero), y vuelve como un borrador. Los valores que no se pueden encontrar en tu documento se marcan; tú revisas y editas todo antes de guardar. Cada guardado crea una versión que puedes restaurar. Tu archivo de CV no se almacena; solo el CV estructurado, en la base de datos local.

### Ofertas de empleo



**Ofertas → Buscar** lee las fuentes habilitadas (Remotive, Remote OK, Himalayas, Jobicy, We Work Remotely, Get on Board, Working Nomads, Jobspresso y los portales públicos de Greenhouse, Lever y Ashby de las empresas seguidas). Solo se mantienen las ofertas que coinciden con tus palabras clave; los duplicados entre fuentes se fusionan y cada oferta mantiene su enlace y fuente originales. Cada fuente se consulta como máximo con la frecuencia que permiten sus términos. **Agregar oferta** importa una URL de ATS a través de su API pública o extrae los campos de un texto pegado. Abre una oferta y usa **Analizar con Gemini** para obtener la puntuación de idoneidad, requisitos, palabras clave faltantes, compensación (solo cuando se publica), modelo de contrato, elegibilidad desde tu país y banderas rojas. Los pesos de clasificación, el piso/objetivo de compensación y los tipos de cambio se encuentran en **Ofertas → Preferencias**.

### CV y carta de presentación adaptados



En la página de una oferta, **Postulación** genera una variante del CV y una carta de presentación a partir de tu CV maestro. Cada texto cita los elementos del CV maestro de donde proviene; los números y tecnologías que no se pueden rastrear hasta ellos se marcan (el modelo tiene un reintento con las razones primero). La empresa, los títulos y las fechas siempre provienen del CV maestro. Revisa las diferencias, edita y aprueba; los textos marcados deben ser corregidos, eliminados o confirmados explícitamente por ti. Después de la aprobación puedes descargar archivos **DOCX** amigables para ATS (una columna, sin tablas ni imágenes) y **PDF** (fuente incrustada, texto seleccionable).

### Pipeline y recordatorios



En la página de una oferta, **Agregar al pipeline** la añade al Kanban del **Pipeline** (Detectada, Interesa, Postulada, Entrevista, Oferta, Rechazada, Descartada). Arrastra las tarjetas entre las columnas o usa su menú; cada cambio se registra en la línea de tiempo de la postulación, junto con notas, contactos, entrevistas y qué CV y carta de presentación aprobados enviaste (se guarda una copia de lo enviado y se puede volver a descargar). Los recordatorios de seguimiento se calculan cuando abres la aplicación (por defecto 7 días sin noticias después de postularte, 3 después de una entrevista, máximo 2 seguimientos por etapa; editable en **Reglas de seguimiento**), o en una fecha que elijas. **Follow-up** (Seguimiento) redacta un correo electrónico corto con Gemini a partir de tu CV y los datos de la postulación; lo revisas, lo copias y lo envías tú mismo, luego lo marcas como enviado. **Exportar calendario (.ics)** descarga las próximas entrevistas y recordatorios para cualquier aplicación de calendario.

### Perfiles de empresas



**Empresas** mantiene un perfil por empresa, construido solo a partir de fuentes que elijas: URL públicas (Gemini las lee con su herramienta de contexto de URL) y textos que pegues. Cada dato mantiene su URL de origen y una cita literal. Un dato está **verificado** cuando Gemini reporta haber leído su URL o su cita está en tu texto pegado; de lo contrario se muestra como **no verificado** con la razón (incluidos los números que no están en la cita). No se utiliza la verificación (grounding) con la Búsqueda de Google: sus términos no permiten almacenar o anotar sus resultados. El perfil enlaza con las ofertas de la empresa.

### Contacto (Outreach)



**Outreach → Mensajes** redacta una nota de invitación de LinkedIn (200 caracteres por defecto, `Outreach:ConnectionNoteMaxLength`), un mensaje de LinkedIn o un correo electrónico para un reclutador o gerente de contratación, en tres tonos. Las afirmaciones sobre ti deben provenir de tu CV maestro y las afirmaciones sobre la empresa de hechos verificados o de la publicación; cualquier otra cosa se marca. Copia el texto, envíalo tú mismo y márcalo como enviado (se añade a la línea de tiempo de la oferta). **Perfil de LinkedIn** revisa el titular y el Acerca de que pegues y propone versiones que citan tu CV. JobSearch Copilot nunca envía mensajes y nunca accede a LinkedIn.

### Entrevistas



**Entrevistas → Preparación**: para una oferta, un banco de posibles preguntas (cada una con la oración de la publicación que la motiva; las citas no encontradas en la publicación se marcan) y las historias STAR construidas solo a partir de tu CV maestro (cada parte cita sus elementos del CV; los números o tecnologías no rastreables se marcan y se pueden editar o confirmar). **Simulacros**: una entrevista simulada en inglés con Gemini como el entrevistador, una pregunta por mensaje y un número limitado de seguimientos (reglas aplicadas por la aplicación). Al final, retroalimentación con rúbrica (estructura, relevancia, evidencia, impacto, comunicación; puntuación general calculada por la aplicación) y respuestas modelo de tu CV. El historial de puntuaciones se grafica.

### Negociación



**Negociación → Comparador**: ingresa las ofertas que recibiste (base por año, mes u hora, bono, tu propia valoración de beneficios y capital, modelo de contrato). Se normalizan a USD anuales con tus tipos de cambio y tus costos de contratista; no se estima nada. **Guion**: un guion de negociación a partir de tus números y tu CV, con los números no encontrados en ellos marcados. **Simulacro**: un juego de roles con Gemini como el reclutador, partiendo de tu oferta, con retroalimentación por rúbrica.

### Panel



**Panel** muestra las ofertas detectadas, postulaciones, tasa de respuesta, tiempo promedio hasta la primera respuesta y por etapa, seguimientos pendientes, próximas entrevistas y el costo estimado del LLM por caso de uso.

### Google (opcional)



**Configuración → Google** tiene un asistente de configuración paso a paso: un proyecto de Google Cloud con la API de Gmail y la API de Google Calendar habilitadas, una pantalla de consentimiento de OAuth (Externa, tu cuenta como usuario de prueba) y un cliente OAuth de tipo **Aplicación web** con el URI de redireccionamiento `http://localhost:5080/google/callback` (con un botón de copiar). Cuando Google muestre el nuevo cliente, elige **Descargar JSON** (Google muestra el secreto solo una vez) y carga ese archivo en el asistente, o pega la identificación y el secreto (ambos a la vez también funciona): se almacenan encriptados en tu carpeta de datos (`google-client.bin`, nunca en la base de datos, el repositorio o una exportación) y funcionan sin reiniciar. Los secretos de usuario (`Google:ClientId`, `Google:ClientSecret`) todavía funcionan y tienen prioridad. JobSearch Copilot solicita tres alcances (scopes):

* `gmail.readonly`: busca y lee hilos que vincules a una postulación (solo se almacena el id del hilo y el asunto; los mensajes se leen bajo demanda y nunca se envían a Gemini).


* `gmail.compose`: crea **borradores** de seguimientos y correos electrónicos de contacto, opcionalmente como respuesta en un hilo vinculado. Google describe este alcance como "gestionar borradores y enviar correos electrónicos"; JobSearch Copilot nunca envía (una prueba falla si aparece alguna llamada de envío).


* `calendar.app.created`: crea un calendario separado de "JobSearch Copilot" y añade las entrevistas a él, después de que confirmes cada una. JobSearch Copilot no puede ver ni cambiar tu calendario principal.



Elige **Solo Calendar** antes de conectar para pedir únicamente `calendar.app.created`. Úsalo cuando el acceso a Gmail esté bloqueado: el Programa de Protección Avanzada de Google solo permite a las aplicaciones de terceros verificadas leer Gmail, y JobSearch Copilot usa tu propio cliente OAuth no verificado, por lo que Google responde "Error 400: policy_enforced". Sin Gmail, copia los borradores con **Copiar** y registra las respuestas a mano. **Agregar Gmail** vuelve a conectar con los tres alcances más adelante.

Cada acción muestra exactamente qué se enviará a Google primero. El token se almacena encriptado en tu carpeta de datos. Con la pantalla de consentimiento en estado "Testing", Google expira el permiso después de 7 días y JobSearch Copilot te pide que te vuelvas a conectar. Para evitar eso, publícala (Google Auth Platform → Audience → Publish app → "In production") sin enviarla a verificación: Google permite aplicaciones no verificadas para uso personal (menos de 100 usuarios); haces clic en la advertencia "Google no ha verificado esta aplicación" al conectar. Fuentes: [vencimiento del token de actualización](https://www.google.com/search?q=https://developers.google.com/identity/protocols/oauth2%23expiration), [cuándo no se necesita verificación](https://support.google.com/cloud/answer/13464323). **Desconectar y revocar** revoca el permiso en Google y elimina el token local.

### Bandeja de entrada de correo y resumen diario



La sección **Hoy** del Panel es el resumen matutino de JobSearchDesk dentro de JobSearch Copilot. **Actualizar correo** lee tu Gmail (el permiso `gmail.readonly`) desde la última comprobación (el último día la primera vez, máximo 4 días atrás) más el correo no leído relacionado con empleos de los últimos 5 días, y clasifica cada hilo en exactamente una categoría:

* **Alertas de LinkedIn**: los roles se leen únicamente del correo de alerta (nunca se extrae de linkedin.com), los duplicados entre ciudades y alertas se fusionan, y cada rol obtiene una idoneidad (Muy buena / Puede ser / Floja) con las reglas que se activaron. Los roles que pasan los filtros se convierten en ofertas de la fuente "LinkedIn (alertas por mail)", con el enlace canónico. La alerta no tiene descripción: abre la oferta, pega el texto en **Descripción** y luego analízalo.


* **Te esperan**: correo de reclutadores, InMails y mensajes de LinkedIn, programación de entrevistas, pasos en un sistema de contratación e invitaciones para responder, cada uno con quién escribió, una línea de tiempo basada en hechos y una próxima acción. **Registrar en <empresa>** vincula el hilo a la postulación correspondiente y registra la respuesta en su línea de tiempo; **Listo** oculta la tarjeta hasta que llegue un nuevo mensaje en el hilo. Las invitaciones se evalúan solo a partir del texto del correo electrónico (no se lee tu calendario), así que marca una invitación como lista una vez que hayas respondido.


* **Filtrados**: todo lo demás, contado por categoría con remitentes de ejemplo, para que nada desaparezca sin ser visto.



Las categorías siempre suman el total de hilos leídos; un control que rompa esto se detiene en lugar de mostrar un resumen parcial. JobSearch Copilot solo lee: nunca envía, archiva, etiqueta o marca el correo como leído. Solo se almacenan los hechos (id del hilo, remitente, asunto, fechas, qué regla coincidió); los cuerpos de los mensajes nunca se almacenan ni se envían a Gemini.

**Configuración → Reglas de correo** contiene las reglas en JSON (inicializadas desde `Seed/mail-rules.seed.json`, que no tiene datos personales). Añade tu ciudad a `sets.home`, las empresas que te gustan a `sets.domainCompanies`, y un piso de pago y tipos de cambio bajo `scoring.pay` si quieres que se compare el pago indicado (sin ellos, el pago no se compara; no se estima nada). Las reglas se validan antes de guardar.

### Registro de llamadas LLM



Cada solicitud enviada a Gemini (incluyendo reintentos, fallas y cancelaciones) se registra con su caso de uso, modelo, versión del prompt, tokens, latencia y costo estimado: **Configuración → Registro de llamadas**.

## Privacidad



* Tu base de datos, CVs, exportaciones y secretos se almacenan fuera del repositorio y son ignorados por git (git-ignored).


* `scripts/check-no-personal-data.sh` se ejecuta en la integración continua (CI) y falla si se está por hacer un commit de una base de datos, un secreto, un documento fuera de los archivos de prueba, una clave API o una dirección de correo electrónico real. Para ejecutarlo antes de cada commit, y para agregar tu propia lista de denegación de cadenas personales en un archivo `.pii-denylist` ignorado por git (una por línea):


```bash
git config core.hooksPath .githooks

```


* Antes de cada llamada a Gemini, la interfaz de usuario mostrará lo que se está enviando.



## Fuentes de datos de empleo



Los listados de empleo pertenecen a sus publicadores. JobSearch Copilot muestra la fuente y el enlace original para cada listado y respeta los términos y límites de velocidad de cada proveedor (por ejemplo, Remotive, Remote OK, Himalayas y Jobicy requieren atribución y un enlace de retroceso).

## Aplicación de escritorio de Windows



[`windows/`](https://www.google.com/search?q=windows/README.md) contiene una versión para Windows: esta misma aplicación en su propia ventana (WebView2), con un acceso directo en el menú Inicio y un instalador, sin pestaña en el navegador y sin necesidad del SDK de .NET. Comparte la carpeta de datos con `dotnet run`. El instalador es construido por `.github/workflows/windows.yml`; pasos de descarga e instalación: [`windows/README.md`](https://www.google.com/search?q=windows/README.md).

## Aplicación de Android



[`android/`](https://www.google.com/search?q=android/README.md) contiene la versión para Android (.NET MAUI Blazor Hybrid, Android 8.0+), construida en fases con su propia solución, pruebas y flujo de trabajo de CI (`.github/workflows/android.yml`). Reutiliza `JobPilot.Domain`, mantiene sus propios datos en el teléfono e intercambia datos con esta aplicación a través de un archivo de exportación/importación. Instalación y firma: [`android/README.md`](https://www.google.com/search?q=android/README.md); Google Play (prueba interna) con un asistente de Windows: [`android/docs/PLAY.md`](https://www.google.com/search?q=android/docs/PLAY.md).

**Mover datos entre la PC y el teléfono:** Configuración → **Exportar / Importar**. "Exportar" descarga un archivo JSON con todos tus datos de JobSearch Copilot (nunca la clave de Gemini ni la conexión con Google). En el otro dispositivo, "Importar y combinar" muestra lo que cambiaría y, luego de confirmar, agrega lo que falta, conserva el cambio más reciente cuando algo se modificó en ambos, toma el valor del archivo para ediciones sin fecha y aplica lo que eliminaste. Tus datos se copian a `backups/` en la carpeta de datos primero. El archivo no está encriptado: mantenlo privado y elimínalo después de importar.

## Licencia

[MIT](https://www.google.com/search?q=LICENSE)

------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

# JobSearch Copilot

A local, single-user assistant for a remote job search, built with **.NET 10, Blazor (Interactive Server)
and MudBlazor**, using the **Gemini API** as its only LLM.

JobSearch Copilot prepares, analyses and drafts; you approve. It never sends an application, e-mail or message on
your behalf. All data stays on your machine.

> **Status:** all 9 phases delivered (Google integration and the mail brief included). Pending: checks with your own Gemini key and Google account. See [`docs/PLAN.md`](docs/PLAN.md) for the roadmap and
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
- **Daily brief from your mail**: LinkedIn job alerts become offers, recruiter mail and invites become "waiting on you"
  cards linked to your applications. Rule-based, read-only, on demand.

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

Paste it in **Configuración → Conexión con Gemini** and choose **Guardar clave** (create one in
[Google AI Studio](https://aistudio.google.com/apikey)). It is stored encrypted in the data folder (`gemini-key.bin`), never in
the database or in export files, and never shown back. You can replace or delete it from the same screen at any time.

Or store it with [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
(`%APPDATA%\Microsoft\UserSecrets\jobpilot-web-local\secrets.json`, outside the repository). The key saved in the app takes
priority; the one in `secrets.json` is used when there is no other, and the screen shows which one is in use:

```bash
dotnet user-secrets set "Gemini:ApiKey" "<your key>" --project src/JobPilot.Web
```

Environment variables (`GEMINI_API_KEY`, `Gemini__ApiKey`) are not read. The key is sent in an HTTP
header and is never shown in the UI or written to logs. Check it in **Configuración → Conexión con Gemini →
Probar conexión**.

### Models, parameters and prices

`src/JobPilot.Web/appsettings.json`:

- `Llm:DefaultModel` and `Llm:UseCases:<use-case>` (model, thinking level, temperature, max output tokens).
  They can be overridden from **Configuración → Modelos por caso de uso** (stored in the local database).
- `Llm:Pricing:<model>` in USD per million tokens, used for cost estimates. Update it when Google changes its
  prices; models without a price show "sin precio configurado".
- `Gemini:*`: base URL, retries and timeouts. `ConnectTimeoutSeconds` (default 10) limits how long opening the connection
  may take: if your network cannot reach Google (broken IPv6, firewall, VPN, proxy), the action fails with
  "No se pudo conectar con Gemini" after that time instead of waiting for Gemini's answer timeout (120 s per attempt).

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

### Job offers

**Ofertas → Buscar** reads the enabled sources (Remotive, Remote OK, Himalayas, Jobicy, We Work Remotely, Get on Board,
Working Nomads, Jobspresso and the
public Greenhouse, Lever and Ashby boards of followed companies). Only offers matching your keywords are kept;
duplicates across sources are merged and every offer keeps its original link and source. Each source is queried at
most as often as its terms allow. **Agregar oferta** imports an ATS URL through its public API or extracts the
fields of a pasted text. Open an offer and use **Analizar con Gemini** for the fit score, requirements, missing
keywords, compensation (only when published), contract model, eligibility from your country and red flags.
Ranking weights, the compensation floor/target and exchange rates are in **Ofertas → Preferencias**.

### Tailored CV and cover letter

On an offer page, **Postulación** generates a CV variant and a cover letter from your master CV. Every text cites the
master CV elements it comes from; numbers and technologies that cannot be traced to them are flagged (the model gets
one retry with the reasons first). Company, titles and dates always come from the master CV. Review the differences,
edit, and approve; flagged texts must be fixed, removed or explicitly confirmed by you. After approval you can download
ATS-friendly **DOCX** (one column, no tables or images) and **PDF** (embedded font, selectable text) files.

### Pipeline and reminders

On an offer page, **Agregar al pipeline** adds it to the **Pipeline** Kanban (Detectada, Interesa, Postulada, Entrevista,
Oferta, Rechazada, Descartada). Drag cards between columns or use their menu; every change is recorded in the application's
timeline, together with notes, contacts, interviews and which approved CV and cover letter you sent (a copy of what was
sent is kept and can be downloaded again). Follow-up reminders are computed when you open the app (by default 7 days
without news after applying, 3 after an interview, at most 2 follow-ups per stage; editable in **Reglas de seguimiento**),
or on a date you choose. **Follow-up** drafts a short e-mail with Gemini from your CV and the application's facts; you
review it, copy it and send it yourself, then mark it as sent. **Exportar calendario (.ics)** downloads upcoming interviews
and reminders for any calendar app.

### Company profiles

**Empresas** keeps a profile per company, built only from sources you choose: public URLs (Gemini reads them with its URL
context tool) and texts you paste. Every fact keeps its source URL and a verbatim quote. A fact is **verificado** when
Gemini reports reading its URL or its quote is in your pasted text; otherwise it is shown as **no verificado** with the
reason (including numbers that are not in the quote). Grounding with Google Search is not used: its terms do not allow
storing or annotating its results. The profile links to the company's offers.

### Outreach

**Outreach → Mensajes** drafts a LinkedIn invitation note (200 characters by default, `Outreach:ConnectionNoteMaxLength`),
a LinkedIn message or an e-mail for a recruiter or hiring manager, in three tones. Claims about you must come from your
master CV and claims about the company from verified facts or the posting; anything else is flagged. Copy the text, send
it yourself and mark it as sent (it is added to the offer's timeline). **Perfil de LinkedIn** reviews the headline and
About you paste and proposes versions that cite your CV. JobSearch Copilot never sends messages and never accesses LinkedIn.

### Interviews

**Entrevistas → Preparación**: for an offer, a bank of likely questions (each with the sentence of the posting that
motivates it; quotes not found in the posting are flagged) and STAR stories built only from your master CV (every part
cites its CV elements; untraceable numbers or technologies are flagged and can be edited or confirmed).
**Simulacros**: a mock interview in English with Gemini as the interviewer, one question per message and a bounded number
of follow-ups (rules enforced by the app). At the end, rubric feedback (structure, relevance, evidence, impact,
communication; overall score computed by the app) and model answers from your CV. The score history is charted.

### Negotiation

**Negociación → Comparador**: enter the offers you received (base per year, month or hour, bonus, your own valuation of
benefits and equity, contract model). They are normalized to annual USD with your exchange rates and your contractor
costs; nothing is estimated. **Guion**: a negotiation script from your numbers and your CV, with numbers not found in
them flagged. **Simulacro**: a roleplay with Gemini as the recruiter, starting from your offer, with rubric feedback.

### Dashboard

**Panel** shows offers detected, applications, response rate, average time to the first response and per stage, pending
follow-ups, upcoming interviews and the estimated LLM cost by use case.

### Google (optional)

**Configuración → Google** has a step-by-step setup wizard: a Google Cloud project with Gmail API and Google Calendar
API enabled, an OAuth consent screen (External, your account as test user) and an OAuth client of type
**Web application** with the redirect URI `http://localhost:5080/google/callback` (with a copy button). When Google shows
the new client, choose **Download JSON** (Google shows the secret only once) and load that file in the wizard, or paste the
id and secret (both at once works too): they are stored encrypted in your data folder (`google-client.bin`, never in the
database, the repository or an export) and work without restarting. User-secrets (`Google:ClientId`,
`Google:ClientSecret`) still work and take priority. JobSearch Copilot asks for three scopes:

- `gmail.readonly`: search and read threads that you link to an application (only the thread id and subject are stored;
  messages are read on demand and never sent to Gemini).
- `gmail.compose`: create **drafts** of follow-ups and e-mail outreach, optionally as a reply in a linked thread. Google
  describes this scope as "manage drafts and send emails"; JobSearch Copilot never sends (a test fails if any send call appears).
- `calendar.app.created`: create a separate "JobSearch Copilot" calendar and add interviews to it, after you confirm each one.
  JobSearch Copilot cannot see or change your primary calendar.

Choose **Solo Calendar** before connecting to ask only for `calendar.app.created`. Use it when Gmail access is blocked:
Google's Advanced Protection Program only lets verified third-party apps read Gmail, and JobSearch Copilot uses your own
unverified OAuth client, so Google answers "Error 400: policy_enforced". Without Gmail, copy drafts with **Copiar** and
record replies by hand. **Agregar Gmail** reconnects with all three scopes later.

Every action shows exactly what will be sent to Google first. The token is stored encrypted in your data folder. With
the consent screen in "Testing" status Google expires the permission after 7 days and JobSearch Copilot asks you to reconnect. To
avoid that, publish it (Google Auth Platform → Audience → Publish app → "In production") without submitting it for
verification: Google allows unverified apps for personal use (fewer than 100 users); you click through the "Google hasn't
verified this app" warning when connecting. Sources: [refresh token expiration](https://developers.google.com/identity/protocols/oauth2#expiration),
[when verification is not needed](https://support.google.com/cloud/answer/13464323).
**Desconectar y revocar** revokes the permission at Google and deletes the local token.

### Mail inbox and daily brief

The **Hoy** section of the Panel is JobSearchDesk's morning brief inside JobSearch Copilot. **Actualizar correo** reads your Gmail
(the `gmail.readonly` permission) since the last check (the last day the first time, at most 4 days back) plus unread
job-related mail from the last 5 days, and sorts every thread into exactly one bucket:

- **LinkedIn alerts**: roles are read from the alert e-mail only (linkedin.com is never fetched), duplicates across
  cities and alerts are merged, and each role gets a fit (Muy buena / Puede ser / Floja) with the rules that fired.
  Roles that pass the filters become offers from the source "LinkedIn (alertas por mail)", with the canonical link. The
  alert has no description: open the offer, paste the text in **Descripción** and then analyse it.
- **Te esperan**: recruiter mail, LinkedIn InMails and messages, interview scheduling, steps in a hiring system and
  invites to answer, each with who wrote, a factual timeline and one next action. **Registrar en &lt;empresa&gt;** links
  the thread to the matching application and records the response in its timeline; **Listo** hides the card until a new
  message arrives in the thread. Invites are judged from the e-mail copy only (your calendar is not read), so mark an
  invite as done once you answered it.
- **Filtrados**: everything else, counted by category with example senders, so nothing disappears unseen.

The buckets always add up to the threads read; a check that broke this stops instead of showing a partial brief.
JobSearch Copilot only reads: it never sends, archives, labels or marks mail as read. Only the facts are stored (thread id,
sender, subject, dates, which rule matched); message bodies are never stored or sent to Gemini.

**Configuración → Reglas de correo** holds the rules as JSON (seeded from `Seed/mail-rules.seed.json`, which has no
personal data). Add your city to `sets.home`, companies you like to `sets.domainCompanies`, and a pay floor and exchange
rates under `scoring.pay` if you want stated pay compared (without them pay is not compared; nothing is estimated). The
rules are validated before saving.

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

Job listings belong to their publishers. JobSearch Copilot shows the source and the original link for every
listing and respects each provider's terms and rate limits (for example Remotive, Remote OK, Himalayas
and Jobicy require attribution and a link back).

## Windows desktop app

[`windows/`](windows/README.md) holds a Windows version: this same app in its own window (WebView2), with a Start menu
shortcut and an installer, no browser tab and no .NET SDK needed. It shares the data folder with `dotnet run`. The installer
is built by `.github/workflows/windows.yml`; download and install steps: [`windows/README.md`](windows/README.md).

## Android app

[`android/`](android/README.md) holds the Android version (.NET MAUI Blazor Hybrid, Android 8.0+), built in phases
with its own solution, tests and CI workflow (`.github/workflows/android.yml`). It reuses `JobPilot.Domain`, keeps its
own data on the phone and exchanges data with this app through an export/import file. Installing and signing:
[`android/README.md`](android/README.md); Google Play (internal testing) with a Windows wizard:
[`android/docs/PLAY.md`](android/docs/PLAY.md).

**Moving data between the PC and the phone:** Configuración → **Exportar / Importar**. "Exportar" downloads one JSON file
with all your JobSearch Copilot data (never the Gemini key or the Google connection). On the other device, "Importar y combinar"
shows what would change and, after you confirm, adds what is missing, keeps the most recent change when something changed
on both, takes the file's value for edits without a date and applies what you deleted. Your data is copied to
`backups/` in the data folder first. The file is not encrypted: keep it private and delete it after importing.

## License

[MIT](LICENSE)
