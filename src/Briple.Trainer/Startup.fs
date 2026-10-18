module Briple.Trainer.Startup

open System
open System.IO

open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Themes.Fluent
open NXUI.Extensions

open Serilog

open Briple.Trainer.Components.Fields
open Briple.Trainer.Localization
open Briple.Trainer.Strings
open Briple.Trainer.Theme

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

let start(argv, buildRoot: unit -> Window) : int =
  AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    .StartWithClassicDesktopLifetime(buildRoot, argv)
