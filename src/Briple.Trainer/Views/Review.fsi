module Briple.Trainer.Views.Review

// The review page for a stored plan: one model owns the draft, the week,
// the edit mode, the status and the note section state; the children are
// seeded views. The page tracks its derived exit phase, never publishes
// it by hand. A culture switch rebuilds the page from the model, so the
// draft and the note survive.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The services the review page needs, built once at the route; the
/// host window is the export picker's owner and may be absent while the
/// shell builds.
[<Struct>]
type ReviewEnv = {
  Plans: PlanStore
  Memories: MemoryStore
  Agent: IProvideAgent
  Host: Window voption
  Logger: Microsoft.Extensions.Logging.ILogger
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

/// The data of the page.
[<Struct>]
type ReviewPageProps = {
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  Entity: PlanEntity
  /// The exit gate: the page tracks its derived phase.
  Gate: PageGate
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

/// The review page.
type ReviewPage =
  inherit UserControl

  new: env: ReviewEnv * props: ReviewPageProps -> ReviewPage
