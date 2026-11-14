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
- **Modo demostración** (`Demo/`): mientras no estén los tickets reales hay un acceso de demostración (`ana`, `luis`, `mara` con contraseña `demo123`, sustituye #27), un historial de ejemplo sembrado en la base local la primera vez y una sincronización simulada (sustituye #32). Las pantallas solo conocen `IAuthenticator`, `IHealthRecordStore` e `ISyncStatusProvider`; el check-up se evalúa con el `HealthEvaluator` real de `Heartbeat.Shared`. El interruptor **Enlace con la Tierra** de la barra superior simula perder el enlace: apagado, los check-ups y mensajes quedan *Pendientes* y salen al volver a encenderlo.
- **Barra de estado**: en línea / sin conexión, último sync y pendientes. Se alimenta de `IConnectivityService` e `ISyncStatusProvider`; el motor de sync (#32) sustituirá la implementación provisional.

- **Panel de información y fuentes** (`Info/`): por indicador explica por qué importa, cómo medir y riesgos, y lista las fuentes NASA / agencias con enlaces que abren en el navegador. El contenido sale de `reference-ranges.json` (copia embebida en `Heartbeat.Shared`) a través de `IIndicatorInfoProvider`, con lectura estricta: una fuente no definida o con URL que no sea http(s) hace fallar las pruebas en vez de mostrar una ficha incompleta. Se abre en una ventana aparte, no modal y única, con `IIndicatorInfoPresenter.Show(indicatorId)`; el dashboard ya lo usa, y **el formulario de check-up (#30) solo tiene que inyectar `IIndicatorInfoPresenter` y llamar a `Show("heartRate")` desde su botón de ayuda** (al ser otra ventana, no se pierde lo escrito en el formulario).

### Añadir una pantalla

1. Crear `XxxViewModel : PageViewModel` y registrarlo con `AddTransient` en `ServiceCollectionExtensions`.
2. Crear su vista y asociarla en `Views/PageTemplates.xaml`.
3. Si va en el menú lateral, añadirla a `MainWindowViewModel.NavigationItems`.

## Flujo de trabajo

- `dev` es la rama por defecto y **solo cambia por pull request**. `main` no se usa.
- Cada ticket va en su propia rama, `<n>-descripcion-corta` (por ejemplo `26-estructura-wpf`), que sale de `dev` y vuelve a `dev` por PR.
- El PR se cierra con `Closes AliaJimenez/HeartbeatBackend#<n>` en la descripción.
- CI: `dotnet build` y `dotnet test` en cada PR y push hacia `dev`.

Más detalle en [`docs/FLUJO-DE-TICKETS.md`](https://github.com/AliaJimenez/HeartbeatBackend/blob/dev/docs/FLUJO-DE-TICKETS.md).
