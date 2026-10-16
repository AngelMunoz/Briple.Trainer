module Briple.Trainer.Program

// The boot: Serilog first (console + daily file under the user's local
// app data), the App type (FluentTheme base), the shell window with the
// app bar at the bottom and the routes filling the rest, over the env
// built on the default database. The logger flushes on the way out.

open System
open System.IO

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Themes.Fluent
open NXUI.Extensions

open Navs
open Serilog

open Briple.Trainer.Components.AppBar
open Briple.Trainer.Components.Fields
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme
open Briple.Trainer.Views

/// The application root: FluentTheme as shipped, the app's own look
/// lives in Theme and in the control factories.
type App() =
  inherit Application()

  override this.Initialize() = this.Styles.Add(FluentTheme())

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

let buildShell(env: AppEnv) =
  let routes = Routes.build env

  let culture = env.Culture

  let window =
    Window(
      Title = Strings.get(culture, Keys.appTitle),
      Width = 1100.0,
      Height = 800.0
    )
      .FontFamily(font)

  env.SetHost window

  let bar =
    AppBar(
      culture,
      fun event ->
        match event with
        | AppBarEvent.HomeRequested ->
          routes.Router
          |> ValueOption.iter(fun router -> Routes.go(router, "/"))
        | AppBarEvent.ToggleLanguage language ->
          env.SetLanguage language

          routes.Router
          |> ValueOption.iter(fun router -> Routes.go(router, "/"))
    )

  window.Content(
    DockPanel().LastChildFill(true).Children(bar.DockBottom(), routes)
  )
  |> ignore

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
