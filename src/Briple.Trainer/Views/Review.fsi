module Briple.Trainer.Views.Review

// The review page for a stored plan: one model owns the draft, the week,
// the edit mode, the status and the note section state; the children are
// seeded views. The page tracks its derived exit phase, never publishes
// it by hand. A culture switch rebuilds the page from the model, so the
// draft and the note survive.

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The data of the page; the host window is the export picker's owner
/// and may be absent while the shell builds.
[<Struct>]
type ReviewPageProps = {
  Culture: PageCulture
  /// The shell's back control, placed in the page header.
  Lead: Control voption
  Plans: PlanStore
  Memories: MemoryStore
  Agent: IProvideAgent
  Host: Window voption
  Entity: PlanEntity
  /// The exit gate: the page tracks its derived phase.
  Gate: PageGate
}

/// The review page.
type ReviewPage =
  inherit UserControl

  new: props: ReviewPageProps -> ReviewPage
