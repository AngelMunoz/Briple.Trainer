module Briple.Trainer.Views.Settings

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
  Lead: (unit -> Control) voption
  Language: unit -> Language
  Theme: unit -> ThemeChoice
  AgentAvailable: unit -> bool
  OnLanguage: Language -> unit
  OnTheme: ThemeChoice -> unit
  OpenLogin: unit -> unit
}

/// A closed dropdown over resource-key labels; the selection maps by
/// index, so changing the language never rewrites the option values.
let picker
  (options: ('T * string) list, current: 'T, pick: 'T -> unit)
  : Control =
  let combo = ComboBox(MinWidth = 140.0)

  for _, key in options do
    combo.Items.Add(ComboBoxItem(Content = Localization.textBlock key))
    |> ignore

  combo.SelectedIndex <-
    options |> List.findIndex(fun (value, _) -> value = current)

  combo.SelectionChanged.Add(fun _ ->
    let index = combo.SelectedIndex

    if index >= 0 && index < List.length options then
      pick(options[index] |> fst))

  combo

/// The page: static text comes from resources; the dropdown labels
/// follow the dictionary, no rebuild.
let view(props: SettingsPageProps) : Control =
  let languageRow =
    picker(
      [ Es, Keys.settingsLangEs; En, Keys.settingsLangEn ],
      props.Language(),
      props.OnLanguage
    )

  let themeRow =
    picker(
      [
        System, Keys.settingsThemeSystem
        Light, Keys.settingsThemeLight
        Dark, Keys.settingsThemeDark
      ],
      props.Theme(),
      props.OnTheme
    )

  let providerBody: Control =
    if props.AgentAvailable() then
      labelKey Keys.settingsConnected
    else
      iconPrimaryButtonKey(Keys.settingsConnect, Icons.key, props.OpenLogin)

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.navSettings
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          cardRowKey(Keys.settingsLanguage, languageRow),
          cardRowKey(Keys.settingsTheme, themeRow),
          cardRowKey(Keys.settingsProvider, providerBody)
        )
  }

type SettingsPage(props: SettingsPageProps) as this =
  inherit UserControl()

  do this.Content <- view props
