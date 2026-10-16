module Briple.Trainer.Views.Review

// The review page for a stored plan: the PlanReview component for field
// edits, save back into the Raw (the source of truth), the .briple
// export, and the note interpretation — the trainer writes what they
// observed and the agent maps it to the closed surface of this plan;
// the parts that do not map are announced and logged.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The data of the page; the host window is the export picker's owner
/// and may be absent while the shell builds.
[<Struct>]
type ReviewPageProps = {
  Culture: CultureInfo
  Plans: PlanStore
  Memories: MemoryStore
  Agent: IProvideAgent
  Host: Window voption
  Entity: PlanEntity
}

/// The review page.
type ReviewPage =
  inherit UserControl

  new: props: ReviewPageProps -> ReviewPage
