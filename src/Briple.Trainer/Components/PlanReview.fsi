module Briple.Trainer.Components.PlanReview

// The plan review as an MVUish component: props in (the culture and the
// parsed plan), events out (PlanChanged after every field edit). The view
// mirrors the Briple app: variant sections, week badges with the
// mesocycle RIR, circuit groups and one editable row per exercise. Only
// the verbatim fields are editable (reps, RIR, rest, notes); edits flow
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

type Model = { Plan: Plan }

type Msg = SetField of path: RowPath * field: ExerciseField * text: string

/// The review of one parsed plan.
type PlanReview =
  inherit UserControl

  new: props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> PlanReview
