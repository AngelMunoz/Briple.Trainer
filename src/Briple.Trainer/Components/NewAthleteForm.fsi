module Briple.Trainer.Components.NewAthleteForm

// The registration form as an MVUish page: props in (the live culture,
// the shell's back lead, the exit gate), events out (Saved carries the
// profile; Cancelled closes). The form asks for the name, the gender
// and at least one goal. The gender starts at Hombre and the model owns
// it, so the visible selection and the saved value never disagree. The
// phase is derived from the model, never published by a branch.

open System

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Navigation

/// What the form reports to its parent.
[<Struct>]
type Event =
  | Saved of Athlete
  | Cancelled

/// The form lifecycle: one save per filling, by construction.
type FormPhase =
  | Filling
  | Spent

/// The form inputs.
[<Struct>]
type Props = {
  Culture: PageCulture
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  /// The exit gate: the form tracks its derived phase.
  Gate: PageGate
}

type Model = {
  Name: string
  Gender: Gender
  Goals: PlanGoal list
  Phase: FormPhase
}

type Msg =
  | SetName of string
  | PickGender of Gender
  | ToggleGoal of PlanGoal
  | Save
  | Submitting
  | Cancel

type NewAthleteForm =
  inherit UserControl

  new:
    props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> NewAthleteForm
