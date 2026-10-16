module Briple.Trainer.Toasts

// Fluent toasts over WindowNotificationManager: `init` installs the host
// on the shell window once; `show` is fire-and-forget feedback.

open Avalonia.Controls

/// Installs the toast host on the shell window.
val init: host: Window -> unit

/// Shows one toast; before `init` this is a no-op.
val show: title: string * body: string * isError: bool -> unit
