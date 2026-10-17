module Briple.Trainer.Components.Fields

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Presenters
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open NXUI.Extensions

open Briple.Trainer.Theme

[<Struct>]
type FooterBar = {
  Secondary: Control list
  Primary: Control list
}

/// The empty footer, for pages without flow actions.
let noFooter: FooterBar = { Secondary = []; Primary = [] }

[<Struct>]
type PageChrome = {
  /// The shell's back control, placed first in the header row.
  Lead: Control voption
  Title: string
  Commands: Control list
  Footer: FooterBar
  Body: Control
  Measure: float
}

/// The page title level: the `title` ramp class.
let inline pageTitle(text: string) : Control =
  TextBlock()
    .Text(text)
    .VerticalAlignment(VerticalAlignment.Center)
    .TextTrimming(TextTrimming.CharacterEllipsis)
    .Classes("title")

/// The section header level: the `subtitle` ramp class.
let inline sectionTitle(text: string) : Control =
  TextBlock().Text(text).Margin(0.0, 16.0, 0.0, 8.0).Classes("subtitle")

/// A secondary-text caption: the `caption` ramp class, muted.
let inline label(text: string) : Control =
  TextBlock().Text(text).Classes("caption", "secondary")

/// One labeled field: label above, input below.
let inline field
  (labelText: string, [<InlineIfLambda>] input: unit -> Control)
  : Control =
  StackPanel().Spacing(4.0).Margin(0.0, 8.0).Children(label labelText, input())

let inline button
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Button =
  Button().Content(text).Padding(16.0, 8.0).OnClickHandler(fun _ _ -> onClick())

/// The primary action button: the accent fill.
let inline primaryButton
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Button =
  button(text, onClick).Classes("accent")

/// A tertiary text action.
let inline hyperlinkButton
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Control =
  HyperlinkButton().Content(text).OnClickHandler(fun _ _ -> onClick())

/// One page command: a plain button for the header row.
let inline command
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Button =
  button(text, onClick)

/// A 20px glyph that takes the text color of its container.
let inline icon(geometry: StreamGeometry) : Control = PathIcon(Data = geometry)

/// A glyph at an explicit size and tone, for standalone composition.
let inline glyph
  (geometry: StreamGeometry, size: float, tone: IBrush)
  : Control =
  PathIcon(Data = geometry, Width = size, Height = size, Foreground = tone)

/// A glyph before its label: the content of an action control.
let inline glyphLabel(geometry: StreamGeometry, text: string) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Children(icon geometry, TextBlock().Text(text))

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
  iconButton(text, geometry, onClick).Classes("accent")

/// One page command with its glyph before the label.
let inline iconCommand
  (
    text: string,
    geometry: StreamGeometry,
    [<InlineIfLambda>] onClick: unit -> unit
  ) : Button =
  iconButton(text, geometry, onClick)

/// One content row: a two-line title and detail with a trailing glyph,
/// in a grid so the glyph stays at the row's end.
let inline contentRow
  (title: string, detail: string, [<InlineIfLambda>] trailing: unit -> Control)
  : Control =
  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("*,Auto")

  grid.Children(
    StackPanel()
      .Spacing(2.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Children(
        TextBlock()
          .Text(title)
          .Classes("body-strong")
          .TextTrimming(TextTrimming.CharacterEllipsis),
        TextBlock()
          .Text(detail)
          .Classes("caption", "secondary")
          .TextTrimming(TextTrimming.CharacterEllipsis)
      )
      .Column(0),
    (trailing())
      .VerticalAlignment(VerticalAlignment.Center)
      .Margin(12.0, 0.0, 0.0, 0.0)
      .Column(1)
  )

/// Makes every item container fill the items control's width, so rows of
/// different lengths share one left edge and one trailing edge.
let inline stretchItems(items: ItemsControl) : ItemsControl =
  let style =
    Style(fun selector -> selector.OfType<ContentPresenter>())
      .Setters(
        [|
          Setter(
            ContentPresenter.HorizontalContentAlignmentProperty,
            HorizontalAlignment.Stretch
          )
        |]
      )

  items.Styles.Add(style)
  items

/// A single-line text input with a placeholder; typing reports the
/// value, the model owns it.
let inline entry
  (
    placeholder: string,
    text: string,
    [<InlineIfLambda>] onChanged: string -> unit
  ) : TextBox =
  TextBox()
    .Padding(8.0, 6.0)
    .Text(text)
    .PlaceholderText(placeholder)
    .OnTextChangedHandler(fun args _ ->
      args.Text |> Option.ofObj |> Option.defaultValue "" |> onChanged)

/// One settings card row: the title left, the control right.
let inline cardRow(title: string, control: Control) : Control =
  Border()
    .Child(
      DockPanel()
        .LastChildFill(false)
        .Children(
          TextBlock()
            .Text(title)
            .Classes("body")
            .VerticalAlignment(VerticalAlignment.Center)
            .DockLeft(),
          control.VerticalAlignment(VerticalAlignment.Center).DockRight()
        )
    )
    .Classes("card")

/// A radio button per typed option in one group; the selected value
/// comes from the model.
let inline radioRow
  (
    options: ('T * string) list,
    group: string,
    selected: 'T,
    [<InlineIfLambda>] onPick: 'T -> unit
  ) : Control =
  let row((choice, label): 'T * string) : Control =
    let isChecked = choice = selected

    RadioButton(Content = label, GroupName = group)
      .IsChecked(Nullable isChecked)
      .Margin(0.0, 0.0, 12.0, 4.0)
      .OnClickHandler(fun _ _ -> onPick choice)

  WrapPanel().Children [| for option in options -> row option |]

/// One checkbox whose checked state follows an observable.
let inline checkRow
  (
    text: string,
    isOn: IObservable<bool>,
    [<InlineIfLambda>] onToggle: unit -> unit
  ) : Control =
  CheckBox(Content = text)
    .IsChecked(isOn |> Observable.map(fun on -> Nullable on))
    .OnClickHandler(fun _ _ -> onToggle())

/// The full page: one header row with the shell's back lead, the title
/// and the commands, the body scrolling in between, and the flow
/// actions when a wizard needs them.
let inline fluentPage(chrome: PageChrome) : Control =
  let commands: Control =
    if List.isEmpty chrome.Commands then
      TextBlock().Column(2)
    else
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(List.toArray chrome.Commands)
        .Column(2)

  let header =
    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")
    grid.MinHeight <- 48.0
    grid.Margin <- Thickness(24.0, 8.0, 24.0, 0.0)

    let lead: Control array =
      match chrome.Lead with
      | ValueSome control -> [|
          control
            .VerticalAlignment(VerticalAlignment.Center)
            .Margin(0.0, 0.0, 8.0, 0.0)
            .Column(0)
        |]
      | ValueNone -> [||]

    grid.Children(
      Array.append lead [| (pageTitle chrome.Title).Column(1); commands |]
    )
    |> ignore

    grid

  let body =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      // the content fills the viewport and starts at the page margin, so
      // rows of different lengths share one left edge and one right edge
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(
        StackPanel()
          .Margin(24.0)
          .Spacing(12.0)
          .MaxWidth(chrome.Measure)
          .HorizontalAlignment(HorizontalAlignment.Left)
          .Children(chrome.Body)
      )

  match chrome.Footer.Secondary, chrome.Footer.Primary with
  | [], [] -> DockPanel().LastChildFill(true).Children(header.DockTop(), body)
  | secondary, primary ->
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

/// A body-only page: the page contract without commands.
let inline page(titleText: string, content: Control) : Control =
  fluentPage {
    Lead = ValueNone
    Title = titleText
    Commands = []
    Footer = noFooter
    Body = content
    Measure = 760.0
  }
