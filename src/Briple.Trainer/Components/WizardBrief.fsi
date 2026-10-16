module Briple.Trainer.Components.WizardBrief

// Step 4 of the wizard as an MVUish component: props in (the culture, the
// athlete and the previous plan's counts), events out (Submitted carries
// the period, the brief and the target counts; Cancelled closes). The
// submit button renders disabled until the form parses as a whole.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests

/// What the form reports to its parent.
type Event =
  | Submitted of weeks: int * brief: Brief * counts: (string * int) list
  | Cancelled

/// The form inputs.
[<Struct>]
type Props = {
  Culture: CultureInfo
  Athlete: Athlete
  PreviousCounts: (string * int) list
}

type Model = {
  Weeks: int
  Goal: PlanGoal
  Variants: string list
  FocusDraft: string
  RirDraft: string
  Counts: Map<string, int>
}

type Msg =
  | SetWeeks of int
  | SetGoal of PlanGoal
  | ToggleVariant of string
  | SetFocus of string
  | SetRir of string
  | SetCount of variant: string * count: string
  | Submit
  | Cancel

/// The brief form.
type WizardBrief =
  inherit UserControl

  new: props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> WizardBrief
