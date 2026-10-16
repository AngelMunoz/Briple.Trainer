module Briple.Trainer.Components.NewAthleteForm

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

[<Struct>]
type Event =
  | Saved of Athlete
  | Cancelled

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

let goalOptions = [
  Hypertrophy, Keys.goalHypertrophy
  WeightLoss, Keys.goalWeightLoss
  CardioVascularHealth, Keys.goalCardio
  BasicBodyActivation, Keys.goalActivation
  Strength, Keys.goalStrength
]

let inline isValid(model: Model) =
  not(System.String.IsNullOrWhiteSpace model.Name)
  && model.Gender.IsSome
  && not(List.isEmpty model.Variants)
  && not(List.isEmpty model.Goals)

let inline profileOf(model: Model) : Athlete = {
  Name = model.Name.Trim()
  Gender = model.Gender |> ValueOption.defaultValue Male
  Variants = model.Variants
  Goals = model.Goals
  Focus =
    model.FocusDraft.Split(',')
    |> Array.toList
    |> List.map(fun part -> part.Trim())
    |> List.filter(fun part -> not(System.String.IsNullOrWhiteSpace part))
}

let init(_: Props) =
  {
    Name = ""
    Gender = ValueNone
    VariantDraft = ""
    Variants = []
    Goals = [ Hypertrophy ]
    FocusDraft = ""
  },
  Cmd.none

/// The update closes over `onEvent`: the Saved and Cancelled decisions are
/// one-shot outputs, not model transitions.
let update (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | SetName name -> { model with Name = name }, Cmd.none
  | PickGender gender -> { model with Gender = ValueSome gender }, Cmd.none
  | SetVariantDraft draft -> { model with VariantDraft = draft }, Cmd.none

  | AddVariant when not(System.String.IsNullOrWhiteSpace model.VariantDraft) ->
    let variant = model.VariantDraft.Trim().ToLowerInvariant()

    if List.contains variant model.Variants then
      { model with VariantDraft = "" }, Cmd.none
    else
      {
        model with
            Variants = model.Variants @ [ variant ]
            VariantDraft = ""
      },
      Cmd.none

  | AddVariant -> model, Cmd.none

  | RemoveVariant variant ->
    {
      model with
          Variants = List.except [ variant ] model.Variants
    },
    Cmd.none

  | ToggleGoal goal ->
    let goals =
      if List.contains goal model.Goals then
        List.except [ goal ] model.Goals
      else
        model.Goals @ [ goal ]

    { model with Goals = goals }, Cmd.none

  | SetFocus focus -> { model with FocusDraft = focus }, Cmd.none

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

  let genderName gender =
    match gender with
    | Male -> t Keys.genderMale
    | Female -> t Keys.genderFemale

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
      let genderOptions = [ t Keys.genderMale; t Keys.genderFemale ]

      let genderText =
        model
        |> Observable.map(fun m ->
          match m.Gender with
          | ValueSome gender -> genderName gender
          | ValueNone -> "")

      let variantsText =
        model |> Observable.map(fun m -> String.concat ", " m.Variants)

      let goalNames =
        model |> Observable.map(fun m -> m.Goals |> List.map goalName)

      let saveEnabled = model |> Observable.map isValid

      let nameEntry = entry(t Keys.athleteName, "", (SetName >> dispatch))

      let variantDraft =
        entry("", "", SetVariantDraft >> dispatch)
        |> fun draft -> draft.Width(160.0)

      let focusEntry = entry("", "", SetFocus >> dispatch)

      let saveButton =
        let primary =
          primaryButton(t Keys.athleteSave, (fun () -> dispatch Save))

        primary.IsEnabled(saveEnabled)

      let cancelButton =
        button(t Keys.athleteCancel, (fun () -> dispatch Cancel))

      StackPanel()
        .Spacing(4.0)
        .Children(
          field(t Keys.athleteName, (fun () -> nameEntry :> Control)),
          field(
            t Keys.athleteGender,
            (fun () ->
              reactiveChoiceChips(
                genderOptions,
                genderText,
                (fun picked ->
                  dispatch(
                    PickGender(
                      if picked = t Keys.genderFemale then Female else Male
                    )
                  ))
              ))
          ),
          field(
            t Keys.athleteVariants,
            (fun () ->
              StackPanel()
                .Spacing(6.0)
                .Children(
                  StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(6.0)
                    .Children(
                      variantDraft,
                      button("+", (fun () -> dispatch AddVariant))
                    ),
                  TextBlock().Text(variantsText).FontFamily(font)
                )
              :> Control)
          ),
          field(
            t Keys.athleteGoals,
            (fun () ->
              reactiveMultiChips(
                goalOptions |> List.map(fun (_, key) -> t key),
                goalNames,
                (fun picked ->
                  goalByName picked |> Option.iter(ToggleGoal >> dispatch))
              ))
          ),
          field(t Keys.athleteFocus, (fun () -> focusEntry :> Control)),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(saveButton, cancelButton)
        )
      :> Control

    let program = Program.mkProgram init (update onEvent) view

    this.Content <- Program.runWith props program
