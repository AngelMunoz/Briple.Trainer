module Briple.Trainer.Views.Athletes

// The athlete detail page: the profile and the plan history. The plans
// section is a store subscription with keyed rows, so every write
// anywhere shows up without navigation and the rows survive. Memories
// live in the plan context (the review page), not in the profile.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The data of the page.
[<Struct>]
type DetailPageProps = {
  Culture: CultureInfo
  Plans: PlanStore
  Athlete: AthleteEntity
}

/// What the page reports to its host.
[<Struct>]
type Event =
  | GenerateRequested
  | ReviewRequested of planId: Guid

/// The detail page; live over the plan store.
type AthleteDetailPage =
  inherit UserControl

  new:
    props: DetailPageProps * [<InlineIfLambda>] onEvent: (Event -> unit) ->
      AthleteDetailPage
