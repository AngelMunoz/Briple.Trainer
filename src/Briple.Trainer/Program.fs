module Briple.Trainer.Program

// The boot: Serilog first (console + daily file under the user's local
// app data), the App type (FluentTheme as shipped plus the brand accent),
// and the shell — a Fluent split pane with the shell-owned back stack —
// over the env built on the default database. The logger flushes on the
// way out.

open System
open System.IO
open System.Reactive.Subjects

open Avalonia
open Avalonia.Animation
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Themes.Fluent
open NXUI.Extensions

open MVUish.Avalonia.Collections
open FSharp.Control.Reactive
open IcedTasks
open Navs
open Navs.Avalonia
open Serilog

open Briple.Trainer.Components.Fields
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme
open Briple.Trainer.Views

/// The application root: FluentTheme with the app's accent on top.
type App() =
  inherit Application()

  override this.Initialize() =
    this.Resources.MergedDictionaries.Add(
      ColorPaletteResources(Accent = Color.Parse accentHex)
    )

    this.Styles.Add(FluentTheme())
    this.Styles.Add(appStyles)

    // the boot language before the shell knows the persisted one; the
    // shell installs the user's choice from the environment
    Localization.install(Language.toCulture Es)

/// Console plus one rolling file a day under %LocalAppData%.
let configureLogging() : unit =
  let folder =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "Briple.Trainer",
      "logs"
    )

  Directory.CreateDirectory(folder) |> ignore

  Log.Logger <-
    LoggerConfiguration()
#if DEBUG
      .MinimumLevel
      .Debug()
#else
      .MinimumLevel
      .Information()
#endif
      .WriteTo.Console()
      .WriteTo.File(
        Path.Combine(folder, "briple-.log"),
        rollingInterval = RollingInterval.Day
      )
      .CreateLogger()

let errorWindow(message: string) =
  Window(Title = "Briple Trainer", Width = 640.0, Height = 400.0)
    .Content(page(message, TextBlock().Text("")))
    .FontFamily(font)

/// One pane entry: the label follows the language, the glyph marks the
/// section, the target is a URL.
[<Struct>]
type NavItem = {
  Key: string
  Target: string
  Icon: StreamGeometry
}

let buildShell(env: AppEnv) =
  Localization.install env.Culture

  let languageSubscription =
    env.LanguageChanged
    |> Observable.subscribe(fun language ->
      Localization.install(Language.toCulture language))

  let history = BackStack()

  // the router lands in this cell once the table is built; the boot
  // navigation builds pages before the cell is filled, and nothing
  // clicks before the window exists
  let routerCell = ref ValueNone

  let router() =
    match routerCell.Value with
    | ValueSome router -> router
    | ValueNone -> invalidOp "the router is not built yet"

  let goTo(url: string) = Routes.go(router(), url)

  let goBack() = history.Back() |> Option.iter goTo

  // the shell owns history and draws Back: a 40x40 icon-only flat
  // button in the page header. Every page gets its own instance; all
  // of them read the same history.
  let lead() : Control =
    let button =
      Button()
        .Content(PathIcon(Data = Icons.arrowLeft).Width(16.0).Height(16.0))
        .IsEnabled(history.CanBack)
        .OnClickHandler(fun _ _ -> goBack())
        .Classes("chrome", "back")

    // the Fluent tooltip content is a control; its text follows the
    // language through the normal binding
    let tip = TextBlock().Text(Localization.text Keys.commonBack)

    ToolTip.SetTip(button, tip)
    button

  let shell = Routes.build(env, lead)
  routerCell.Value <- ValueSome shell.Router

  // the current URL as one shared stream: history, pane highlights and
  // the window title read it. One adapter subscribes the router.
  let currentUrl = new BehaviorSubject<string>("/")

  let routeSubscription =
    shell.Router.Route
    |> AVal.toObservable
    |> Observable.choose(fun route ->
      route |> ValueOption.toOption |> Option.map _.path)
    |> Observable.subscribe(fun url ->
      currentUrl.OnNext url
      history.Sync url)

  // the pane state as one stream: the rows hide their labels, the pane
  // title hides itself and the SplitView follows, all from one source
  let paneOpen = new BehaviorSubject<bool>(true)

  // the pane rows: the glyph rides a 48px rail that matches the compact
  // pane: open, the label shows beside it; collapsed, the glyph centers
  // on the rail
  let navRow(item: NavItem) : Control =
    let active =
      currentUrl
      |> Observable.map(fun url ->
        History.sectionOf url = ValueSome item.Target)

    Button()
      .Content(
        DockPanel()
          .Children(
            // the selection bar: the Fluent accent strip on the rail
            Border().Width(3.0).Background(accent).IsVisible(active),
            Border()
              .Width(48.0)
              .Child(
                PathIcon(Data = item.Icon)
                  .HorizontalAlignment(HorizontalAlignment.Center)
              ),
            TextBlock()
              .Text(Localization.text item.Key)
              .VerticalAlignment(VerticalAlignment.Center)
              .IsVisible(paneOpen)
          )
      )
      .HorizontalContentAlignment(HorizontalAlignment.Left)
      .Foreground(
        active |> Observable.map(fun on -> if on then accent else text)
      )
      .FontWeight(
        active
        |> Observable.map(fun on ->
          if on then FontWeight.SemiBold else FontWeight.Normal)
      )
      .OnClickHandler(fun _ _ -> goTo item.Target)
      .Classes("nav-item")

  // the pane's main group: the label follows the resource dictionary,
  // so the item itself never changes on a language switch
  let paneList =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<NavItem>((fun item _ -> navRow item), false)
      )
      .ItemsSource(
        [
          {
            Key = Keys.navAthletes
            Target = "/"
            Icon = Icons.accountGroup
          }
        ]
      )

  // settings rides the pane footer; its label is a resource too
  let settingsHost =
    navRow {
      Key = Keys.navSettings
      Target = "/settings"
      Icon = Icons.settings
    }

  let window =
    Window(
      Title = Strings.get(env.Culture, Keys.appTitle),
      Width = 1100.0,
      Height = 800.0
    )
      .FontFamily(font)

  // the router and the resource installer are the two shell-lifetime
  // subscriptions; the window owns them and releases them on close
  window.Closed.Add(fun _ ->
    routeSubscription.Dispose()
    languageSubscription.Dispose())

  env.SetHost window
  Toasts.init(window)

  Env.applyTheme((env :> IProvideTheme).Theme)

  // the tokens follow the live variant: light/dark retints every
  // consumer without rebinding
  match Application.Current with
  | null -> ()
  | app ->
    Theme.applyVariant(app.ActualThemeVariant)

    app.ActualThemeVariantChanged.Add(fun _ ->
      Theme.applyVariant(app.ActualThemeVariant))

  // the Fluent window: the content reaches behind the system caption and
  // the surface is Mica where the platform offers it, falling back to
  // acrylic blur and plain transparency. The root margin keeps the pane
  // below the caption.
  window.ExtendClientAreaToDecorationsHint <- true

  window.ExtendClientAreaTitleBarHeightHint <- 32.0

  window.TransparencyLevelHint <-
    ResizeArray [
      WindowTransparencyLevel.Mica
      WindowTransparencyLevel.AcrylicBlur
      WindowTransparencyLevel.Transparent
    ]

  window.Background <- Brushes.Transparent

  // the window title names the section, under the current language
  let titleOf(url: string) =
    let app = get(env.Culture, Keys.appTitle)

    match History.sectionOf url with
    | ValueSome "/" -> $"{app} — {get(env.Culture, Keys.navAthletes)}"
    | ValueSome "/settings" -> $"{app} — {get(env.Culture, Keys.navSettings)}"
    | _ -> app

  let window =
    window.Title(
      Observable.combineLatest currentUrl env.LanguageChanged
      |> Observable.map(fun (url, _) -> titleOf url)
    )

  window.KeyBindings.Add(
    KeyBinding(
      Gesture = KeyGesture.Parse("Alt+Left"),
      Command = ActionCommand goBack
    )
  )

  // the OS close honors the same phases as the route guards: cancel the
  // close, ask, close again when the answer allows it
  let mutable closeConfirmed = false

  window.Closing.Add(fun args ->
    if not closeConfirmed then
      args.Cancel <- true

      asyncEx {
        let! allowed = shell.AskLeave currentUrl.Value

        if allowed then
          closeConfirmed <- true
          window.Close()
      }
      |> Async.StartImmediate)

  let pane =
    // the pane toggle: the only way in and out of the compact rail
    let toggle =
      Button()
        .Content(
          Border()
            .Width(48.0)
            .Child(
              PathIcon(Data = Icons.menu)
                .HorizontalAlignment(HorizontalAlignment.Center)
            )
        )
        .HorizontalContentAlignment(HorizontalAlignment.Left)
        .OnClickHandler(fun _ _ -> paneOpen.OnNext(not paneOpen.Value))
        .Classes("chrome")

    DockPanel()
      .Children(
        settingsHost.DockBottom(),
        StackPanel()
          .Children(
            toggle,
            TextBlock()
              .Text(Localization.text Keys.appTitle)
              .Classes("pane-title")
              .Margin(16.0, 8.0, 16.0, 12.0)
              .IsVisible(paneOpen),
            paneList
          )
      )

  // the router outlet swaps cached views with no page transition: the
  // library default crossfade/slide keeps two presenters alive, and
  // re-parenting a cached singleton through that window is what killed
  // the renderer ("Visual already has a parent"). An empty composite
  // completes instantly; null here falls back to the library default.
  let outlet = RouterOutlet()
  outlet.Router <- shell.Router

  outlet.PageTransition <-
    CompositePageTransition(PageTransitions = ResizeArray [])

  let split =
    SplitView()
      .DisplayMode(SplitViewDisplayMode.CompactInline)
      .CompactPaneLength(48.0)
      .OpenPaneLength(220.0)
      .IsPaneOpen(paneOpen)
      .Pane(pane)
      .Content(outlet)

  // the dialog overlay rides on top of everything; the top margin keeps
  // the content below the extended caption
  window.Content(
    Grid().Margin(0.0, 32.0, 0.0, 0.0).Children(split, Dialogs.host)
  )
  |> ignore

  // narrow windows snap the pane to the rail and wide ones open it, but
  // only when the window crosses the line: a manual toggle survives
  let mutable wideWindow = true

  window.SizeChanged.Add(fun size ->
    let wide = size.NewSize.Width >= 700.0

    if wide <> wideWindow then
      wideWindow <- wide
      paneOpen.OnNext wide)

  window

let start(argv, buildRoot: unit -> Window) : int =
  AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    .StartWithClassicDesktopLifetime(buildRoot, argv)

[<EntryPoint>]
let main argv =
  configureLogging()

  try
    match Env.create(defaultDbPath()) with
    | Error e ->
      Log.Fatal(e, "boot failed")
      start(argv, fun () -> errorWindow e.Message)
    | Ok env -> start(argv, fun () -> buildShell env)
  finally
    Log.CloseAndFlush()
