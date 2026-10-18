module Briple.Trainer.Components.AthleteList

// The athletes page as a stateless view over the athlete store: the
// list is bound straight to the store (no model copy, no manual
// subscription), and the navigation decisions go out through the
// constructor callback. The content rebuilds when the culture changes.

open System
open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Persistence

/// The data source of the page.
[<Struct>]
type AthleteListProps = {
  Culture: PageCulture
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  /// The live athletes list, straight from the store.
  Athletes: IObservable<AthleteEntity list>
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
    props: AthleteListProps * [<InlineIfLambda>] onEvent: (Event -> unit) ->
      AthleteList
