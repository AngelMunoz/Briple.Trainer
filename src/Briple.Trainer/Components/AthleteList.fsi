module Briple.Trainer.Components.AthleteList

// The athletes page over the athlete store: active and archived slices
// enter the page model through its subscriptions; the rows keep their
// actions out of the open button. Navigation decisions go out through
// the constructor callback, and the content rebuilds when the culture
// changes.

open System
open System.Globalization
open System.Threading.Tasks

open Avalonia.Controls

open Briple.Trainer.Env
open Briple.Trainer.Persistence

/// The services the athletes page needs, built once at the route.
[<Struct>]
type AthleteListEnv = {
  /// The live active athletes.
  Observe: unit -> IObservable<AthleteEntity list>
  /// The live archived athletes.
  ObserveArchived: unit -> IObservable<AthleteEntity list>
  /// The active snapshot.
  Snapshot: unit -> AthleteEntity list
  /// The archived snapshot.
  SnapshotArchived: unit -> AthleteEntity list
  /// Writes the archive flag and reloads the store.
  Archive: Guid -> Task<Result<unit, exn>>
  /// Clears the archive flag and reloads the store.
  Restore: Guid -> Task<Result<unit, exn>>
  /// Deletes the athlete aggregate and reloads the store.
  Delete: Guid -> Task<Result<unit, exn>>
  Dialogs: IDialogs
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

/// The page data.
[<Struct>]
type AthleteListProps = {
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

/// What the page reports to its host.
[<Struct>]
type Event =
  | NewRequested
  | OpenRequested of Guid

/// The athletes page: active rows, archived rows when shown, and the
/// new-athlete entry.
type AthleteList =
  inherit UserControl

  new:
    env: AthleteListEnv *
    props: AthleteListProps *
    [<InlineIfLambda>] onEvent: (Event -> unit) ->
      AthleteList
