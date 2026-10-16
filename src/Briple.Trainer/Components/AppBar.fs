module Briple.Trainer.Components.AppBar

open System.Globalization

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open Briple.Trainer.Components.Fields
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type AppBarEvent =
  | HomeRequested
  | ToggleLanguage of Language

type AppBar
  (culture: CultureInfo, [<InlineIfLambda>] onEvent: AppBarEvent -> unit) as this
  =
  inherit UserControl()

  do
    let t key = get(culture, key)

    let currentLanguage =
      match culture.TwoLetterISOLanguageName with
      | "en" -> En
      | _ -> Es

    let homeButton =
      let flat = button(t Keys.navAthletes, (fun () -> onEvent HomeRequested))

      flat.Foreground(accent).Dock(Dock.Left)

    let languageButton =
      let flat =
        button(
          (t Keys.navLanguage),
          (fun () -> onEvent(ToggleLanguage(Language.toggle currentLanguage)))
        )

      flat.Foreground(Brushes.White).Dock(Dock.Right)

    this.Content <-
      DockPanel()
        .LastChildFill(false)
        .Background(text)
        .Children(homeButton, languageButton)
