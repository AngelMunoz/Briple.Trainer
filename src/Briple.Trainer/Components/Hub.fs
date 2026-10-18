module Briple.Trainer.Components.Hub

open System

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI
open NXUI.Extensions

open Briple.Trainer.Components.Fields
open Briple.Trainer.Theme

[<Struct>]
type HubSlot = {
  Header: Control
  Body: Control
  Width: float
}

[<Struct>]
type HubProps = {
  Title: string
  Slots: HubSlot list
  Active: IObservable<int> voption
  OnSelect: (int -> unit) voption
}

[<Struct>]
type WheelStep =
  | Pan of offset: float
  | Pass

[<Literal>]
let gutter = 24.0

/// One wheel notch of hub travel, in px.
[<Literal>]
let notch = 48.0

let inline slotView(header: Control, body: Control) : HubSlot = {
  Header = header
  Body = body
  Width = 400.0
}

let inline slot(header: string, body: Control) : HubSlot =
  slotView(TextBlock().Text(header), body)

/// A vertical wheel over the hub pans it sideways. The turn that would
/// leave the hub goes to the page, so a long page keeps scrolling once
/// the hub reaches either end.
let wheelStep(offset: float, max: float, delta: Vector) : WheelStep =
  // one classic notch arrives as 120; a touchpad sends fractions of it
  let notches = if abs delta.Y >= 120.0 then delta.Y / 120.0 else delta.Y

  let step = -notches * notch
  let limit = Math.Max(0.0, max)

  if step = 0.0 then Pass
  elif step < 0.0 && offset <= 1.0 then Pass
  elif step > 0.0 && offset >= limit - 1.0 then Pass
  else Pan(Math.Clamp(offset + step, 0.0, limit))

/// The columns share the viewport when every one keeps its minimum; when
/// they do not, each keeps the minimum and the hub scrolls.
let fitColumns
  (slots: HubSlot list, columns: StackPanel array, available: float)
  : unit =
  if columns.Length > 0 && available > 0.0 then
    let count = float columns.Length
    let share = (available - gutter * (count - 1.0)) / count

    for index in 0 .. columns.Length - 1 do
      columns[index].Width <- Math.Max(slots[index].Width, share)

let hub(props: HubProps) : Control =
  let scroller =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Auto)
      .VerticalScrollBarVisibility(ScrollBarVisibility.Disabled)

  // the offset of each column: recomputed when the widths change, so a
  // resize keeps the selection pointing at the same column
  let offsets: float array = Array.zeroCreate props.Slots.Length

  let scrollTo(index: int) : unit =
    if index >= 0 && index < offsets.Length then
      scroller.Offset <- Vector(offsets[index], 0.0)

  let activeHeader(index: int) : IObservable<bool> voption =
    props.Active
    |> ValueOption.map(fun active ->
      active |> Observable.map(fun current -> current = index))

  let columns: StackPanel array = [|
    for index, item in props.Slots |> List.indexed ->
      let header =
        let button =
          Button(Content = item.Header)
            .FontWeight(FontWeight.SemiBold)
            .HorizontalAlignment(HorizontalAlignment.Left)
            .OnClickHandler(fun _ _ ->
              scrollTo index
              props.OnSelect |> ValueOption.iter(fun select -> select index))
            .Classes("chrome")

        match activeHeader index with
        | ValueSome active ->
          button
            .Foreground(
              active |> Observable.map(fun on -> if on then accent else muted)
            )
            .FontWeight(
              active
              |> Observable.map(fun on ->
                if on then FontWeight.SemiBold else FontWeight.Normal)
            )
        | ValueNone -> button

      StackPanel()
        .Width(item.Width)
        .Spacing(8.0)
        .Children(header, Border().Child(item.Body).Classes("card"))
  |]

  let measureOffsets() =
    let mutable x = 0.0

    for index in 0 .. columns.Length - 1 do
      offsets[index] <- x
      x <- x + columns[index].Width + gutter

  measureOffsets()

  let content =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(gutter)
      .Children(columns |> Array.map(fun column -> column :> Control))

  scroller.Content <- content

  // a resize re-shares the width: every column stays whole on a wide
  // window and keeps its minimum on a narrow one
  scroller.SizeChanged.Add(fun _ ->
    fitColumns(props.Slots, columns, scroller.Bounds.Width)
    measureOffsets())

  // the handler sits on the content, before the scroller's presenter
  // reads the event, and keeps the turn while the hub can still pan
  content.PointerWheelChanged.Add(fun args ->
    let limit = Math.Max(0.0, scroller.Extent.Width - scroller.Viewport.Width)

    match wheelStep(scroller.Offset.X, limit, args.Delta) with
    | Pan offset ->
      scroller.Offset <- Vector(offset, 0.0)
      args.Handled <- true
    | Pass -> ())

  // the page owns the selection: the hub follows it and pans to it.
  // The binding lives on the scroller; it is released when the hub
  // leaves the tree, so a dropped page leaves no live subscription.
  props.Active
  |> ValueOption.iter(fun active ->
    let binding =
      scroller.Bind(
        ScrollViewer.OffsetProperty,
        active
        |> Observable.filter(fun index -> index >= 0 && index < offsets.Length)
        |> Observable.map(fun index -> Vector(offsets[index], 0.0))
      )

    scroller.DetachedFromVisualTree.Add(fun _ -> binding.Dispose()))

  match props.Title with
  | "" -> scroller
  | _ -> StackPanel().Spacing(8.0).Children(sectionTitle props.Title, scroller)
