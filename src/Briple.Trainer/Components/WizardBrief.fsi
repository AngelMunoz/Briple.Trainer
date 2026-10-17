module Briple.Trainer.Components.WizardBrief

// Step 2 of the wizard as an MVUish component: props in (the culture and
// the athlete), events out (Submitted carries the period and the brief).
// Weeks, goal, frequency and emphasis are all closed selections with
// standard controls; nothing is typed by hand and nothing derived (RIR,
// counts) is asked.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests

/// What the brief builds on when a previous plan exists: chain on it or
/// start clean. The choice rides the Submitted event.
type Mode =
  | Adjust
  | Fresh

/// What the form reports to its parent.
type Event =
  | Submitted of weeks: int * brief: Brief * mode: Mode
  | Cancelled

/// The form inputs.
[<Struct>]
type Props = {
  Culture: CultureInfo
  Athlete: Athlete
  /// True when there is a previous plan this generation could adjust.
  HasPrevious: bool
}

type Model = {
  Weeks: int
  Goal: PlanGoal
  Frequency: VariantFrequency
  Emphasis: Emphasis list
  Mode: Mode
}

type Msg =
  | SetWeeks of int
  | SetGoal of PlanGoal
  | SetFrequency of VariantFrequency
  | ToggleEmphasis of Emphasis
  | SetMode of Mode
  | Submit
  | Cancel

/// The brief form.
type WizardBrief =
  inherit UserControl

  new: props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> WizardBrief
