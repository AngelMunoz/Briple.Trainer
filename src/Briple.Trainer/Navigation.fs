module Briple.Trainer.Navigation

open System
open System.Reactive.Subjects
open System.Windows.Input

open FSharp.Control.Reactive

/// URL-to-section mapping for the nav pane.
module Sections =

  /// The pane section a URL belongs to.
  let sectionOf(url: string) : string voption =
    match url with
    | "/" -> ValueSome "/"
    | url when url.StartsWith("/athletes", StringComparison.Ordinal) ->
      ValueSome "/"
    | "/settings" -> ValueSome "/settings"
    | _ -> ValueNone

/// The lifecycle state of a page the shell protects on exit.
type PagePhase =
  | Ready
  | UnsavedChanges
  | Working

/// Commands the shell pushes down into a page: the guard side of the
/// gate.
type PageCommand = | CancelRun

/// One page's exit gate: the phase the page tracks, the commands the
/// shell pushes down, and the leave decision.
type PageGate
  (url: string, [<InlineIfLambda>] decide: PageGate -> PagePhase -> Async<bool>)
  =
  let phase = new BehaviorSubject<PagePhase>(Ready)
  let commands = new System.Reactive.Subjects.Subject<PageCommand>()
  let shown = new BehaviorSubject<int>(0)
  let mutable listener: IDisposable voption = ValueNone

  member _.Phase: IObservable<PagePhase> = phase :> _

  member _.PhaseValue: PagePhase = phase.Value

  member _.Track(phases: IObservable<PagePhase>) : IDisposable =
    phases
    |> Observable.distinctUntilChanged
    |> Observable.subscribe phase.OnNext

  member _.Commands: IObservable<PageCommand> = commands :> _

  member _.Push(command: PageCommand) : unit = commands.OnNext command

  member _.Shown: IObservable<int> = shown :> _

  member _.MarkShown() : unit = shown.OnNext(shown.Value + 1)

  member _.Listen(onShown: unit -> unit, onCancel: unit -> unit) : IDisposable =
    // one live page per URL: a new visit replaces the previous listener so
    // a dropped page never receives the shell's commands
    listener |> ValueOption.iter(fun previous -> previous.Dispose())

    let commands =
      commands
      |> Observable.subscribe(function
        | CancelRun -> onCancel())

    let activations =
      shown |> Observable.skip 1 |> Observable.subscribe(fun _ -> onShown())

    let current = {
      new IDisposable with
        member _.Dispose() =
          commands.Dispose()
          activations.Dispose()
    }

    listener <- ValueSome current
    current

  member this.AskLeave() : Async<bool> = decide this phase.Value

  member _.Url: string = url

/// Create-or-get registry of gates by URL: one gate per page, so a
/// cached review of plan A never contaminates plan B.
type PageGates
  ([<InlineIfLambda>] decide: string -> PageGate -> PagePhase -> Async<bool>) =
  let gates =
    System.Collections.Concurrent.ConcurrentDictionary<string, PageGate>()

  member _.Gate(url: string) : PageGate =
    gates.GetOrAdd(url, fun url -> PageGate(url, decide url))

  member _.Remove(url: string) : bool =
    match gates.TryRemove url with
    | true, _ -> true
    | false, _ -> false

/// An ICommand over a plain action, for key bindings.
type ActionCommand([<InlineIfLambda>] action: unit -> unit) =
  let canExecuteChanged = Event<EventHandler, EventArgs>()

  interface ICommand with
    member _.CanExecute(_: obj) = true

    member _.Execute(_: obj) = action()

    [<CLIEvent>]
    member _.CanExecuteChanged = canExecuteChanged.Publish
