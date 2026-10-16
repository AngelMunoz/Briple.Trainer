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

/// What the component reports to its parent.
type Event = PlanChanged of Plan

/// The component inputs.
[<Struct>]
type Props = {
  Culture: System.Globalization.CultureInfo
  Parsed: ParsedPlan
}

type Model = {
  Plan: Plan
  /// The last committed tree: what revert restores.
  Snapshot: Plan
  /// The week the pivot shows.
  Week: int
  /// The row in explicit edit mode.
  Editing: RowPath voption
}

type Msg =
  | SetField of path: RowPath * field: ExerciseField * text: string
  | SetWeek of week: int
  | BeginEdit of path: RowPath
  | EndEdit
  | RevertEdit

/// The review of one parsed plan.
type PlanReview =
  inherit UserControl

  new: props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> PlanReview
