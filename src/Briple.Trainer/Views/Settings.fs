module Briple.Trainer.Views.Settings

open System.Globalization

open Avalonia.Controls
open NXUI.Extensions

open Briple.Trainer.Components.Fields
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

type SettingsPage(props: SettingsPageProps) as this =
  inherit UserControl()

  let culture = props.Culture
  let t key = get(culture, key)

  let languageRow =
    let esName = t Keys.settingsLangEs
    let enName = t Keys.settingsLangEn

    let current =
      match props.Language with
      | Es -> esName
      | En -> enName

    radioRow(
      [ esName; enName ],
      "settings-language",
      current,
      fun picked -> props.OnLanguage(if picked = enName then En else Es)
    )

  let themeRow =
    let system = t Keys.settingsThemeSystem
    let light = t Keys.settingsThemeLight
    let dark = t Keys.settingsThemeDark

    let current =
      match props.Theme with
      | System -> system
      | Light -> light
      | Dark -> dark

    let pick(picked: string) =
      props.OnTheme(
        match picked with
        | p when p = light -> Light
        | p when p = dark -> Dark
        | _ -> System
      )

    radioRow([ system; light; dark ], "settings-theme", current, pick)

  let providerBody: Control =
    if props.AgentAvailable then
      label(t Keys.settingsConnected)
    else
      StackPanel()
        .Spacing(8.0)
        .Children(
          label(t Keys.settingsMissingToken),
          primaryButton(t Keys.settingsConnect, props.OpenLogin)
        )

  do

    this.Content <-
      fluentPage {
        Title = t Keys.navSettings
        Commands = []
        Body =
          StackPanel()
            .Spacing(4.0)
            .Children(
              field(t Keys.settingsLanguage, (fun () -> languageRow)),
              field(t Keys.settingsTheme, (fun () -> themeRow)),
              field(t Keys.settingsProvider, (fun () -> providerBody))
            )
        Measure = 560.0
      }
