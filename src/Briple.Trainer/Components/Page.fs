module Briple.Trainer.Components.Page

open System
open System.Globalization

open Avalonia.Controls

open MVUish.Avalonia

open Briple.Trainer.Navigation

[<Struct>]
type PageCulture = {
  Current: unit -> CultureInfo
  Changed: IObservable<CultureInfo>
}

[<Struct>]
type GateInputs = {
  Control: Control
  Gate: PageGate
  OnShown: unit -> unit
  OnCancel: unit -> unit
}

let trackPhase
  (control: Control, gate: PageGate, phases: IObservable<PagePhase>)
  : unit =
  let subscription = gate.Track phases
  control.DetachedFromVisualTree.Add(fun _ -> subscription.Dispose())

let listenGate(inputs: GateInputs) : IDisposable =
  let subscription = inputs.Gate.Listen(inputs.OnShown, inputs.OnCancel)

  inputs.Control.DetachedFromVisualTree.Add(fun _ -> subscription.Dispose())

  subscription
