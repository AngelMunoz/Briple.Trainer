module Briple.Trainer.Components.AthleteList

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open MVUish.Avalonia
open MVUish.Avalonia.Collections

open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type AthleteListProps = {
  Culture: CultureInfo
  /// The live athletes list, straight from the store.
  Athletes: IObservable<AthleteEntity list>
}

type Model = { Athletes: AthleteEntity list }

type Msg = Refreshed of AthleteEntity list

/// What the page reports to its host.
[<Struct>]
type Event =
  | NewRequested
  | OpenRequested of Guid

let goalName(culture: CultureInfo, goal: PlanGoal) : string =
  match goal with
  | Hypertrophy -> get(culture, Keys.goalHypertrophy)
  | WeightLoss -> get(culture, Keys.goalWeightLoss)
  | CardioVascularHealth -> get(culture, Keys.goalCardio)
  | BasicBodyActivation -> get(culture, Keys.goalActivation)
  | Strength -> get(culture, Keys.goalStrength)

/// One row: the name and the profile summary; the whole row opens.
let athleteRow
  (props: AthleteListProps, onOpen: Guid -> unit, athlete: AthleteEntity)
  : Control =
  let summary = [
    athlete.Profile.Gender |> Gender.toFileToken
    athlete.Profile.Goals
    |> List.map(fun goal -> goalName(props.Culture, goal))
    |> String.concat ", "
  ]

  button(athlete.Profile.Name, (fun () -> onOpen athlete.Id))
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Margin(0.0, 0.0, 0.0, 8.0)
    .Content(
      StackPanel()
        .Spacing(2.0)
        .Children(
          TextBlock()
            .Text(athlete.Profile.Name)
            .FontSize(16.0)
            .FontWeight(FontWeight.Bold)
            .FontFamily(font),
          TextBlock()
            .Text(String.concat " · " summary)
            .FontFamily(font)
            .Foreground(muted)
        )
    )
  :> Control

let view
  (props: AthleteListProps)
  (onEvent: Event -> unit)
  (model: IObservable<Model>)
  (_: Dispatch<Msg>)
  : Control =
  let t key = get(props.Culture, key)

  let onOpen id = onEvent(Event.OpenRequested id)

  // the keyed binding diffs the store emissions: rows survive, only the
  // real deltas apply
  let rows =
    let template =
      FuncDataTemplate<AthleteEntity>(
        (fun athlete _ -> athleteRow(props, onOpen, athlete)),
        false
      )

    let list = ItemsControl().ItemTemplate(template)

    list.BindItemsSource(
      model |> Observable.map(fun m -> m.Athletes),
      fun athlete -> athlete.Id
    )
    |> ignore

    list

  let emptyState =
    label(t Keys.athletesEmpty)
      .IsVisible(model |> Observable.map(fun m -> List.isEmpty m.Athletes))

  let newButton =
    primaryButton(t Keys.athletesNew, (fun () -> onEvent Event.NewRequested))
      .HorizontalAlignment(HorizontalAlignment.Left)

  page(
    t Keys.athletesTitle,
    StackPanel().Spacing(8.0).Children(newButton :> Control, emptyState, rows)
  )

type AthleteList
  (props: AthleteListProps, [<InlineIfLambda>] onEvent: Event -> unit) as this =
  inherit UserControl()

  let init(_: AthleteListProps) = { Athletes = [] }, Cmd.none

  let update (msg: Msg) (model: Model) =
    match msg with
    | Refreshed athletes -> { Athletes = athletes }, Cmd.none

  // the store is the only input: every emission lands as a refresh
  let subscribe(_: Model) : Sub<Msg> =
    fun dispatch ->
      props.Athletes |> Observable.subscribe(Refreshed >> dispatch)

  do
    let program =
      Program.mkProgram init update (view props onEvent)
      |> Program.withSubscription subscribe

    this.Content <- Program.runWith props program
