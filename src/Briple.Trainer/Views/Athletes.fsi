module Briple.Trainer.Views.Athletes

// The athlete detail page: the profile and the plan history. The plans
// section binds straight to the store, so every write shows up without
// navigation, and the content rebuilds when the culture changes.
// Memories live in the plan context (the review page), not here.

open System
open System.Globalization
open System.Threading.Tasks

open Avalonia.Controls

open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The services the detail page needs, built once at the route.
[<Struct>]
type AthleteDetailEnv = {
  Plans: PlanStore
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
type DetailPageProps = {
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  Athlete: AthleteEntity
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

/// What the page reports to its host.
[<Struct>]
type Event =
  | GenerateRequested
  | ReviewRequested of planId: Guid
  /// The page changed the athlete; the host returns to the list.
  | BackRequested

/// The detail page; live over the plan store.
type AthleteDetailPage =
  inherit UserControl

  new:
    env: AthleteDetailEnv *
    props: DetailPageProps *
    [<InlineIfLambda>] onEvent: (Event -> unit) ->
      AthleteDetailPage
