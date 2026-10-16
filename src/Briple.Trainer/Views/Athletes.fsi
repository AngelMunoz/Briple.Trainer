module Briple.Trainer.Views.Athletes

// The athlete detail page: profile, the quick-mark form, the proposed
// memories with their confirm/discard cards and the plan history. Store
// writes reload the page in place; the callbacks navigate.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Persistence

/// The data and callbacks of the page.
[<Struct>]
type DetailPageProps = {
  Culture: CultureInfo
  Store: IStore
  Athlete: AthleteEntity
  Memories: MemoryEntity list
  Plans: PlanEntity list
  OnGenerate: unit -> unit
  OnReview: Guid -> unit
}

/// The detail page; store writes reload it in place.
type AthleteDetailPage =
  inherit UserControl

  new: props: DetailPageProps -> AthleteDetailPage
