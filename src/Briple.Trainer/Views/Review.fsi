module Briple.Trainer.Views.Review

// The review page for a stored plan: the PlanReview component for field
// edits, save back into the Raw (the source of truth) and the .briple
// export through a file picker on the host window.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Persistence

/// The data of the page; the host window is the export picker's owner and
/// may be absent while the shell builds.
[<Struct>]
type ReviewPageProps = {
  Culture: CultureInfo
  Store: IStore
  Host: Window voption
  Entity: PlanEntity
}

/// The review page.
type ReviewPage =
  inherit UserControl

  new: props: ReviewPageProps -> ReviewPage
