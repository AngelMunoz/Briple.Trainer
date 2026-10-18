module Briple.Trainer.Shell

// The shell: the Fluent split pane, the pane rows, the router outlet,
// the back control, and the window lifetime. Program builds one window
// from the production environment.

open Avalonia.Controls
open Briple.Trainer.Env

/// Builds the shell window over the production environment.
val create: env: AppEnv -> Window
