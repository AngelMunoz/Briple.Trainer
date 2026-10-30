module Briple.Trainer.Views.Settings

// The settings page: three live preferences. The page is a stateless
// view over the current values, rebuilt when the culture changes, so a
// language switch shows the new labels and the new selection.

open Avalonia.Controls

open Briple.Trainer.Env
open Briple.Trainer.Strings

/// The services and the starting values the settings page needs.
[<Struct>]
type SettingsEnv = {
  Language: Language
  Theme: ThemeChoice
  AgentAvailable: bool
}

/// The page data.
[<Struct>]
type SettingsPageProps = {
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  OnLanguage: Language -> unit
  OnTheme: ThemeChoice -> unit
  OpenLogin: unit -> unit
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

/// The settings page.
type SettingsPage =
  inherit UserControl

  new: env: SettingsEnv * props: SettingsPageProps -> SettingsPage
