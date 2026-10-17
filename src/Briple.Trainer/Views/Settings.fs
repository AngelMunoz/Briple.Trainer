module Briple.Trainer.Views.Settings

open System.Globalization

open Avalonia.Controls
open NXUI.Extensions

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Env
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type SettingsPageProps = {
  Culture: PageCulture
  Lead: Control voption
  Language: unit -> Language
  Theme: unit -> ThemeChoice
  AgentAvailable: unit -> bool
  OnLanguage: Language -> unit
  OnTheme: ThemeChoice -> unit
  OpenLogin: unit -> unit
}

/// A closed dropdown over localized labels; the pick maps the label
/// back to the value it was built from.
let picker
  (options: string list, current: string, pick: string -> unit)
  : Control =
  let combo = ComboBox(MinWidth = 140.0)

  options |> List.iter(combo.Items.Add >> ignore)

  combo.SelectedItem <- current

  combo.SelectionChanged.Add(fun _ ->
    match combo.SelectedItem with
    | :? string as picked -> pick picked
    | _ -> ())

  combo

/// The page at one culture.
let view (props: SettingsPageProps) (culture: CultureInfo) : Control =
  let t key = get(culture, key)

  let languageRow =
    let esName = t Keys.settingsLangEs
    let enName = t Keys.settingsLangEn

    let current =
      match props.Language() with
      | Es -> esName
      | En -> enName

    picker(
      [ esName; enName ],
      current,
      fun picked -> props.OnLanguage(if picked = enName then En else Es)
    )

  let themeRow =
    let system = t Keys.settingsThemeSystem
    let light = t Keys.settingsThemeLight
    let dark = t Keys.settingsThemeDark

    let current =
      match props.Theme() with
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

    picker([ system; light; dark ], current, pick)

  let providerBody: Control =
    if props.AgentAvailable() then
      label(t Keys.settingsConnected)
    else
      iconPrimaryButton(t Keys.settingsConnect, Icons.key, props.OpenLogin)

  fluentPage {
    Lead = props.Lead
    Title = t Keys.navSettings
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          cardRow(t Keys.settingsLanguage, languageRow),
          cardRow(t Keys.settingsTheme, themeRow),
          cardRow(t Keys.settingsProvider, providerBody)
        )
    Measure = 560.0
  }

type SettingsPage(props: SettingsPageProps) as this =
  inherit UserControl()

  do
    this.Content <-
      Page.cultureView(props.Culture, (fun culture -> view props culture))
