module Briple.Trainer.Components.Page

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open NXUI.Extensions

open FSharp.Control.Reactive

open MVUish.Avalonia

open Briple.Trainer.Navigation

[<Struct>]
type PageCulture = {
  Current: unit -> CultureInfo
  Changed: IObservable<CultureInfo>
}

[<Struct>]
type PageView<'Model, 'Msg> = {
  Culture: PageCulture
  Model: IObservable<'Model>
  Build: CultureInfo -> 'Model -> Dispatch<'Msg> -> Control
  Dispatch: Dispatch<'Msg>
}

[<Struct>]
type GateInputs = {
  Control: Control
  Gate: PageGate
  OnShown: unit -> unit
  OnCancel: unit -> unit
}

/// A stateless page whose content follows the culture.
let inline cultureView
  (culture: PageCulture, [<InlineIfLambda>] build: CultureInfo -> Control)
  : Control =
  let pages = culture.Changed |> Observable.map build
  ContentControl().Content(pages.ToBinding())

/// A page with a model; the content rebuilds on culture changes only.
let cultureHost(view: PageView<'Model, 'Msg>) : Control =
  let pages: IObservable<Control> =
    Observable.combineLatest view.Culture.Changed view.Model
    |> Observable.distinctUntilChangedKey fst
    |> Observable.map(fun (culture, model) ->
      view.Build culture model view.Dispatch)

  ContentControl().Content(pages.ToBinding())

let trackPhase
  (control: Control, gate: PageGate, phases: IObservable<PagePhase>)
  : unit =
  let subscription = gate.Track phases
  control.DetachedFromVisualTree.Add(fun _ -> subscription.Dispose())

let listenGate(inputs: GateInputs) : IDisposable =
  let subscription = inputs.Gate.Listen(inputs.OnShown, inputs.OnCancel)

  inputs.Control.DetachedFromVisualTree.Add(fun _ -> subscription.Dispose())

  subscription
