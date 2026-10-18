module Briple.Trainer.Startup

// The application root and the desktop lifetime. Program calls these
// once: logging, the boot window, and the Avalonia start.

open Avalonia.Controls

/// The application root: FluentTheme with the app's accent on top.
type App =
  inherit Avalonia.Application
  new: unit -> App

/// Configures Serilog: console plus one rolling file a day under the
/// user's local app data.
val configureLogging: unit -> unit

/// The window shown when boot fails before the shell exists.
val errorWindow: message: string -> Window

/// Starts the Avalonia desktop lifetime with the root window factory.
val start: argv: string[] * buildRoot: (unit -> Window) -> int
