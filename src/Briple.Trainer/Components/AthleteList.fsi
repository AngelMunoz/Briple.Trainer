module Briple.Trainer.Components.AthleteList

// The athletes page as an MVUish component over the athlete store: the
// list lives in the model, the subscription turns store emissions into
// refresh messages, and the navigation decisions go out through the
// constructor callback. The keyed rows survive every emission.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Persistence

/// The data source of the page.
[<Struct>]
type AthleteListProps = {
  Culture: CultureInfo
  /// The live athletes list, straight from the store.
  Athletes: IObservable<AthleteEntity list>
}

type Model = { Athletes: AthleteEntity list }

type Msg = Refreshed of AthleteEntity list

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
