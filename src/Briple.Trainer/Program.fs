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

/// One pane entry: the label follows the language, the target is a URL.
[<Struct>]
type NavItem = { Label: string; Target: string }

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
          }
          {
            Label = get(culture, Keys.navSettings)
            Target = "/settings"
          }
        ])

    let navRow(item: NavItem) : Control =
      let active =
        currentUrl
        |> Observable.map(fun url ->
          History.sectionOf url = ValueSome item.Target)

      Button()
        .Content(item.Label)
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
        box(get(Language.toCulture language, Keys.commonBack)))
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

  // a language switch refreshes the chrome and re-navigates the current
  // URL: the routes carry no cache, so every page rebuilds under the new
  // culture
  env.LanguageChanged
  |> Observable.skip 1
  |> Observable.subscribe(fun _ ->
    window.Title <- get(env.Culture, Keys.appTitle)

    Routes.go(router, currentUrl.Value))
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
        TextBlock()
          .Text(
            env.LanguageChanged
            |> Observable.map(fun language ->
              get(Language.toCulture language, Keys.appTitle))
          )
          .FontSize(18.0)
          .FontWeight(FontWeight.SemiBold)
          .Margin(16.0, 20.0, 16.0, 12.0),
        paneList
      )

  let chrome =
    DockPanel()
      .LastChildFill(false)
      .Margin(12.0, 8.0)
      .Children(backButton.DockLeft())

  // the router outlet renders the current route with the library's
  // default page transition
  let outlet = RouterOutlet()
  outlet.Router <- router

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

  // the dialog overlay rides on top of everything
  window.Content(Grid().Children(split, Dialogs.host)) |> ignore

  // narrow windows collapse the pane to the compact rail
  window.SizeChanged.Add(fun size ->
    split.IsPaneOpen <- size.NewSize.Width >= 700.0)

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
