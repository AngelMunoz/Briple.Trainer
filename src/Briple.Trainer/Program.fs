module Briple.Trainer.Program

// The boot: the App type (SimpleTheme base), the shell window with the
// app bar at the bottom and the routes filling the rest, over the env
// built on the default database.

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Themes.Simple
open NXUI.Extensions

open Navs

open Briple.Trainer.Components.AppBar
open Briple.Trainer.Components.Fields
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme
open Briple.Trainer.Views

/// The application root: SimpleTheme is the base, the Metro language
/// lives in Theme and in the control factories.
type App() =
  inherit Application()

  override this.Initialize() = this.Styles.Add(SimpleTheme())

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

let inline start(argv, [<InlineIfLambda>] buildRoot: unit -> Window) =
  AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    .StartWithClassicDesktopLifetime(buildRoot, argv)

[<EntryPoint>]
let main argv =

  match Env.create(defaultDbPath()) with
  | Error e ->
    eprintfn $"boot failed: %O{e}"
    start(argv, fun () -> errorWindow e.Message)
  | Ok env -> start(argv, fun () -> buildShell env)
