module Briple.Trainer.Components.WizardBrief

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions

open MVUish.Avalonia

open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests
open Briple.Trainer.Strings

type Event =
  | Submitted of weeks: int * brief: Brief * counts: (string * int) list
  | Cancelled

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

let goalOptions = [
  Hypertrophy, Keys.goalHypertrophy
  WeightLoss, Keys.goalWeightLoss
  CardioVascularHealth, Keys.goalCardio
  BasicBodyActivation, Keys.goalActivation
  Strength, Keys.goalStrength
]

let inline previousCount props variant =
  props.PreviousCounts
  |> List.tryPick(fun (candidate, count) ->
    if candidate = variant then Some count else None)

let rirPerWeek(draft: string) : (int * string) list option =
  if System.String.IsNullOrWhiteSpace draft then
    Some []
  else
    let pairs =
      draft.Split(',') |> Array.toList |> List.map(fun part -> part.Split(':'))

    let parsed =
      pairs
      |> List.map(fun pair ->
        match pair with
        | [| week; target |] ->
          Int32.TryParse week
          |> function
            | true, weekNumber when weekNumber > 0 ->
              Some(weekNumber, target.Trim())
            | _ -> None
        | _ -> None)

    if List.contains None parsed then
      None
    else
      parsed |> List.choose id |> Some

/// The brief built from the model, when everything parses.
let tryBrief
  (props: Props)
  (model: Model)
  : (Brief * (string * int) list) option =
  let selected =
    props.Athlete.Variants
    |> List.filter(fun variant -> List.contains variant model.Variants)

  let counts =
    selected
    |> List.choose(fun variant ->
      model.Counts
      |> Map.tryFind variant
      |> Option.filter(fun count -> count > 0)
      |> Option.map(fun count -> variant, count))

  let complete =
    not(List.isEmpty selected) && List.length counts = List.length selected

  rirPerWeek model.RirDraft
  |> Option.bind(fun rir ->
    if complete then
      Some(
        {
          Goal = model.Goal
          Adjustments = []
          Focus =
            model.FocusDraft.Split(',')
            |> Array.toList
            |> List.map(fun part -> part.Trim())
            |> List.filter(fun part ->
              not(System.String.IsNullOrWhiteSpace part))
          RirPerWeek = rir
          Variants = selected
        },
        counts
      )
    else
      None)

let init(props: Props) =
  let counts =
    props.Athlete.Variants
    |> List.map(fun variant ->
      variant, previousCount props variant |> Option.defaultValue 0)
    |> Map.ofList

  {
    Weeks = 4
    Goal =
      props.Athlete.Goals |> List.tryHead |> Option.defaultValue Hypertrophy
    Variants = props.Athlete.Variants
    FocusDraft = String.concat ", " props.Athlete.Focus
    RirDraft = ""
    Counts = counts
  },
  Cmd.none

/// The update closes over `onEvent`: the submit and cancel decisions are
/// one-shot outputs, not model transitions.
let update (props: Props) (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | SetWeeks weeks -> { model with Weeks = weeks }, Cmd.none
  | SetGoal goal -> { model with Goal = goal }, Cmd.none

  | ToggleVariant variant ->
    let variants =
      if List.contains variant model.Variants then
        List.except [ variant ] model.Variants
      else
        model.Variants @ [ variant ]

    { model with Variants = variants }, Cmd.none

  | SetFocus focus -> { model with FocusDraft = focus }, Cmd.none
  | SetRir draft -> { model with RirDraft = draft }, Cmd.none

  | SetCount(variant, count) ->
    let parsed =
      Int32.TryParse count
      |> function
        | true, number when number >= 0 -> Some number
        | _ -> None

    let counts =
      match parsed with
      | Some number -> Map.add variant number model.Counts
      | None -> Map.remove variant model.Counts

    { model with Counts = counts }, Cmd.none

  | Submit when tryBrief props model |> Option.isSome ->
    let brief, counts = tryBrief props model |> Option.get

    onEvent(Event.Submitted(model.Weeks, brief, counts))
    model, Cmd.none

  | Submit -> model, Cmd.none
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

  do

    let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      let weeks = model |> Observable.map(fun m -> string m.Weeks)

      let goalText = model |> Observable.map(fun m -> goalName m.Goal)

      let variantSelection = model |> Observable.map(fun m -> m.Variants)

      let submitEnabled =
        model |> Observable.map(fun m -> tryBrief props m |> Option.isSome)

      let countEntry(variant: string) =
        let countInput =
          entry(
            "0",
            (previousCount props variant |> Option.defaultValue 0 |> string),
            (fun text -> dispatch(SetCount(variant, text)))
          )

        field(variant, (fun () -> countInput :> Control))

      let weeksChips =
        reactiveChoiceChips(
          [ "1"; "2"; "3"; "4" ],
          weeks,
          (fun picked -> dispatch(SetWeeks(Int32.Parse picked)))
        )

      let goalChips =
        reactiveChoiceChips(
          (goalOptions |> List.map(fun (_, key) -> t key)),
          goalText,
          (fun picked -> goalByName picked |> Option.iter(SetGoal >> dispatch))
        )

      let variantChips =
        reactiveMultiChips(
          props.Athlete.Variants,
          variantSelection,
          (ToggleVariant >> dispatch)
        )

      let countsSection =
        StackPanel()
          .Spacing(4.0)
          .Children(
            [| for variant in props.Athlete.Variants -> countEntry variant |]
          )

      let focusEntry = entry("", "", SetFocus >> dispatch)

      let rirEntry = entry("", "", SetRir >> dispatch)

      let generateButton =
        let primary =
          primaryButton(t Keys.wizardGenerate, (fun () -> dispatch Submit))

        primary.IsEnabled(submitEnabled)

      let cancelButton =
        button(t Keys.athleteCancel, (fun () -> dispatch Cancel))

      StackPanel()
        .Spacing(4.0)
        .Children(
          field(t Keys.wizardWeeks, (fun () -> weeksChips)),
          field(t Keys.wizardGoal, (fun () -> goalChips)),
          field(t Keys.athleteVariants, (fun () -> variantChips)),
          field(t Keys.wizardCounts, (fun () -> countsSection :> Control)),
          field(t Keys.wizardFocus, (fun () -> focusEntry :> Control)),
          field(t Keys.wizardRir, (fun () -> rirEntry :> Control)),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(generateButton, cancelButton)
        )
      :> Control

    let program = Program.mkProgram init (update props onEvent) view

    this.Content <- Program.runWith props program
