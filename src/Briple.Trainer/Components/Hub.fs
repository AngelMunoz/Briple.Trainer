module Briple.Trainer.Components.Hub

open System

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI
open NXUI.Extensions

open Briple.Trainer.Components.Fields
open Briple.Trainer.Theme

[<Struct>]
type HubSlot = {
  Header: string
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
  | Pan of delta: float
  | Pass

[<Literal>]
let gutter = 24.0

let inline slot(header: string, body: Control) : HubSlot = {
  Header = header
  Body = body
  Width = 400.0
}

let inline wheelOffset(current: float, delta: float, max: float) : float =
  Math.Clamp(current + delta, 0.0, Math.Max(0.0, max))

/// Only Shift + vertical pans the hub. A plain vertical wheel passes to
/// the page, so the body keeps its normal scroll.
let wheelStep(delta: Vector, shift: bool) : WheelStep =
  if shift && abs delta.Y > 0.0 then
    Pan(-delta.Y * 48.0)
  else
    Pass

let hub(props: HubProps) : Control =
  let scroller =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Auto)
      .VerticalScrollBarVisibility(ScrollBarVisibility.Disabled)

  let offsets = ResizeArray<float>()
  let mutable x = 0.0

  for item in props.Slots do
    offsets.Add x
    x <- x + item.Width + gutter

  let extent = x - gutter

  let scrollTo(index: int) : unit =
    if index >= 0 && index < offsets.Count then
      scroller.Offset <- Vector(offsets[index], 0.0)

  let activeHeader(index: int) : IObservable<bool> voption =
    props.Active
    |> ValueOption.map(fun active ->
      active |> Observable.map(fun current -> current = index))

  let columns: Control array = [|
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

      let card = Border().Child(item.Body).Classes("card")

      StackPanel().Width(item.Width).Spacing(8.0).Children(header, card)
  |]

  scroller.Content <-
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(gutter)
      .Margin(0.0, 0.0, gutter, 0.0)
      .Children(columns)

  // the page owns the selection: the hub follows it and pans to it.
  // The binding lives on the scroller; it is released when the hub
  // leaves the tree, so a dropped page leaves no live subscription.
  props.Active
  |> ValueOption.iter(fun active ->
    let binding =
      scroller.Bind(
        ScrollViewer.OffsetProperty,
        active
        |> Observable.filter(fun index -> index >= 0 && index < offsets.Count)
        |> Observable.map(fun index -> Vector(offsets[index], 0.0))
      )

    scroller.DetachedFromVisualTree.Add(fun _ -> binding.Dispose()))

  // a plain vertical wheel goes to the page; Shift + wheel pans
  scroller.PointerWheelChanged.Add(fun args ->
    let shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift)

    match wheelStep(args.Delta, shift) with
    | Pan delta ->
      let max = extent - scroller.Viewport.Width

      scroller.Offset <-
        Vector(wheelOffset(scroller.Offset.X, delta, max), 0.0)

      args.Handled <- true
    | Pass -> ())

  match props.Title with
  | "" -> scroller
  | _ -> StackPanel().Spacing(8.0).Children(sectionTitle props.Title, scroller)
