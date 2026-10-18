module Briple.Trainer.Navigation

// URL-to-section mapping for the nav pane. The router owns navigation
// history and the shell reads it through `IRouter.History`.

open System

/// URL-to-section mapping for the nav pane.
module Sections =

  /// The pane section a URL belongs to.
  val sectionOf: url: string -> string voption

/// The lifecycle state of a page the shell protects on exit.
type PagePhase =
  | Ready
  | UnsavedChanges
  | Working

/// Commands the shell pushes down into a page: the guard side of the
/// gate.
type PageCommand = | CancelRun

/// One page's exit gate, the page's viewmodel for leaving: the phase
/// the page publishes, the commands the shell pushes down, and the
/// leave decision. The page writes the phase once through `Track`; the
/// route guard and the OS close ask; everything about "may I leave"
/// lives here.
type PageGate =
  /// The page's live phase.
  member Phase: IObservable<PagePhase>

  /// The page's phase right now.
  member PhaseValue: PagePhase

  /// Follows the page's phase stream; the gate calls the subscription
  /// once, the page never subscribes. The first value replays.
  member Track: phases: IObservable<PagePhase> -> IDisposable

  /// The shell's command channel; the page subscribes once through the
  /// MVU program's declared input.
  member Commands: IObservable<PageCommand>

  /// Pushes a command down into the page.
  member Push: command: PageCommand -> unit

  /// Increments on every route activation, cached or not. Pages use it
  /// to reconcile their state when they become visible again.
  member Shown: IObservable<int>

  /// Registers the page's two gate inputs: shown again, and a cancel
  /// command from the shell. The gate owns the subscriptions; the page
  /// hands two callbacks and the MVU program returns the disposable.
  member Listen:
    onShown: (unit -> unit) * onCancel: (unit -> unit) -> IDisposable

  /// Signals one activation of the route.
  member MarkShown: unit -> unit

  /// Asks whether leaving is allowed at the current phase; the decide
  /// function owns the dialog and any teardown-on-leave.
  member AskLeave: unit -> Async<bool>

  /// The URL this gate belongs to.
  member Url: string

  new:
    url: string *
    [<InlineIfLambda>] decide: (PageGate -> PagePhase -> Async<bool>) ->
      PageGate

/// Create-or-get registry of gates by URL: one gate per page, so a
/// cached review of plan A never contaminates plan B.
type PageGates =
  /// The gate of the page at this URL, created on first use.
  member Gate: url: string -> PageGate

  /// Drops a gate after its page is gone: no phase, no commands, no
  /// subscriptions survive the visit.
  member Remove: url: string -> bool

  new:
    [<InlineIfLambda>] decide: (string -> PageGate -> PagePhase -> Async<bool>) ->
      PageGates

/// An ICommand over a plain action, for key bindings.
type ActionCommand =
  interface System.Windows.Input.ICommand

  new: [<InlineIfLambda>] action: (unit -> unit) -> ActionCommand
