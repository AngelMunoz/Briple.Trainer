module Briple.Trainer.Components.WizardBrief

// Step 2 of the wizard as a sealed component: props in (the culture, the
// athlete and the draft the page owns), changes out through OnChanged.
// Weeks, goal, frequency and emphasis are closed selections with
// standard controls; nothing is typed and nothing derived (RIR, counts)
// is asked. The page owns Generar; the component only edits the draft.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests

/// What the brief builds on when a previous plan exists.
type Mode =
  | Adjust
  | Fresh

/// The brief draft, owned by the wizard page.
[<Struct>]
type BriefDraft = {
  Weeks: int
  Goal: PlanGoal
  Frequency: VariantFrequency
  Emphasis: Emphasis list
  /// The language of the plan content.
  Language: ContentLanguage
  Mode: Mode
}

/// The default draft of a fresh wizard visit.
val defaultDraft:
  athlete: Athlete * hasPrevious: bool * language: ContentLanguage -> BriefDraft

/// The form inputs.
[<Struct>]
type Props = {
  Athlete: Athlete
  /// True when there is a previous plan this generation could adjust.
  HasPrevious: bool
  /// The initial draft of this visit.
  Draft: BriefDraft
  /// Every edit travels to the page, which keeps the draft.
  OnChanged: BriefDraft -> unit
}

/// The brief form.
type WizardBrief =
  inherit UserControl

  new: props: Props -> WizardBrief
