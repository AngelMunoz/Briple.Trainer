module Briple.Trainer.Dialogs

// Fluent confirmation dialogs: one overlay mounted by the shell, one
// question at a time, Escape answers no.

open Avalonia.Controls

/// What a confirmation asks: title, body, the two answers.
[<Struct>]
type ConfirmRequest = {
  Title: string
  Body: string
  Accept: string
  Cancel: string
}

/// Asks the user: true accepts, false cancels — or Escape.
val inline confirmAsync: request: ConfirmRequest -> Async<bool>

/// The overlay layer the shell mounts last; it shows only while a
/// request is pending.
val host: Control
