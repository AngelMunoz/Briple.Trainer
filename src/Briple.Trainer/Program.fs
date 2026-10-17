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
    .Content(page(message, TextBlock().Text("").FontFamily(font)))
    .FontFamily(font)

/// One pane entry: the label follows the language, the glyph marks the
/// section, the target is a URL.
[<Struct>]
type NavItem = {
  Label: string
  Target: string
  Icon: StreamGeometry
}

let buildShell(env: AppEnv) =
  let router = Routes.build env

  let history = BackStack()

  // the current URL as one shared stream: history, pane highlights and
  // the language refresh all read it
  let currentUrl = new BehaviorSubject<string>("/")

  router.Route
  |> AVal.toObservable
  |> Observable.choose(fun route ->
    route |> ValueOption.toOption |> Option.map _.path)
  |> Observable.subscribe currentUrl.OnNext
  |> ignore

  currentUrl |> Observable.subscribe history.Sync |> ignore

  // the pane state as one stream: the rows hide their labels, the pane
  // title hides itself and the SplitView follows, all from one source
  let paneOpen = new BehaviorSubject<bool>(true)

  // the pane rows: a button per section, the active one follows the URL
  let paneList =
    let items =
      env.LanguageChanged
      |> Observable.map(fun language ->
        let culture = Language.toCulture language

        [
          {
            Label = get(culture, Keys.navAthletes)
            Target = "/"
            Icon = Icons.accountGroup
          }
          {
            Label = get(culture, Keys.navSettings)
            Target = "/settings"
            Icon = Icons.settings
          }
        ])

    // the glyph rides a 48px rail that matches the compact pane: open,
    // the label shows beside it; collapsed, the glyph centers on the rail
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
                .Text(item.Label)
                .FontFamily(font)
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
        .OnClickHandler(fun _ _ -> Routes.go(router, item.Target))
      :> Control

    let template = FuncDataTemplate<NavItem>((fun item _ -> navRow item), false)

    let list = ItemsControl().ItemTemplate(template)

    list.BindItemsSource(items, id) |> ignore

    list

  let goBack() =
    history.Back() |> Option.iter(fun url -> Routes.go(router, url))

  let backButton =
    let button = Button()

    button.Bind(
      Button.ContentProperty :> AvaloniaProperty,
      env.LanguageChanged
      |> Observable.map(fun language ->
        box(
          glyphLabel(
            Icons.arrowLeft,
            get(Language.toCulture language, Keys.commonBack)
          )
        ))
    )
    |> ignore

    button.IsEnabled(history.CanBack).OnClickHandler(fun _ _ -> goBack())

  let window =
    Window(
      Title = Strings.get(env.Culture, Keys.appTitle),
      Width = 1100.0,
      Height = 800.0
    )

  env.SetHost window
  Toasts.init(window)

  Env.applyTheme((env :> IProvideTheme).Theme)

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

  currentUrl
  |> Observable.subscribe(fun url -> window.Title <- titleOf url)
  |> ignore

  // a language switch refreshes the chrome and re-navigates the current
  // URL; the pages stay cached (the culture snapshot inside them is the
  // known gap the reactive-culture plan addresses)
  env.LanguageChanged
  |> Observable.skip 1
  |> Observable.subscribe(fun _ -> Routes.go(router, currentUrl.Value))
  |> ignore

  window.KeyBindings.Add(
    KeyBinding(
      Gesture = KeyGesture.Parse("Alt+Left"),
      Command = ActionCommand goBack
    )
  )

  let pane =
    StackPanel()
      .Children(
        // the pane toggle: the only way in and out of the compact rail
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
          .OnClickHandler(fun _ _ -> paneOpen.OnNext(not paneOpen.Value)),
        TextBlock()
          .Text(
            env.LanguageChanged
            |> Observable.map(fun language ->
              get(Language.toCulture language, Keys.appTitle))
          )
          .FontSize(18.0)
          .FontWeight(FontWeight.SemiBold)
          .Margin(16.0, 8.0, 16.0, 12.0)
          .IsVisible(paneOpen),
        paneList
      )

  let chrome =
    DockPanel()
      .LastChildFill(false)
      .Margin(12.0, 8.0)
      .Children(backButton.DockLeft())

  // the router outlet swaps cached views with no page transition: the
  // library default crossfade/slide keeps two presenters alive, and
  // re-parenting a cached singleton through that window is what killed
  // the renderer ("Visual already has a parent"). An empty composite
  // completes instantly; null here falls back to the library default.
  let outlet = RouterOutlet()
  outlet.Router <- router

  outlet.PageTransition <-
    CompositePageTransition(PageTransitions = ResizeArray [])

  let content =
    DockPanel().LastChildFill(true).Children(chrome.DockTop(), outlet)

  let split =
    SplitView()
      .DisplayMode(SplitViewDisplayMode.CompactInline)
      .CompactPaneLength(48.0)
      .OpenPaneLength(220.0)
      .IsPaneOpen(true)
      .Pane(pane)
      .Content(content)

  // the pane stream drives the SplitView: the hamburger, the rows and
  // the open state read one source
  paneOpen
  |> Observable.subscribe(fun isOpen -> split.IsPaneOpen <- isOpen)
  |> ignore

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
