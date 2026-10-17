module Briple.Trainer.Components.WizardBrief

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open NXUI.Extensions

open MVUish.Avalonia
open MVUish.Avalonia.Collections

open Briple.Trainer
open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests
open Briple.Trainer.Strings

type Mode =
  | Adjust
  | Fresh

[<Struct>]
type BriefDraft = {
  Weeks: int
  Goal: PlanGoal
  Frequency: VariantFrequency
  Emphasis: Emphasis list
  Mode: Mode
}

[<Struct>]
type Props = {
  Culture: CultureInfo
  Athlete: Athlete
  HasPrevious: bool
  Draft: BriefDraft
  OnChanged: BriefDraft -> unit
}

type Model = BriefDraft

type Msg =
  | SetWeeks of int
  | SetGoal of PlanGoal
  | SetFrequency of VariantFrequency
  | ToggleEmphasis of Emphasis
  | SetMode of Mode

let goalOptions = [
  Hypertrophy, Keys.goalHypertrophy
  WeightLoss, Keys.goalWeightLoss
  CardioVascularHealth, Keys.goalCardio
  BasicBodyActivation, Keys.goalActivation
  Strength, Keys.goalStrength
]

let emphasisOptions = [
  Arms, Keys.emphasisArms
  Shoulders, Keys.emphasisShoulders
  Chest, Keys.emphasisChest
  Back, Keys.emphasisBack
  Legs, Keys.emphasisLegs
  Glutes, Keys.emphasisGlutes
  CoreWork, Keys.emphasisCore
  FullBody, Keys.emphasisFullBody
  Squat, Keys.emphasisSquat
  Deadlift, Keys.emphasisDeadlift
  BenchPress, Keys.emphasisBenchPress
  OverheadPress, Keys.emphasisOverheadPress
  OlympicLifts, Keys.emphasisOlympicLifts
  Push, Keys.emphasisPush
  Pull, Keys.emphasisPull
  FullBodyCircuit, Keys.emphasisFullBodyCircuit
  Hiit, Keys.emphasisHiit
  StrengthCardio, Keys.emphasisStrengthCardio
  ContinuousZone2, Keys.emphasisContinuousZone2
  IntervalHiit, Keys.emphasisIntervalHiit
  Mixed, Keys.emphasisMixed
  Mobility, Keys.emphasisMobility
  Stability, Keys.emphasisStability
  MovementPatterns, Keys.emphasisMovementPatterns
]

let frequencyOptions = [
  DosDias, Keys.freq2
  TresDias, Keys.freq3
  CuatroDias, Keys.freq4
  CincoDias, Keys.freq5
  SeisDias, Keys.freq6
]

/// The default draft of a fresh wizard visit.
let defaultDraft(athlete: Athlete, hasPrevious: bool) : BriefDraft = {
  Weeks = 4
  Goal = athlete.Goals |> List.tryHead |> Option.defaultValue Hypertrophy
  Frequency = TresDias
  Emphasis = []
  Mode = if hasPrevious then Adjust else Fresh
}

let init(props: Props) = props.Draft, Cmd.none

/// Every edit reports the new draft to the page; the page owns it.
let update (props: Props) (msg: Msg) (model: Model) =
  let next =
    match msg with
    | SetWeeks weeks -> { model with Weeks = weeks }

    | SetGoal goal ->
        // the emphasis catalog is per goal: a goal change resets the picks
        {
          model with
              Goal = goal
              Emphasis = []
        }

    | SetFrequency frequency -> { model with Frequency = frequency }

    | ToggleEmphasis emphasis ->
      let picked =
        if List.contains emphasis model.Emphasis then
          List.except [ emphasis ] model.Emphasis
        else
          model.Emphasis @ [ emphasis ]

      { model with Emphasis = picked }

    | SetMode mode -> { model with Mode = mode }

  props.OnChanged next
  next, Cmd.none

let goalName (culture: CultureInfo) (goal: PlanGoal) =
  goalOptions
  |> List.tryPick(fun (candidate, key) ->
    if candidate = goal then Some(get(culture, key)) else None)
  |> Option.defaultValue ""

let emphasisName (culture: CultureInfo) (emphasis: Emphasis) =
  emphasisOptions
  |> List.tryPick(fun (candidate, key) ->
    if candidate = emphasis then
      Some(get(culture, key))
    else
      None)
  |> Option.defaultValue ""

let frequencyName (culture: CultureInfo) (frequency: VariantFrequency) =
  frequencyOptions
  |> List.tryPick(fun (candidate, key) ->
    if candidate = frequency then
      Some(get(culture, key))
    else
      None)
  |> Option.defaultValue ""

/// The brief form at one culture, over the current draft.
let view
  (props: Props)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let current = props.Draft
  let culture = props.Culture
  let t key = get(culture, key)

  // the goal radio is built from the athlete's own goals
  let athleteGoals =
    if List.isEmpty props.Athlete.Goals then
      [ Hypertrophy ]
    else
      props.Athlete.Goals

  let modeRow: Control =
    if props.HasPrevious then
      let modeName mode =
        match mode with
        | Adjust -> t Keys.wizardModeAdjust
        | Fresh -> t Keys.wizardModeNew

      field(
        t Keys.wizardMode,
        fun () ->
          radioRow(
            [ Adjust, modeName Adjust; Fresh, modeName Fresh ],
            "brief-mode",
            current.Mode,
            SetMode >> dispatch
          )
      )
    else
      StackPanel()

  let weeksChips =
    radioRow(
      [ for week in 1..4 -> week, string week ],
      "brief-weeks",
      current.Weeks,
      SetWeeks >> dispatch
    )

  let goalChips =
    radioRow(
      [ for goal in athleteGoals -> goal, goalName culture goal ],
      "brief-goal",
      current.Goal,
      SetGoal >> dispatch
    )

  let frequencyChips =
    radioRow(
      [ for frequency, key in frequencyOptions -> frequency, t key ],
      "brief-frequency",
      current.Frequency,
      SetFrequency >> dispatch
    )

  // the emphasis catalog follows the picked goal: the keyed binding
  // diffs the snapshots, so rows survive a goal change
  let emphasisRow(emphasis: Emphasis) : Control =
    let isOn =
      model
      |> Observable.map(fun current -> List.contains emphasis current.Emphasis)

    checkRow(
      emphasisName culture emphasis,
      isOn,
      fun () -> dispatch(ToggleEmphasis emphasis)
    )

  let emphasisTemplate =
    FuncDataTemplate<Emphasis>((fun emphasis _ -> emphasisRow emphasis), false)

  let emphasisSection =
    let items = model |> Observable.map(fun m -> Emphasis.optionsFor m.Goal)

    // a 24-item catalog cannot be one long column: wrap it in two or
    // three columns inside the hub section
    ItemsControl()
      .ItemTemplate(emphasisTemplate)
      .ItemsPanel(
        FuncTemplate<Panel>(fun () ->
          WrapPanel(Orientation = Orientation.Horizontal))
      )
      .BindItemsSource(items, id)

  StackPanel()
    .Spacing(4.0)
    .Children(
      modeRow,
      field(t Keys.wizardWeeks, (fun () -> weeksChips)),
      field(t Keys.wizardGoal, (fun () -> goalChips)),
      field(t Keys.wizardFrequency, (fun () -> frequencyChips)),
      field(t Keys.wizardEmphasis, (fun () -> emphasisSection))
    )

type WizardBrief(props: Props) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram init (update props) (view props)

    this.Content <- Program.runWith props program
