module Briple.Trainer.Components.Hub

// The horizontal panning surface (the W8.1 hub, on Fluent): an optional
// title above, columns beside. Headers select a column; the page can
// drive the selection. The columns share the viewport when they fit, so
// a wide window shows every column whole, and scroll when they do not.
// A vertical wheel pans the hub until the hub reaches an end; there the
// turn goes to the page, so both axes keep working.

open System

open Avalonia
open Avalonia.Controls

/// One hub column: its header, its body, its width. The width is the
/// minimum the column keeps when the viewport cannot hold every column.
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
  /// Pan to this offset: the current offset plus one notch, clamped.
  | Pan of offset: float
  /// Leave the event to the page.
  | Pass

/// The gutter between columns, in px.
[<Literal>]
val gutter: float = 24.0

/// One hub column at the default width (400): a readable day card.
val inline slot: header: string * body: Control -> HubSlot

/// One hub column with a control header (a resource-bound label).
val inline slotView: header: Control * body: Control -> HubSlot

/// The wheel decision: the hub takes the vertical wheel for a horizontal
/// pan while it can move that way, and passes the turn at either end.
/// `max` is the hub's scrollable extent; the result is the offset to pan
/// to.
val wheelStep: offset: float * max: float * delta: Vector -> WheelStep

/// The hub: title (empty means no title row) over the columns.
val hub: props: HubProps -> Control
