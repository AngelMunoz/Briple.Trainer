module Briple.Trainer.Components.NewAthleteForm

open System
open System.Globalization

open Avalonia.Controls
open NXUI.Extensions

open MVUish.Avalonia

open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Strings

[<Struct>]
type Event =
  | Saved of Athlete
  | Cancelled

[<Struct>]
type Props = { Culture: CultureInfo }

type Model = {
  Name: string
  Gender: Gender voption
  Goals: PlanGoal list
}

type Msg =
  | SetName of string
  | PickGender of Gender
  | ToggleGoal of PlanGoal
  | Save
  | Cancel

let goalOptions = [
  Hypertrophy, Keys.goalHypertrophy
  WeightLoss, Keys.goalWeightLoss
  CardioVascularHealth, Keys.goalCardio
  BasicBodyActivation, Keys.goalActivation
  Strength, Keys.goalStrength
]

let inline isValid(model: Model) =
  not(String.IsNullOrWhiteSpace model.Name)
  && model.Gender.IsSome
  && not(List.isEmpty model.Goals)

let inline profileOf(model: Model) : Athlete = {
  Name = model.Name.Trim()
  Gender = model.Gender |> ValueOption.defaultValue Male
  Goals = model.Goals
}

let init(_: Props) =
  {
    Name = ""
    Gender = ValueNone
    Goals = [ Hypertrophy ]
  },
  Cmd.none

/// The update closes over `onEvent`: the Saved and Cancelled decisions
/// are one-shot outputs, not model transitions.
let update (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | SetName name -> { model with Name = name }, Cmd.none

  | PickGender gender -> { model with Gender = ValueSome gender }, Cmd.none

  | ToggleGoal goal ->
    let goals =
      if List.contains goal model.Goals then
        List.except [ goal ] model.Goals
      else
        model.Goals @ [ goal ]

    { model with Goals = goals }, Cmd.none

  | Save when isValid model ->
    onEvent(Saved(profileOf model))
    model, Cmd.none

  | Save -> model, Cmd.none

  | Cancel ->
    onEvent Cancelled
    model, Cmd.none

type NewAthleteForm(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) as this
  =
  inherit UserControl()

  let t key = get(props.Culture, key)

  let goalName goal =
    goalOptions
    |> List.tryPick(fun (candidate, key) ->
      if candidate = goal then Some(t key) else None)
    |> Option.defaultValue ""

  let goalByName(picked: string) =
    goalOptions
    |> List.tryPick(fun (candidate, key) ->
      if t key = picked then Some candidate else None)

  do

    let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      let saveEnabled = model |> Observable.map isValid

      let nameEntry = entry(t Keys.athleteName, "", (SetName >> dispatch))

      let genderRadios =
        radioRow(
          [ t Keys.genderMale; t Keys.genderFemale ],
          "new-athlete-gender",
          t Keys.genderMale,
          fun picked ->
            dispatch(
              PickGender(if picked = t Keys.genderFemale then Female else Male)
            )
        )

      // a checkbox per goal; the checked state follows the model
      let goalChecks =
        let rows = [|
          for candidate, key in goalOptions ->
            let isOn =
              model |> Observable.map(fun m -> List.contains candidate m.Goals)

            checkRow(t key, isOn, fun () -> dispatch(ToggleGoal candidate))
        |]

        StackPanel().Spacing(6.0).Children rows :> Control

      let saveCommand =
        let save = command(t Keys.athleteSave, (fun () -> dispatch Save))

        save.IsEnabled(saveEnabled)

      fluentPage {
        Title = t Keys.athletesNew
        Commands = [ saveCommand ]
        Body =
          StackPanel()
            .Spacing(4.0)
            .Children(
              field(t Keys.athleteName, (fun () -> nameEntry :> Control)),
              field(t Keys.athleteGender, (fun () -> genderRadios)),
              field(t Keys.athleteGoals, (fun () -> goalChecks)),
              hyperlinkButton(t Keys.athleteCancel, (fun () -> dispatch Cancel))
            )
        Measure = 560.0
      }

    let program = Program.mkProgram init (update onEvent) view

    this.Content <- Program.runWith props program
