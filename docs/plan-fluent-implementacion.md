# Plan de implementación Fluent — GUI de Briple.Trainer

Fecha: 2026-10-16. Deriva de `docs/reporte-gui-metro-ux.md` y de la
decisión de abandonar Metro: Fluent nativo de Avalonia 12 + reglas del
skill `ui-design-rules`.

## Estado (2026-10-16): F0–F5 completas + guard de agente

- Build cero errores y cero warnings; fantomas aplicado.
- Suites: UI 28/28 (incluye historial, diálogo de confirmación, arranque
  libre y guard de agente), Core 111/111.
- Corrida visual manual pendiente por el usuario.

Guard de agente (2026-10-16, posterior a F5):

- `Routes.requiresAgent` es la puerta compartida por TODA ruta de datos
  (`/`, `/athletes/new`, `/athletes/:id`, `/generate`, `/review`): el
  token del proveedor es el acceso a la app; sin él la navegación va a
  `/login`. Solo `/login` y `/settings` quedan abiertos. Las rutas no
  usan caché, así que el guard corre en cada activación.
- El arranque dispara a `/`; el guard decide si eso aterriza o manda a
  conectar. Esto revierte el "arranque libre" de F4: sin token la app
  abre en el login (el CRUD local también queda detrás del token).
- El chequeo inline del wizard queda como defensa interna (cubre montaje
  directo del componente, p. ej. tests). El "Continuar sin conectar" del
  login se retiró: con el gate completo no tenía destino.

Uso de Navs (2026-10-16, corrección sobre lo anterior):

- `Routes.build` devuelve el `IRouter<Control>` de la librería:
  `AvaloniaRouter(table)` con la tabla de `Navs.Route.define<Control>`
  (aplicación explícita de tipo, como la documentación) y guards por
  pipe. El shell lo monta en un `RouterOutlet`; ya no se reconstruye con
  los controles `Routes`/`Route`.
- `Dialogs.host` intercambia contenido por suscripción explícita: el
  `.Content(observable)` de NXUI resuelve al setter de `object` y
  guardaba el observable como contenido.

Logging centralizado (2026-10-16):

- `Env.loggerFactory` es la única fábrica del proceso (puente a Serilog);
  existe antes que el env para los pasos de arranque.
- Servicios con logger propio, todos bajo el pipeline de la app:
  `AvaloniaRouter` (categoría `Navs.Router`), Migrondi (`Migrondi`, vía
  `Persistence.migrate(logger, dbPath)`), el agente de Core
  (`Briple.Trainer.Core.Agent`). El nivel se gobierna desde
  `configureLogging` (`MinimumLevel` y `MinimumLevel.Override`).

Notas de la pasada final:
- Escape del diálogo responde con foco dentro del diálogo (túnel por la
  capa); los botones Cancelar cubren el resto.
- El panel colapsa a riel por ancho de ventana (<700px), sin toggle
  manual todavía.
- `WizardBrief` mantiene su botón Generar dentro del formulario: un
  formulario envía al final, los CommandBar llevan los comandos de
  página. Hechos verificados contra los paquetes en el
cache de NuGet (Avalonia 12.0.5, Navs 1.0.0):

- `CommandBar`/`CommandBarButton` existen con tema Fluent completo
  (primarios inline + desborde `...` con `SecondaryCommands`).
- `Button` soporta la clase de estilo `accent`.
- No hay estilos tipográficos nombrados (Title/Caption): la rampa la
  definimos con factories propias.
- `Application.RequestedThemeVariant` cambia claro/oscuro en vivo.
- `WindowNotificationManager` + `NotificationCard` dan toasts Fluent.
- `SplitView` trae DisplayMode/pane/placement completos.
- Navs no tiene historial, pero `router.Route` (`aval`) observa cada
  navegación con la URL final real: el back stack vive en el shell.
- Las rutas no usan caché: navegar a la URL vigente reconstruye la
  página (así se refresca el idioma sin reiniciar a `/`).
- Navs.Avalonia ya anima el cambio de página (CrossFade 150ms +
  PageSlide 300ms).

## Decisiones

- **Gate libre**: sin token arranca en Atletas; conectar proveedor vive
  en Ajustes.
- **Tema**: sigue al SO; override Sistema/Claro/Oscuro en Ajustes,
  persistido en settings.json.
- **Pane lateral fijo**: SplitView Inline ~220px (Atletas, Ajustes);
  colapsa a riel en ventanas angostas.
- **Comandos en CommandBar** al tope de cada página.
- **Acento de marca** `#2D7DD2` vía `ColorPaletteResources`.
- **Back del shell**: historial propio, botón Atrás + Alt+Left.

## Fases

### F0 — Este documento.

### F1 — Shell Fluent + historial

- `Navigation.fs` (+fsi): `History` puro (push/pop/canBack, dedup de
  consecutivas, tope 100) + backstack observable (`CanBack`) y
  sincronización con `router.Route`.
- `Toasts.fs` (+fsi): `init(window)` instala `WindowNotificationManager`;
  `show(title, body, isError)`.
- `Env.fs`: `SetLanguage` emite por `IObservable<Language>` nuevo; el
  shell refresca pane y título; las páginas se refrescan navegando a la
  URL vigente.
- `Program.fs`: shell con `SplitView`, pane (Atletas, Ajustes), fila
  superior con Atrás, acento de marca, instalación de toasts.
- Fuera: `Components/AppBar.fs/.fsi`. Strings: `nav.settings`,
  `common.back`.

### F2 — FluentPage + rampa + pilotos

- `Fields.fs`: `[<Struct>]` `PageChrome` (Title/Commands/Body/Measure) →
  `fluentPage`: margen 24, spacing 12, título 28 semibold, `MaxWidth`
  760, CommandBar al tope. Rampa: pageTitle 28, sectionTitle 20, body
  14, caption 12. `primaryButton` → clase `accent`; `hyperlinkButton`
  nuevo. `Errors.friendly(culture, exn)` mapea a resx; detalle a Serilog.
- Migran Login (medida 560, "continuar sin conectar"), AthleteList
  (comando Nuevo atleta, empty state), NewAthleteForm (Guardar accent,
  Cancelar hyperlink, fallo → toast + datos intactos).

### F3 — Wizard, Review, PlanReview

- `Dialogs.fs` (+fsi): overlay modal Fluent, `confirmAsync` con
  Escape=rechazar, alojado en layer del shell.
- Wizard: comandos por paso en CommandBar; Cancelar activo durante la
  generación (CTS existente); Failed con "Reintentar" (re-envía el último
  brief) y "Salir sin guardar"; Descartar pide confirmación.
- PlanReview: semanas → `TabControl` restilizado.
- Review: Guardar/Exportar al CommandBar; fallo de export → toast; Atrás
  del shell.

### F4 — Ajustes + tema + arranque libre

- `Views/Settings.fs` (+fsi), ruta `/settings`: idioma, tema
  (Sistema/Claro/Oscuro), proveedor (estado, conectar, aviso inseguro).
- Persistencia de tema e idioma en settings.json.
- `Routes.build` arranca siempre en `/`.

### F5 — Pasada final

- Checklist `ui-design-rules` por pantalla: targets ≥36px, jerarquía,
  recoverability, feedback, labels=acción.
- `dotnet build Briple.Trainer.slnx` cero errores y cero warnings,
  `dotnet fantomas .`, corrida manual con screenshots.
- Commits por fase cuando se pidan; nunca push sin permiso.

## Fuera de alcance

Core, esquema SQLite, semántica de Stores, versiones de Navs/MVUish.
`History` queda puro y testeable, sin proyecto de tests nuevo este ciclo.
Hub/Panorama/tiling no aplican a Fluent de escritorio.
