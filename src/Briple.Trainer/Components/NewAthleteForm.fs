module Briple.Trainer.Components.NewAthleteForm

open System

open Avalonia.Controls
open NXUI.Extensions

open MVUish.Avalonia

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Navigation
open Briple.Trainer.Strings

[<Struct>]
type Event =
  | Saved of Athlete
  | Cancelled

type FormPhase =
  | Filling
  | Spent

[<Struct>]
type Props = {
  Culture: PageCulture
  Lead: Control voption
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

let goalOptions = [
  Hypertrophy, Keys.goalHypertrophy
  WeightLoss, Keys.goalWeightLoss
  CardioVascularHealth, Keys.goalCardio
  BasicBodyActivation, Keys.goalActivation
  Strength, Keys.goalStrength
]

let inline isValid(model: Model) =
  not(String.IsNullOrWhiteSpace model.Name) && not(List.isEmpty model.Goals)

/// Leaving loses typed work only when something differs from the
/// defaults. The gender and the first goal are defaults, not input.
let inline hasInput(model: Model) =
  not(String.IsNullOrWhiteSpace model.Name)
  || model.Gender <> Male
  || model.Goals <> [ Hypertrophy ]

let inline profileOf(model: Model) : Athlete = {
  Name = model.Name.Trim()
  Gender = model.Gender
  Goals = model.Goals
}

/// The one phase rule of the page: an in-flight save is not lost work.
let inline phaseOf(model: Model) : PagePhase =
  match model.Phase with
  | Spent -> Ready
  | Filling when hasInput model -> UnsavedChanges
  | Filling -> Ready

let init(_: Props) =
  {
    Name = ""
    Gender = Male
    Goals = [ Hypertrophy ]
    Phase = Filling
  },
  Cmd.none

/// The update closes over `onEvent`: Saved and Cancelled are one-shot
/// outputs. Saved leaves through a message so the model reaches Spent
/// before the parent navigates.
let update (props: Props) (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | SetName name -> { model with Name = name }, Cmd.none

  | PickGender gender -> { model with Gender = gender }, Cmd.none

  | ToggleGoal goal ->
    let goals =
      if List.contains goal model.Goals then
        List.except [ goal ] model.Goals
      else
        model.Goals @ [ goal ]

    { model with Goals = goals }, Cmd.none

  // the event leaves once: a second click must not insert the athlete
  | Save when isValid model && model.Phase = Filling ->
    { model with Phase = Spent }, Cmd.ofMsg Submitting

  | Save -> model, Cmd.none

  | Submitting ->
    onEvent(Saved(profileOf model))
    model, Cmd.none

  | Cancel ->
    onEvent Cancelled
    model, Cmd.none

/// The page: static text comes from resources; the model owns the name,
/// the gender and the goals.
let view
  (props: Props)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let initial, _ = init props

  let saveEnabled =
    model |> Observable.map(fun m -> isValid m && m.Phase = Filling)

  let nameEntry =
    TextBox()
      .Padding(8.0, 6.0)
      .Text(model |> Observable.map _.Name)
      .PlaceholderText(Localization.text Keys.athleteName)
      .OnTextChangedHandler(fun args _ ->
        args.Text
        |> Option.ofObj
        |> Option.defaultValue ""
        |> SetName
        |> dispatch)

  let genderRadios =
    radioRowKeys(
      [ Male, Keys.genderMale; Female, Keys.genderFemale ],
      "new-athlete-gender",
      initial.Gender,
      PickGender >> dispatch
    )

  let goalChecks =
    let rows: Control array = [|
      for candidate, key in goalOptions do
        let isOn =
          model |> Observable.map(fun m -> List.contains candidate m.Goals)

        checkRowKey(key, isOn, (fun () -> dispatch(ToggleGoal candidate)))
    |]

    StackPanel().Spacing(6.0).Children rows

  let saveCommand =
    let save =
      iconCommandKey(
        Keys.athleteSave,
        Icons.contentSave,
        (fun () -> dispatch Save)
      )

    save.IsEnabled(saveEnabled)

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.athletesNew
    Commands = [ saveCommand ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(4.0)
        .Children(
          fieldKey(Keys.athleteName, (fun () -> nameEntry)),
          fieldKey(Keys.athleteGender, (fun () -> genderRadios)),
          fieldKey(Keys.athleteGoals, (fun () -> goalChecks)),
          hyperlinkButtonKey(Keys.athleteCancel, (fun () -> dispatch Cancel))
        )
    Measure = 560.0
  }

type NewAthleteForm(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) as this
  =
  inherit UserControl()

  do
    let render (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      Page.trackPhase(this, props.Gate, model |> Observable.map phaseOf)

      view props model dispatch

    let program = Program.mkProgram init (update props onEvent) render

    this.Content <- Program.runWith props program
