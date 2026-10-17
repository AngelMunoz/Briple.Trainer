module Briple.Trainer.Components.Fields

// The Fluent control kit: the type ramp, buttons, chips, inputs and the
// page contract. One accent (the app's), hierarchy from type size and
// weight, page commands in a CommandBar at the top. `field` takes the
// input as a `unit -> Control` hole (content projection): the caller
// decides when the control is built.

open System
open Avalonia.Controls
open Avalonia.Media

/// Flow actions of a page footer: secondaries keep to the left,
/// primaries claim the right.
[<Struct>]
type FooterBar = {
  Secondary: Control list
  Primary: Control list
}

/// The empty footer, for pages without flow actions.
val noFooter: FooterBar

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
val inline pageTitle: text: string -> Control

/// The section header level: 20 SemiBold.
val inline sectionTitle: text: string -> Control

/// A secondary-text caption: 12, muted.
val inline label: text: string -> Control

/// One labeled field: label above, input below.
val inline field:
  labelText: string * [<InlineIfLambda>] input: (unit -> Control) -> Control

/// A plain button.
val inline button:
  text: string * [<InlineIfLambda>] onClick: (unit -> unit) -> Button

/// The primary action button: the accent fill.
val inline primaryButton:
  text: string * [<InlineIfLambda>] onClick: (unit -> unit) -> Button

/// A tertiary text action.
val inline hyperlinkButton:
  text: string * [<InlineIfLambda>] onClick: (unit -> unit) -> Control

/// One command for the page's CommandBar.
val inline command:
  text: string * [<InlineIfLambda>] onClick: (unit -> unit) -> CommandBarButton

/// A 20px glyph that takes the text color of its container: PathIcon as
/// the Fluent theme sizes and colors it.
val inline icon: geometry: StreamGeometry -> Control

/// A glyph at an explicit size and tone, for standalone composition.
val inline glyph:
  geometry: StreamGeometry * size: float * tone: IBrush -> Control

/// A glyph before its label: the content of an action control.
val inline glyphLabel: geometry: StreamGeometry * text: string -> Control

/// A plain button with a glyph before its label.
val inline iconButton:
  text: string *
  geometry: StreamGeometry *
  [<InlineIfLambda>] onClick: (unit -> unit) ->
    Button

/// The primary action button with a glyph before its label.
val inline iconPrimaryButton:
  text: string *
  geometry: StreamGeometry *
  [<InlineIfLambda>] onClick: (unit -> unit) ->
    Button

/// One CommandBar command with its glyph left of the label.
val inline iconCommand:
  text: string *
  geometry: StreamGeometry *
  [<InlineIfLambda>] onClick: (unit -> unit) ->
    CommandBarButton

/// A single-choice chip row; the selected option renders in accent.
val inline choiceChips:
  options: string list *
  selected: string *
  [<InlineIfLambda>] onPick: (string -> unit) ->
    Control

/// A multi-choice chip row; the selected options render in accent.
val inline multiChips:
  options: string list *
  selected: string list *
  [<InlineIfLambda>] onToggle: (string -> unit) ->
    Control

/// A single-choice chip row whose selection follows an observable; the
/// row builds once and the chip backgrounds react.
val inline reactiveChoiceChips:
  options: string list *
  selected: IObservable<string> *
  [<InlineIfLambda>] onPick: (string -> unit) ->
    Control

/// A multi-choice chip row whose selection follows an observable; the
/// row builds once and the chip backgrounds react.
val inline reactiveMultiChips:
  options: string list *
  selected: IObservable<string list> *
  [<InlineIfLambda>] onToggle: (string -> unit) ->
    Control

/// A single-line text input with a placeholder.
val inline entry:
  placeholder: string *
  text: string *
  [<InlineIfLambda>] onChanged: (string -> unit) ->
    TextBox

/// A single-choice row of radio buttons; the control owns the visual
/// state, so the selection is never ambiguous.
val inline radioRow:
  options: string list *
  group: string *
  initial: string *
  [<InlineIfLambda>] onPick: (string -> unit) ->
    Control

/// One checkbox whose checked state follows an observable; the
/// subscription is disposed when the control detaches.
val inline checkRow:
  text: string *
  isOn: IObservable<bool> *
  [<InlineIfLambda>] onToggle: (unit -> unit) ->
    Control

/// The CommandBar of a page: the commands land as primaries, dynamic
/// overflow takes the rest when the window runs narrow.
val inline commandBar: commands: Control list -> Control

/// The full page: the CommandBar under the title, the body below, all
/// inside the measure.
val inline fluentPage: chrome: PageChrome -> Control

/// A body-only page: the page contract without commands.
val inline page: titleText: string * content: Control -> Control
