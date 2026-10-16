module Briple.Trainer.Components.NewAthleteForm

// The registration form as an MVUish component: props in (the culture),
// events out (Saved carries the profile; Cancelled closes). The form is
// valid when it has a name, a gender, at least one variant and at least
// one goal; the save button renders disabled otherwise.

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions

open MVUish.Avalonia

open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Strings
open Briple.Trainer.Theme

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
  VariantDraft: string
  Variants: string list
  Goals: PlanGoal list
  FocusDraft: string
}

type Msg =
  | SetName of string
  | PickGender of Gender
  | SetVariantDraft of string
  | AddVariant
  | RemoveVariant of string
  | ToggleGoal of PlanGoal
  | SetFocus of string
  | Save
  | Cancel

type NewAthleteForm =
  inherit UserControl

  new:
    props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> NewAthleteForm
