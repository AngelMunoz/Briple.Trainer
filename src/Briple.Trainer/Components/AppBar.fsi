module Briple.Trainer.Components.AppBar

// The bottom app bar: home and language toggle. The bar owns no state;
// events go out and the shell handles them.

open System.Globalization

open Briple.Trainer.Strings

/// The events the bar raises.
[<Struct>]
type AppBarEvent =
  | HomeRequested
  | ToggleLanguage of Language

/// The bar control.
type AppBar =
  inherit Avalonia.Controls.UserControl

  new:
    culture: CultureInfo * [<InlineIfLambda>] onEvent: (AppBarEvent -> unit) ->
      AppBar
