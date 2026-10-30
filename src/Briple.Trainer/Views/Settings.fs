module Briple.Trainer.Views.Settings

open System

open Avalonia.Controls
open NXUI.Extensions

open FSharp.Control.Reactive
open MVUish.Avalonia

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Env
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type SettingsEnv = {
  Language: Language
  Theme: ThemeChoice
  AgentAvailable: bool
}

[<Struct>]
type SettingsPageProps = {
  Lead: (unit -> Control) voption
  OnLanguage: Language -> unit
  OnTheme: ThemeChoice -> unit
  OpenLogin: unit -> unit
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

type Model = {
  Language: Language
  Theme: ThemeChoice
  AgentAvailable: bool
}

type Msg =
  | LanguageSelected of Language
  | ThemeSelected of ThemeChoice
  | ConnectRequested

let init(env: SettingsEnv) =
  let model = {
    Language = env.Language
    Theme = env.Theme
    AgentAvailable = env.AgentAvailable
  }

  model, Cmd.none

let update (props: SettingsPageProps) (msg: Msg) (model: Model) =
  match msg with
  | LanguageSelected language ->
    { model with Language = language },
    Cmd.ofAction(fun () -> props.OnLanguage language)
  | ThemeSelected theme ->
    { model with Theme = theme }, Cmd.ofAction(fun () -> props.OnTheme theme)
  | ConnectRequested -> model, Cmd.ofAction(fun () -> props.OpenLogin())

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
let view
  (env: SettingsEnv)
  (props: SettingsPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let languageRow =
    picker(
      [ Es, Keys.settingsLangEs; En, Keys.settingsLangEn ],
      env.Language,
      LanguageSelected >> dispatch
    )

  let themeRow =
    picker(
      [
        System, Keys.settingsThemeSystem
        Light, Keys.settingsThemeLight
        Dark, Keys.settingsThemeDark
      ],
      env.Theme,
      ThemeSelected >> dispatch
    )

  let available = model |> Observable.map _.AgentAvailable

  let connected = labelKey(Keys.settingsConnected).IsVisible(available)

  let connect =
    iconPrimaryButtonKey(
      Keys.settingsConnect,
      Icons.key,
      (fun () -> dispatch ConnectRequested)
    )
      .IsVisible(available |> Observable.map not)

  let providerBody = StackPanel().Spacing(8.0).Children(connected, connect)

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

type SettingsPage(env: SettingsEnv, props: SettingsPageProps) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram
        (fun (_: SettingsPageProps) -> init env)
        (update props)
        (view env props)

    let root, lifetime = Program.runWithLifecycle props program

    this.Content <- root
    props.Lifetime.AddDisposable lifetime
