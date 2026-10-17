module Briple.Trainer.Components.Fields

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open Briple.Trainer.Theme

/// Flow actions of a page footer: secondaries keep to the left,
/// primaries claim the right.
[<Struct>]
type FooterBar = {
  Secondary: Control list
  Primary: Control list
}

/// The empty footer, for pages without flow actions.
let noFooter: FooterBar = { Secondary = []; Primary = [] }

/// The page contract: title, page commands, flow actions and body, read
/// inside a measure so prose and forms keep a readable width.
[<Struct>]
type PageChrome = {
  Title: string
  Commands: Control list
  Footer: FooterBar
  Body: Control
  Measure: float
}

/// The page title level: 28 SemiBold.
let inline pageTitle(text: string) : Control =
  TextBlock()
    .Text(text)
    .FontFamily(font)
    .FontSize(28.0)
    .FontWeight(FontWeight.SemiBold)
    .Margin(0.0, 0.0, 0.0, 8.0)

/// The section header level: 20 SemiBold.
let inline sectionTitle(text: string) : Control =
  TextBlock()
    .Text(text)
    .FontFamily(font)
    .FontSize(20.0)
    .FontWeight(FontWeight.SemiBold)
    .Margin(0.0, 16.0, 0.0, 8.0)

/// A secondary-text caption: 12, muted.
let inline label(text: string) : Control =
  TextBlock().Text(text).FontFamily(font).FontSize(12.0).Foreground(muted)

/// One labeled field: label above, input below.
let inline field
  (labelText: string, [<InlineIfLambda>] input: unit -> Control)
  : Control =
  StackPanel().Spacing(4.0).Margin(0.0, 8.0).Children(label labelText, input())

let inline button
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Button =
  Button()
    .Content(text)
    .FontFamily(font)
    .Padding(16.0, 8.0)
    .OnClickHandler(fun _ _ -> onClick())

/// The primary action button: the accent fill.
let inline primaryButton
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Button =
  let flat = button(text, onClick)

  flat.Classes.Add("accent")

  flat

/// A tertiary text action.
let inline hyperlinkButton
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Control =
  HyperlinkButton()
    .Content(text)
    .FontFamily(font)
    .OnClickHandler(fun _ _ -> onClick())

/// One command for the page's CommandBar.
let inline command
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : CommandBarButton =
  CommandBarButton(Label = text).OnClickHandler(fun _ _ -> onClick())

/// A 20px glyph that takes the text color of its container.
let inline icon(geometry: StreamGeometry) : Control =
  PathIcon(Data = geometry) :> Control

/// A glyph at an explicit size and tone, for standalone composition.
let inline glyph
  (geometry: StreamGeometry, size: float, tone: IBrush)
  : Control =
  PathIcon(Data = geometry, Width = size, Height = size, Foreground = tone)
  :> Control

/// A glyph before its label: the content of an action control.
let inline glyphLabel(geometry: StreamGeometry, text: string) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Children(icon geometry, TextBlock().Text(text).FontFamily(font))

/// A plain button with a glyph before its label.
let inline iconButton
  (
    text: string,
    geometry: StreamGeometry,
    [<InlineIfLambda>] onClick: unit -> unit
  ) : Button =
  button(text, onClick).Content(glyphLabel(geometry, text))

/// The primary action button with a glyph before its label.
let inline iconPrimaryButton
  (
    text: string,
    geometry: StreamGeometry,
    [<InlineIfLambda>] onClick: unit -> unit
  ) : Button =
  let flat = iconButton(text, geometry, onClick)

  flat.Classes.Add("accent")

  flat

/// One CommandBar command with its glyph left of the label.
let inline iconCommand
  (
    text: string,
    geometry: StreamGeometry,
    [<InlineIfLambda>] onClick: unit -> unit
  ) : CommandBarButton =
  CommandBarButton(Label = text, Icon = PathIcon(Data = geometry))
    .OnClickHandler(fun _ _ -> onClick())

let inline chipButton
  (text: string, [<InlineIfLambda>] onClick: unit -> unit, isOn: bool)
  : Button =
  let flat = button(text, onClick)

  if isOn then
    flat.Background(accent).Foreground(Brushes.White)
  else
    flat.Background(chip).Foreground(Brushes.Black)

let inline choiceChips
  (
    options: string list,
    selected: string,
    [<InlineIfLambda>] onPick: string -> unit
  ) : Control =
  WrapPanel().Children [|
    for option in options ->
      chipButton(option, (fun () -> onPick option), (option = selected))
        .Margin(0.0, 0.0, 6.0, 6.0)
      :> Control
  |]

let inline multiChips
  (
    options: string list,
    selected: string list,
    [<InlineIfLambda>] onToggle: string -> unit
  ) : Control =
  WrapPanel().Children [|
    for option in options ->
      chipButton(
        option,
        (fun () -> onToggle option),
        (List.contains option selected)
      )
        .Margin(0.0, 0.0, 6.0, 6.0)
      :> Control
  |]

let inline reactiveChoiceChips
  (
    options: string list,
    selected: IObservable<string>,
    [<InlineIfLambda>] onPick: string -> unit
  ) : Control =
  let rows: Control array = [|
    for option in options ->
      let row = button(option, (fun () -> onPick option))

      row
        .Background(
          selected
          |> Observable.map(fun current ->
            if current = option then accent else chip)
        )
        .Foreground(
          selected
          |> Observable.map(fun current ->
            if current = option then
              Brushes.White :> IBrush
            else
              Brushes.Black :> IBrush)
        )
  |]

  WrapPanel().Children [| for row in rows -> row.Margin(0.0, 0.0, 6.0, 6.0) |]

let inline reactiveMultiChips
  (
    options: string list,
    selected: IObservable<string list>,
    [<InlineIfLambda>] onToggle: string -> unit
  ) : Control =
  let rows: Control array = [|
    for option in options ->
      let row = button(option, (fun () -> onToggle option))

      row
        .Background(
          selected
          |> Observable.map(fun current ->
            if List.contains option current then accent else chip)
        )
        .Foreground(
          selected
          |> Observable.map(fun current ->
            if List.contains option current then
              Brushes.White :> IBrush
            else
              Brushes.Black :> IBrush)
        )
        .Margin(0.0, 0.0, 6.0, 6.0)
  |]

  WrapPanel().Children(rows)

let inline entry
  (
    placeholder: string,
    text: string,
    [<InlineIfLambda>] onChanged: string -> unit
  ) : TextBox =
  TextBox()
    .FontFamily(font)
    .Padding(8.0, 6.0)
    .Text(text)
    .PlaceholderText(placeholder)
    .OnText(fun _ observed ->
      observed
      |> Observable.map(fun value ->
        value |> Option.ofObj |> Option.defaultValue "")
      |> Observable.subscribe onChanged
      |> ignore)

/// A radio button per option in one group; the control owns the visual
/// state, so the active option is always visible.
let inline radioRow
  (
    options: string list,
    group: string,
    initial: string,
    [<InlineIfLambda>] onPick: string -> unit
  ) : Control =
  let row(option: string) : Control =
    RadioButton(Content = option, GroupName = group, FontFamily = font)
      .IsChecked(Nullable((option = initial)))
      .Margin(0.0, 0.0, 12.0, 4.0)
      .OnClickHandler(fun _ _ -> onPick option)

  WrapPanel().Children [| for option in options -> row option |]

/// One checkbox whose checked state follows an observable.
let inline checkRow
  (
    text: string,
    isOn: IObservable<bool>,
    [<InlineIfLambda>] onToggle: unit -> unit
  ) : Control =
  let check = CheckBox(Content = text, FontFamily = font)

  let tap =
    isOn |> Observable.subscribe(fun on -> check.IsChecked <- Nullable on)

  check.DetachedFromVisualTree.Add(fun _ -> tap.Dispose())

  check.OnClickHandler(fun _ _ -> onToggle())

/// The CommandBar of a page: labels ride beside the glyphs (nothing
/// truncates), the commands land as primaries and dynamic overflow takes
/// the rest when the window runs narrow.
let inline commandBar(commands: Control list) : Control =
  let bar =
    CommandBar()
      .IsDynamicOverflowEnabled(true)
      .DefaultLabelPosition(CommandBarDefaultLabelPosition.Right)
      .HorizontalAlignment(HorizontalAlignment.Stretch)

  commands
  |> List.iter(fun command ->
    match command with
    | :? CommandBarButton as button ->
      bar.PrimaryCommands.Add(button :> ICommandBarElement)
    | _ -> ())

  bar

/// The full page: the title and CommandBar pinned along the top edge,
/// the flow actions (when any) pinned to the bottom, and the body
/// scrolling in between, centered inside the measure.
let inline fluentPage(chrome: PageChrome) : Control =
  let header: Control =
    match chrome.Commands with
    | [] ->
      StackPanel()
        .Margin(24.0, 16.0, 24.0, 0.0)
        .Children(pageTitle chrome.Title)
    | commands ->
      StackPanel()
        .Spacing(12.0)
        .Margin(24.0, 16.0, 24.0, 0.0)
        .Children(pageTitle chrome.Title, commandBar commands)

  let body =
    ScrollViewer()
      .Content(
        StackPanel()
          .Margin(24.0)
          .Spacing(12.0)
          .MaxWidth(chrome.Measure)
          .Children(chrome.Body)
      )

  match chrome.Footer.Secondary, chrome.Footer.Primary with
  | [], [] ->
    DockPanel().LastChildFill(true).Children(header.DockTop(), body) :> Control
  | secondary, primary ->
    // flow actions: secondaries keep left, primaries claim the right
    let footer =
      DockPanel()
        .LastChildFill(false)
        .Margin(24.0, 12.0, 24.0, 16.0)
        .Children(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(List.toArray secondary)
            .DockLeft(),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(List.toArray primary)
            .DockRight()
        )

    DockPanel()
      .LastChildFill(true)
      .Children(header.DockTop(), footer.DockBottom(), body)
    :> Control

/// A body-only page: the page contract without commands.
let inline page(titleText: string, content: Control) : Control =
  fluentPage {
    Title = titleText
    Commands = []
    Footer = noFooter
    Body = content
    Measure = 760.0
  }
