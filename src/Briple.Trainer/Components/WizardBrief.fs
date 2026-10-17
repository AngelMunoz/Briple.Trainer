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

/// What the brief builds on when a previous plan exists: chain on it or
/// start clean. The choice rides the Submitted event.
type Mode =
  | Adjust
  | Fresh

type Event =
  | Submitted of weeks: int * brief: Brief * mode: Mode
  | Cancelled

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

let init(props: Props) =
  {
    Weeks = 4
    Goal =
      props.Athlete.Goals |> List.tryHead |> Option.defaultValue Hypertrophy
    Frequency = TresDias
    Emphasis = []
    Mode = Adjust
  },
  Cmd.none

/// The update closes over `onEvent`: the submit and cancel decisions are
/// one-shot outputs, not model transitions.
let update (props: Props) (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | SetWeeks weeks -> { model with Weeks = weeks }, Cmd.none

  | SetGoal goal ->
    // the emphasis catalog is per goal: a goal change resets the picks
    {
      model with
          Goal = goal
          Emphasis = []
    },
    Cmd.none

  | SetFrequency frequency -> { model with Frequency = frequency }, Cmd.none

  | ToggleEmphasis emphasis ->
    let picked =
      if List.contains emphasis model.Emphasis then
        List.except [ emphasis ] model.Emphasis
      else
        model.Emphasis @ [ emphasis ]

    { model with Emphasis = picked }, Cmd.none

  | SetMode mode -> { model with Mode = mode }, Cmd.none

  | Submit ->
    onEvent(
      Event.Submitted(
        model.Weeks,
        {
          Goal = model.Goal
          Frequency = model.Frequency
          Emphasis = model.Emphasis
          Adjustments = []
        },
        model.Mode
      )
    )

    model, Cmd.none

  | Cancel ->
    onEvent Event.Cancelled
    model, Cmd.none

type WizardBrief(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) as this
  =
  inherit UserControl()
  let t key = get(props.Culture, key)

  let goalName goal =
    goalOptions
    |> List.tryPick(fun (candidate, key) ->
      if candidate = goal then Some(t key) else None)
    |> Option.defaultValue ""

  let goalByName picked =
    goalOptions
    |> List.tryPick(fun (candidate, key) ->
      if t key = picked then Some candidate else None)

  let emphasisName emphasis =
    emphasisOptions
    |> List.tryPick(fun (candidate, key) ->
      if candidate = emphasis then Some(t key) else None)
    |> Option.defaultValue ""

  let emphasisByName picked =
    emphasisOptions
    |> List.tryPick(fun (candidate, key) ->
      if t key = picked then Some candidate else None)

  let frequencyName frequency =
    frequencyOptions
    |> List.tryPick(fun (candidate, key) ->
      if candidate = frequency then Some(t key) else None)
    |> Option.defaultValue ""

  let frequencyByName picked =
    frequencyOptions
    |> List.tryPick(fun (candidate, key) ->
      if t key = picked then Some candidate else None)

  do

    let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      // the goal radio is built from the athlete's own goals; the same
      // default the init model carries
      let athleteGoals =
        if List.isEmpty props.Athlete.Goals then
          [ Hypertrophy ]
        else
          props.Athlete.Goals

      let initialGoal =
        athleteGoals |> List.tryHead |> Option.defaultValue Hypertrophy

      // the starting point, only when there is a previous plan: silent
      // adjustment was a surprise the user did not pick
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
                [ modeName Adjust; modeName Fresh ],
                "brief-mode",
                modeName Adjust,
                fun picked ->
                  dispatch(
                    SetMode(if picked = modeName Fresh then Fresh else Adjust)
                  )
              )
          )
        else
          StackPanel() :> Control

      let weeksChips =
        radioRow(
          [ for week in 1..4 -> $"{week}" ],
          "brief-weeks",
          "4",
          fun picked -> dispatch(SetWeeks(Int32.Parse picked))
        )

      let goalChips =
        radioRow(
          athleteGoals |> List.map goalName,
          "brief-goal",
          goalName initialGoal,
          fun picked -> goalByName picked |> Option.iter(SetGoal >> dispatch)
        )

      let frequencyChips =
        radioRow(
          frequencyOptions |> List.map fst |> List.map frequencyName,
          "brief-frequency",
          frequencyName TresDias,
          fun picked ->
            frequencyByName picked |> Option.iter(SetFrequency >> dispatch)
        )

      // the emphasis catalog follows the picked goal: the keyed binding
      // diffs the snapshots, so rows survive a goal change and only the
      // catalog's own deltas apply
      let emphasisRow(emphasis: Emphasis) : Control =
        let isOn =
          model
          |> Observable.map(fun current ->
            List.contains emphasis current.Emphasis)

        checkRow(
          emphasisName emphasis,
          isOn,
          fun () -> dispatch(ToggleEmphasis emphasis)
        )

      let emphasisTemplate =
        FuncDataTemplate<Emphasis>(
          (fun emphasis _ -> emphasisRow emphasis),
          false
        )

      let emphasisSection =
        let items = model |> Observable.map(fun m -> Emphasis.optionsFor m.Goal)

        let list = ItemsControl().ItemTemplate(emphasisTemplate)

        list.BindItemsSource(items, id) |> ignore
        list

      let generateButton =
        iconPrimaryButton(
          t Keys.wizardGenerate,
          Icons.play,
          (fun () -> dispatch Submit)
        )

      let cancelButton =
        iconButton(
          t Keys.athleteCancel,
          Icons.arrowLeft,
          (fun () -> dispatch Cancel)
        )

      StackPanel()
        .Spacing(4.0)
        .Children(
          modeRow,
          field(t Keys.wizardWeeks, (fun () -> weeksChips)),
          field(t Keys.wizardGoal, (fun () -> goalChips)),
          field(t Keys.wizardFrequency, (fun () -> frequencyChips)),
          field(t Keys.wizardEmphasis, (fun () -> emphasisSection)),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(generateButton, cancelButton)
        )
      :> Control

    let program = Program.mkProgram init (update props onEvent) view

    this.Content <- Program.runWith props program
