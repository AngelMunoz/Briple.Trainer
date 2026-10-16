module Briple.Trainer.Views.Settings

// The settings page: language, theme and the provider connection. The
// language switch re-renders through the shell's language subscription;
// the theme applies in place.

open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Env
open Briple.Trainer.Strings

/// What the page needs, as one value.
[<Struct>]
type SettingsPageProps = {
  Culture: CultureInfo
  Language: Language
  Theme: ThemeChoice
  AgentAvailable: bool
  OnLanguage: Language -> unit
  OnTheme: ThemeChoice -> unit
  OpenLogin: unit -> unit
}

/// The settings page.
type SettingsPage =
  inherit UserControl

  new: props: SettingsPageProps -> SettingsPage
