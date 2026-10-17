module Briple.Trainer.Components.Hub

// The horizontal panning surface (the W8.1 hub, on Fluent): an optional
// title above, columns beside, the trailing column peeking to invite the
// pan. Headers select a column; the page can drive the selection. The
// hub never captures a plain vertical wheel, so the page keeps
// scrolling normally.

open System

open Avalonia
open Avalonia.Controls

/// One hub column: its header, its body, its width.
[<Struct>]
type HubSlot = {
  Header: Control
  Body: Control
  Width: float
}

/// What the hub reads: the columns, the active column the page owns and
/// the header click callback.
[<Struct>]
type HubProps = {
  Title: string
  Slots: HubSlot list
  /// The active column; -1 means none. The hub pans to it and marks the
  /// header.
  Active: IObservable<int> voption
  /// A header click; the page updates its model. The hub pans anyway.
  OnSelect: (int -> unit) voption
}

/// How the hub answers one wheel event.
[<Struct>]
type WheelStep =
  /// Pan by this many pixels.
  | Pan of delta: float
  /// Leave the event to the page.
  | Pass

/// The gutter between columns and the trailing peek, in px.
[<Literal>]
val gutter: float = 24.0

/// One column at the default width (400): a readable day card.
val inline slot: header: string * body: Control -> HubSlot

/// One hub column with a control header (a resource-bound label).
val inline slotView: header: Control * body: Control -> HubSlot

/// Where a wheel nudge lands: the delta applied, clamped to the hub's
/// scrollable extent.
val inline wheelOffset: current: float * delta: float * max: float -> float

/// The wheel decision: only Shift + vertical pans the hub. A plain
/// vertical wheel always passes to the page.
val wheelStep: delta: Vector * shift: bool -> WheelStep

/// The hub: title (empty means no title row) over the columns.
val hub: props: HubProps -> Control
