module Briple.Trainer.Components.Fields

// The flat Metro control factories: square corners, one accent, the app
// font. Every control the app renders comes from here or from the MVUish
// components, so the language stays uniform. `field` takes the input as a
// `unit -> Control` hole (content projection): the caller decides when the
// control is built.

open System
open Avalonia.Controls
open Avalonia.Media

/// A section title.
val inline title: text: string -> Control

/// A small muted label.
val inline label: text: string -> Control

/// A muted label over the control the hole builds.
val inline field:
  labelText: string * [<InlineIfLambda>] input: (unit -> Control) -> Control

/// A flat button.
val inline button:
  text: string * [<InlineIfLambda>] onClick: (unit -> unit) -> Button

/// The accent button for the primary action.
val inline primaryButton:
  text: string * [<InlineIfLambda>] onClick: (unit -> unit) -> Button

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

/// A page shell: title over scrolling content.
val inline page: titleText: string * content: Control -> Control
