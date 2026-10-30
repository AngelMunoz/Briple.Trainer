module Briple.Trainer.Components.AthleteList

// The athletes page as a stateless view over the athlete store: the
// list is bound straight to the store (no model copy, no manual
// subscription), and the navigation decisions go out through the
// constructor callback. The content rebuilds when the culture changes.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Persistence

/// The services the athletes page needs, built once at the route.
[<Struct>]
type AthleteListEnv = {
  Observe: unit -> IObservable<AthleteEntity list>
  Snapshot: unit -> AthleteEntity list
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

/// The athletes page: one row per athlete, plus the new-athlete entry.
type AthleteList =
  inherit UserControl

  new:
    env: AthleteListEnv *
    props: AthleteListProps *
    [<InlineIfLambda>] onEvent: (Event -> unit) ->
      AthleteList
