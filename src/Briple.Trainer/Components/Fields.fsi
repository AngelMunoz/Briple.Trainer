module Briple.Trainer.Components.Fields

// The Fluent control kit: the type ramp, buttons, inputs and the page
// contract. One accent (the app's), hierarchy from type size and
// weight, page commands in the single header row. `field` takes the
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

/// The page contract: the shell's back control as the header lead, the
/// title, the page commands, the flow actions and the body, read inside
/// a measure so prose and forms keep a readable width.
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

/// One page command: a plain button for the header row. The primary
/// action goes last (rightmost); mark it with `accent`.
val inline command:
  text: string * [<InlineIfLambda>] onClick: (unit -> unit) -> Button

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

/// One page command with its glyph before the label.
val inline iconCommand:
  text: string *
  geometry: StreamGeometry *
  [<InlineIfLambda>] onClick: (unit -> unit) ->
    Button

/// One content row: a two-line title and detail with a trailing glyph,
/// in a grid so the glyph stays at the row's end. The caller wraps it
/// in the tappable surface.
val inline contentRow:
  title: string *
  detail: string *
  [<InlineIfLambda>] trailing: (unit -> Control) ->
    Control

/// Makes every item container fill the items control's width, so rows of
/// different lengths share one left edge and one trailing edge.
val inline stretchItems: items: ItemsControl -> ItemsControl

/// A single-line text input with a placeholder; typing reports the
/// value, the model owns it.
val inline entry:
  placeholder: string *
  text: string *
  [<InlineIfLambda>] onChanged: (string -> unit) ->
    TextBox

/// One settings card row: the title left, the control right.
val inline cardRow: title: string * control: Control -> Control

/// A single-choice row of radio buttons over typed options; the control
/// owns the visual state, the value comes from the model.
val inline radioRow:
  options: ('T * string) list *
  group: string *
  selected: 'T *
  [<InlineIfLambda>] onPick: ('T -> unit) ->
    Control
    when 'T: equality

/// One checkbox whose checked state follows an observable; the
/// subscription is disposed when the control detaches.
val inline checkRow:
  text: string *
  isOn: IObservable<bool> *
  [<InlineIfLambda>] onToggle: (unit -> unit) ->
    Control

/// The full page: ONE header row (the shell's back lead, the title
/// beside it, the page commands to the right with the primary last),
/// the flow actions (when any) pinned to the bottom, and the body
/// scrolling in between, centered inside the measure.
val inline fluentPage: chrome: PageChrome -> Control

/// A body-only page: the page contract without commands.
val inline page: titleText: string * content: Control -> Control
