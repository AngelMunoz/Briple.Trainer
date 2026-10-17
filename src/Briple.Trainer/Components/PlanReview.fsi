module Briple.Trainer.Components.PlanReview

// The plan review as an MVUish component: props in (the culture and the
// parsed plan), events out (PlanChanged after every field edit). The
// view reads first: the weeks render as a pivot, every exercise is a
// text line, and one row at a time enters an explicit edit mode (commit
// keeps the edits, revert restores the last snapshot). Only the
// verbatim fields are editable (reps, RIR, rest, notes); edits flow
// back as the full Plan tree.

open Avalonia.Controls

open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types

/// The verbatim fields a row exposes for editing.
[<Struct>]
type ExerciseField =
  | RepsField
  | RirField
  | RestField
  | NotesField

/// The position of one exercise inside the plan tree.
[<Struct>]
type RowPath = {
  Block: int
  Variant: int
  Week: int
  Day: int
  Order: int
}

/// What the component reports to its parent: the edited tree and the
/// navigation state the page keeps across rebuilds.
type Event =
  | PlanChanged of Plan
  | ViewChanged of week: int * editing: bool

/// The component inputs.
[<Struct>]
type Props = {
  Culture: System.Globalization.CultureInfo
  Parsed: ParsedPlan
  /// The week the page had shown, when it comes back from a rebuild.
  Week: int voption
  /// The edit mode the page had, when it comes back from a rebuild.
  Editing: bool
}

type Model = {
  Plan: Plan
  /// The last committed tree: what revert restores.
  Snapshot: Plan
  /// The week the pivot shows, by number.
  Week: int
  /// The page-level edit mode: on, every row shows its fields; off,
  /// the tree reads as prose.
  Editing: bool
}

type Msg =
  | SetField of path: RowPath * field: ExerciseField * text: string
  | SetWeek of week: int
  | SetEditing of on: bool
  | RevertEdit

/// The review of one parsed plan.
type PlanReview =
  inherit UserControl

  new: props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> PlanReview
