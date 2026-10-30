module Briple.Trainer.Components.Hub

open System

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI
open NXUI.Extensions

open MVUish.Avalonia

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
  InitialActive: int voption
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

type Model = { Active: int }

type Msg =
  | Selected of int
  | Activated of int

let init(props: HubProps) =
  {
    Active = props.InitialActive |> ValueOption.defaultValue -1
  },
  Cmd.none

let update (props: HubProps) (msg: Msg) (model: Model) =
  match msg with
  | Selected index ->
    let select =
      props.OnSelect
      |> ValueOption.map(fun select -> Cmd.ofAction(fun () -> select index))
      |> ValueOption.defaultValue Cmd.none

    { model with Active = index }, select
  | Activated index -> { model with Active = index }, Cmd.none

let subscribe (props: HubProps) (_: Model) : Sub<Msg> =
  props.Active
  |> ValueOption.map(fun active ->
    Sub.fromObservable "active" (active |> Observable.map Activated))
  |> ValueOption.defaultValue Sub.none

let view
  (props: HubProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let scroller =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Auto)
      .VerticalScrollBarVisibility(ScrollBarVisibility.Disabled)

  // the offset of each column: recomputed when the widths change, so a
  // resize keeps the selection pointing at the same column
  let offsets: float array = Array.zeroCreate props.Slots.Length

  let columns: StackPanel array = [|
    for index, item in props.Slots |> List.indexed ->
      let active =
        model |> Observable.map(fun current -> current.Active = index)

      let header =
        Button(Content = item.Header)
          .FontWeight(FontWeight.SemiBold)
          .HorizontalAlignment(HorizontalAlignment.Left)
          .OnClickHandler(fun _ _ -> dispatch(Selected index))
          .Classes("chrome")
          .Foreground(
            active |> Observable.map(fun on -> if on then accent else muted)
          )
          .FontWeight(
            active
            |> Observable.map(fun on ->
              if on then FontWeight.SemiBold else FontWeight.Normal)
          )

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

  // the model owns the selection: the scroller follows it and pans to it.
  // The binding lives on the scroller; it is released when the hub leaves
  // the tree, so a dropped page leaves no live subscription.
  let binding =
    scroller.Bind(
      ScrollViewer.OffsetProperty,
      model
      |> Observable.map(fun current ->
        if current.Active >= 0 && current.Active < offsets.Length then
          Vector(offsets[current.Active], 0.0)
        else
          scroller.Offset)
    )

  scroller.DetachedFromVisualTree.Add(fun _ -> binding.Dispose())

  match props.Title with
  | "" -> scroller
  | _ -> StackPanel().Spacing(8.0).Children(sectionTitle props.Title, scroller)

type Hub(props: HubProps) as this =
  inherit UserControl()

  do
    let render (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      view props model dispatch

    let program =
      Program.mkProgram init (update props) render
      |> Program.withSubscription(subscribe props)

    let root, _ = Program.runWithLifetime this props program

    this.Content <- root

let hub(props: HubProps) : Control = Hub(props) :> Control
