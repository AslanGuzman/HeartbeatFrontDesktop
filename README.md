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
- **Tema** en `Themes/`: verde / ámbar / rojo significan normal / watch / critical (NR-01) y no se usan para nada más; el estado nunca se comunica solo con color.
- **Barra de estado**: en línea / sin conexión, último sync y pendientes. Se alimenta de `IConnectivityService` e `ISyncStatusProvider`; el motor de sync (#32) sustituirá la implementación provisional.

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
