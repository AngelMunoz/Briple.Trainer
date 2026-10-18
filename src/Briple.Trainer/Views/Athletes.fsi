module Briple.Trainer.Views.Athletes

// The athlete detail page: the profile and the plan history. The plans
// section binds straight to the store, so every write shows up without
// navigation, and the content rebuilds when the culture changes.
// Memories live in the plan context (the review page), not here.

open System

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The data of the page.
[<Struct>]
type DetailPageProps = {
  Culture: PageCulture
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
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
