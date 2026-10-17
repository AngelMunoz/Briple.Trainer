module Briple.Trainer.Views.Settings

// The settings page: three live preferences. The page is a stateless
// view over the current values, rebuilt when the culture changes, so a
// language switch shows the new labels and the new selection.

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Env
open Briple.Trainer.Strings

/// What the page needs, as one value. The values are getters so a
/// culture rebuild reads the live preference, not a snapshot.
[<Struct>]
type SettingsPageProps = {
  Culture: PageCulture
  /// The shell's back control, placed in the page header.
  Lead: Control voption
  Language: unit -> Language
  Theme: unit -> ThemeChoice
  AgentAvailable: unit -> bool
  OnLanguage: Language -> unit
  OnTheme: ThemeChoice -> unit
  OpenLogin: unit -> unit
}

/// The settings page.
type SettingsPage =
  inherit UserControl

  new: props: SettingsPageProps -> SettingsPage
