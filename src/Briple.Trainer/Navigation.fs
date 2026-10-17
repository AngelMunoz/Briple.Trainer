module Briple.Trainer.Navigation

open System
open System.Reactive.Subjects
open System.Windows.Input

open FSharp.Control.Reactive

/// Pure back-stack operations over a most-recent-first URL list.
module History =

  let push (url: string) (stack: string list) : string list =
    match stack with
    | current :: _ when current = url -> stack
    | _ -> url :: List.truncate 99 stack

  /// Records a completed navigation. A URL that is the entry below the
  /// current top is a completed back navigation: the top collapses.
  let settle (url: string) (stack: string list) : string list =
    match stack with
    | _ :: previous :: _ when previous = url -> List.tail stack
    | _ -> push url stack

  /// The URL a back navigation goes to, without changing the stack.
  let back(stack: string list) : string option =
    match stack with
    | _ :: previous :: _ -> Some previous
    | _ -> None

  let canBack(stack: string list) : bool =
    match stack with
    | _ :: _ :: _ -> true
    | _ -> false

  /// The pane section a URL belongs to.
  let sectionOf(url: string) : string voption =
    if url = "/" || url.StartsWith("/athletes", StringComparison.Ordinal) then
      ValueSome "/"
    elif url = "/settings" then
      ValueSome "/settings"
    else
      ValueNone

/// The live back stack: URLs of completed navigations land in `Sync`,
/// `Back` reads where a back navigation goes.
type BackStack() =
  let stacks = new BehaviorSubject<string list>([])

  member _.Sync(url: string) =
    let current = stacks.Value
    let next = History.settle url current

    if next <> current then
      stacks.OnNext next

  member _.Back() : string option = History.back stacks.Value

  member _.Current: string option = stacks.Value |> List.tryHead

  member _.CanBack: IObservable<bool> = stacks |> Observable.map History.canBack

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
