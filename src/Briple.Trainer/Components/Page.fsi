module Briple.Trainer.Components.Page

// The page contract: the live culture and the view host. A page view is
// a pure function of the culture and the model; the host rebuilds it
// when the culture changes, so a language switch needs no navigation
// and no manual subscription.

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

/// What a model-backed page needs to render: the culture, the model
/// stream, the build function and the dispatch.
[<Struct>]
type PageView<'Model, 'Msg> = {
  Culture: PageCulture
  Model: IObservable<'Model>
  Build: CultureInfo -> 'Model -> Dispatch<'Msg> -> Control
  Dispatch: Dispatch<'Msg>
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

/// A stateless page whose content follows the culture.
val inline cultureView:
  culture: PageCulture * [<InlineIfLambda>] build: (CultureInfo -> Control) ->
    Control

/// A page with a model; the content rebuilds on culture changes only.
val cultureHost: view: PageView<'Model, 'Msg> -> Control

/// Follows the page's phase for its gate and stops when the page leaves
/// the tree. Gated pages are transient, so detach is the end of their
/// life; the gate never keeps a dead page's model subscribed.
val trackPhase:
  control: Control * gate: PageGate * phases: IObservable<PagePhase> -> unit

/// Registers the page's gate inputs and stops them when the page leaves
/// the tree. Returns the MVU subscription handle.
val listenGate: inputs: GateInputs -> IDisposable
