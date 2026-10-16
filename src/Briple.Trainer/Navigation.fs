module Briple.Trainer.Navigation

open System
open System.Reactive.Subjects
open System.Windows.Input

/// Pure back-stack operations over a most-recent-first URL list.
module History =

  /// Pushes unless the URL repeats the current top; depth caps at 100.
  let push (url: string) (stack: string list) : string list =
    match stack with
    | current :: _ when current = url -> stack
    | _ -> url :: List.truncate 99 stack

  /// The URL a back navigation goes to.
  let back(stack: string list) : string option =
    match stack with
    | _ :: previous :: _ -> Some previous
    | _ -> None

  /// Drops the current entry.
  let pop(stack: string list) : string list =
    match stack with
    | _ :: rest -> rest
    | [] -> []

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
/// `Back` pops and answers where to navigate.
type BackStack() =
  let stacks = new BehaviorSubject<string list>([])

  member _.Sync(url: string) =
    stacks.OnNext(History.push url stacks.Value)

  member _.Back() : string option =
    match History.back stacks.Value with
    | Some url ->
      stacks.OnNext(History.pop stacks.Value)
      Some url
    | None -> None

  member _.Current: string option = stacks.Value |> List.tryHead

  member _.CanBack: IObservable<bool> = stacks |> Observable.map History.canBack

/// An ICommand over a plain action, for key bindings.
type ActionCommand([<InlineIfLambda>] action: unit -> unit) =
  let canExecuteChanged = Event<EventHandler, EventArgs>()

  interface ICommand with
    member _.CanExecute(_: obj) = true

    member _.Execute(_: obj) = action()

    [<CLIEvent>]
    member _.CanExecuteChanged = canExecuteChanged.Publish
