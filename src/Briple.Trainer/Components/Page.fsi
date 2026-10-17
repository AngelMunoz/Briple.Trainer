module Briple.Trainer.Components.Page

// The page contract: the live culture, the gate inputs and the helpers
// that tie a page's lifetime to its subscriptions. Static text reads the
// resource dictionary; composed text reads the culture stream where it
// is rendered. A page is built once, so a language switch never rebuilds
// it or navigates.

open System
open System.Globalization

open Avalonia.Controls

open MVUish.Avalonia

open Briple.Trainer.Navigation

/// The live culture of a page: the value now, read on demand, and
/// every switch after.
[<Struct>]
type PageCulture = {
  Current: unit -> CultureInfo
  Changed: IObservable<CultureInfo>
}

/// The two gate inputs a page listens to. The control is the page, so
/// the listener dies with it.
[<Struct>]
type GateInputs = {
  Control: Control
  Gate: PageGate
  OnShown: unit -> unit
  OnCancel: unit -> unit
}

/// Follows the page's phase for its gate and stops when the page leaves
/// the tree. Gated pages are transient, so detach is the end of their
/// life; the gate never keeps a dead page's model subscribed.
val trackPhase:
  control: Control * gate: PageGate * phases: IObservable<PagePhase> -> unit

/// Registers the page's gate inputs and stops them when the page leaves
/// the tree. Returns the MVU subscription handle.
val listenGate: inputs: GateInputs -> IDisposable
