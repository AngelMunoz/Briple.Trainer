module Briple.Trainer.Components.NewAthleteForm

// The registration form as an MVUish component: props in (the culture),
// events out (Saved carries the profile; Cancelled closes). The form
// asks for the name, the gender and at least one goal; the training
// frequency is a decision of each plan, not of the profile. Standard
// controls only: radios for the gender, checkboxes for the goals.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types

/// What the form reports to its parent.
[<Struct>]
type Event =
  | Saved of Athlete
  | Cancelled

/// The form inputs.
[<Struct>]
type Props = { Culture: CultureInfo }

type Model = {
  Name: string
  Gender: Gender voption
  Goals: PlanGoal list
  /// True once the profile left as an event; a spent form saves nothing.
  Saved: bool
}

type Msg =
  | SetName of string
  | PickGender of Gender
  | ToggleGoal of PlanGoal
  | Save
  | Cancel

type NewAthleteForm =
  inherit UserControl

  new:
    props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> NewAthleteForm
