# Heartbeat — App de escritorio

NASA Space Apps 2026. App WPF de autoevaluación de salud para astronautas (funciona sin conexión y sincroniza con la API cuando hay enlace).

> Herramienta de autoevaluación, no diagnóstico médico.

El backend, los contratos y **todos los tickets** viven en [AliaJimenez/HeartbeatBackend](https://github.com/AliaJimenez/HeartbeatBackend). Este repo contiene solo el cliente de escritorio.

## Requisitos

- Windows 10/11
- .NET SDK 10.0.x
- Git (el código compartido llega como submódulo)

## Clonar

```bash
git clone --recurse-submodules https://github.com/IsaiasMorillo/HeartbeatFrontDesktop.git
```

Si ya clonaste sin submódulos:

```bash
git submodule update --init
```

Para traer cambios nuevos de `Heartbeat.Shared` / `Heartbeat.Client` (y luego commitear el puntero actualizado):

```bash
git submodule update --remote external/HeartbeatBackend
```

> No edites nada dentro de `external/HeartbeatBackend`: los cambios a Shared van como PR en `HeartbeatBackend`.

## Compilar, probar y ejecutar

```bash
dotnet build HeartbeatDesktop.sln
dotnet test HeartbeatDesktop.sln
dotnet run --project src/Heartbeat.Desktop/Heartbeat.Desktop.csproj
```

## Estructura

| Ruta | Contenido |
| --- | --- |
| `src/Heartbeat.Desktop` | App WPF (`net10.0-windows`) |
| `tests/Heartbeat.Desktop.Tests` | Pruebas xUnit de navegación, barra de estado y DI |
| `external/HeartbeatBackend` | Submódulo: `Heartbeat.Shared` y `Heartbeat.Client` |

### Arquitectura de `Heartbeat.Desktop`

- **MVVM** con [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- **DI** con `Microsoft.Extensions.Hosting`; todo el registro está en `ServiceCollectionExtensions.AddHeartbeatDesktop()`.
- **Navegación ViewModel primero**: `INavigationService.NavigateTo<TViewModel>()`. Cada página se crea en su propio scope de DI y se libera al salir. La vista de cada ViewModel se declara en `Views/PageTemplates.xaml`.
- **Tema** en `Themes/` (claro, paleta de marca: blanco ~70 %, azul `#110559` ~20 %, rojo `#990E0E` ~10 %). El semáforo verde / ámbar / rojo (NR-01) vive en `Converters/StatusPalette.cs`: verde y ámbar significan solo normal / watch, y el estado nunca se comunica solo con color (cada chip lleva su texto). Chrome propio de ventana, rail azul de navegación, tarjetas con elevación y animación de entrada por página.
- **Check-up** (`CheckUpViewModel`, #28): de 1 a 6 indicadores en una sola sesión (los campos vacíos se omiten). Presión (sistólica y diastólica juntas), pulso, glucosa y temperatura por teclado; sueño por teclado o deslizador (paso de 0,5 h) y ánimo con 5 botones. Cada valor se valida con `IHealthEvaluator.Validate` antes de guardar y el resultado se evalúa al instante en el equipo. Se guarda en SQLite (`LocalHealthRecordStore`) con `SyncState = pending`, la versión de rangos y la nota opcional.
- **Acceso** (`AuthService`, #27): el primer acceso de cada astronauta es **en línea** (`POST /auth/login`); la app guarda su perfil, un hash PBKDF2-SHA256 propio (210 000 iteraciones, sal por cuenta) en `LocalAccount` y los tokens protegidos con **DPAPI** (usuario actual de Windows) en `TokenCache`. Sin conexión se valida la contraseña contra ese hash y se abre la sesión con el perfil guardado, devolviendo los tokens al cliente para que la sincronización retome al volver el enlace. Una respuesta 401/400/403 del servidor es definitiva (no se cae al hash local); si el servidor no responde, se usa la cuenta local si existe. Los datos se filtran por el `AstronautId` del servidor, así que varios astronautas pueden compartir el equipo sin ver datos ajenos. Cerrar sesión borra los tokens y conserva la cuenta local. Es un mecanismo de nivel demostración, como declara el contrato.
- **Sincronización** (`SyncEngine`, #32): servicio en segundo plano que sube lo pendiente sin bloquear la interfaz. Sincroniza al abrir la sesión, tras guardar (check-up, mensaje o perfil), al volver el enlace y cada 60 s con conexión, siempre para el astronauta con la sesión abierta. Sube los check-ups en lotes de hasta 500, del más viejo al más nuevo (con sus mensajes y reportes, sin adelantarse al check-up que adjuntan, y la edición de perfil); marca `synced` solo los Id que el servidor devuelve en `accepted`, y `rejected` —con el motivo— los que rechaza (no se reintentan; se ven en el historial y en Mensajes y la barra de estado los cuenta); guarda `serverTime` como `lastPulledAt` (`SyncCursor`); baja los mensajes de control de misión; y si `rangesUpdateAvailable` descarga los rangos, los valida y los guarda (`RangesCache`) para usarlos en el próximo arranque. Si el servidor no responde, el token caducó o no hay red, lo pendiente se conserva y se reintenta; la barra de estado lo dice y tiene un botón **Sincronizar ahora**. En modo `Demo` el servidor de mentira acepta todo, y rechaza lo que contenga `[rechazar]` para poder ver un rechazo.
- **Perfil** (`ProfileViewModel`, #34): el astronauta ve su cuenta y misión y edita solo su `displayName`, `callSign` y `notes` (límites 100 / 30 / 500 del contrato), también sin conexión. La edición queda pendiente con su `updatedAt` dentro de `LocalAccount.ProfileJson` (`ProfileCache`; no cambia el esquema de la base) y cuenta como pendiente en la barra de estado. `IProfileStore.GetPendingEdit` la entrega como `UpdateProfileRequest` para `SyncRequest.Profile`; al sincronizar, `MarkSynced` la limpia. Gana la edición más reciente (el servidor aplica `updatedAt >=`): un perfil del servidor posterior descarta la edición local al volver a entrar en línea.
- **Dashboard** (`DashboardViewModel`, #30): el estado actual de los 6 indicadores en una pantalla. Arriba el estado general (el peor de las últimas lecturas) y qué requiere atención; una tarjeta por indicador con su último valor, unidad, estado en color y texto, y hora («Hoy, 19:45», «Ayer…»); aviso destacado si hay críticos; y el botón **Nuevo check-up**. Como un check-up puede omitir indicadores, cada tarjeta usa la lectura más reciente de su indicador.
- **Resultado** (`CheckUpResultViewModel`, #29): al guardar un check-up se abre su pantalla de resultado, con el estado y la etiqueta de cada lectura (color + texto), la guía de las que no están en rango, el aviso destacado de escalamiento si hay críticos (a quién contactar y qué indicadores) y el botón **Enviar reporte a control de misión**, que encola un mensaje `report` con el check-up adjunto (uno por check-up; sin enlace sale al reconectar).
- **Modo demostración** (`Demo/`): mientras no estén los tickets reales hay un acceso de demostración (`ana`, `luis`, `mara` con contraseña `demo123`, sustituye #27), un historial de ejemplo sembrado en la base local la primera vez y una sincronización simulada (sustituye #32). Las pantallas solo conocen `IAuthenticator`, `IHealthRecordStore` e `ISyncStatusProvider`; el check-up se evalúa con el `HealthEvaluator` real de `Heartbeat.Shared`. El interruptor **Enlace con la Tierra** de la barra superior simula perder el enlace: apagado, los check-ups y mensajes quedan *Pendientes* y salen al volver a encenderlo.
- **Barra de estado**: en línea / sin conexión, último sync y pendientes. Se alimenta de `IConnectivityService` e `ISyncStatusProvider`; el motor de sync (#32) sustituirá la implementación provisional.

- **Panel de información y fuentes** (`Info/`): por indicador explica por qué importa, cómo medir y riesgos, y lista las fuentes NASA / agencias con enlaces que abren en el navegador. El contenido sale de `reference-ranges.json` (copia embebida en `Heartbeat.Shared`) a través de `IIndicatorInfoProvider`, con lectura estricta: una fuente no definida o con URL que no sea http(s) hace fallar las pruebas en vez de mostrar una ficha incompleta. Se abre en una ventana aparte, no modal y única, con `IIndicatorInfoPresenter.Show(indicatorId)`; el dashboard ya lo usa, y **el formulario de check-up (#30) solo tiene que inyectar `IIndicatorInfoPresenter` y llamar a `Show("heartRate")` desde su botón de ayuda** (al ser otra ventana, no se pierde lo escrito en el formulario).

### Añadir una pantalla

1. Crear `XxxViewModel : PageViewModel` y registrarlo con `AddTransient` en `ServiceCollectionExtensions`.
2. Crear su vista y asociarla en `Views/PageTemplates.xaml`.
3. Si va en el menú lateral, añadirla a `MainWindowViewModel.NavigationItems`.

## Servidor real o modo demostración

`src/Heartbeat.Desktop/appsettings.json` (sección `CrewApi`) decide con quién habla la app. Por defecto es **`Demo`**: tres astronautas ficticios (`ana`, `luis`, `mara`, contraseña `demo123`), sin servidor y con un historial de ejemplo. Para usar la Crew API real:

```bash
# PowerShell, con la API corriendo (por defecto en http://localhost:5027)
$env:CrewApi__Mode = "Live"
$env:CrewApi__BaseUrl = "http://localhost:5027/"
dotnet run --project src/Heartbeat.Desktop/Heartbeat.Desktop.csproj
```

En modo `Live` no se siembran datos de ejemplo y se entra con las cuentas de astronauta del servidor. El interruptor **Enlace con la Tierra** (también en el login) simula perder la conexión para probar el acceso sin conexión.

## Flujo de trabajo

- `dev` es la rama por defecto y **solo cambia por pull request**. `main` no se usa.
- Cada ticket va en su propia rama, `<n>-descripcion-corta` (por ejemplo `26-estructura-wpf`), que sale de `dev` y vuelve a `dev` por PR.
- El PR se cierra con `Closes AliaJimenez/HeartbeatBackend#<n>` en la descripción.
- CI: `dotnet build` y `dotnet test` en cada PR y push hacia `dev`.

Más detalle en [`docs/FLUJO-DE-TICKETS.md`](https://github.com/AliaJimenez/HeartbeatBackend/blob/dev/docs/FLUJO-DE-TICKETS.md).
