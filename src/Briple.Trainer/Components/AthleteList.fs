module Briple.Trainer.Components.AthleteList

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type AthleteListEnv = {
  Observe: unit -> IObservable<AthleteEntity list>
  Snapshot: unit -> AthleteEntity list
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

[<Struct>]
type AthleteListProps = {
  Lead: (unit -> Control) voption
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

[<Struct>]
type Event =
  | NewRequested
  | OpenRequested of Guid

type Model = {
  Athletes: AthleteEntity list
  Culture: CultureInfo
}

type Msg =
  | AthletesChanged of AthleteEntity list
  | CultureChanged of CultureInfo
  | NewRequested
  | OpenRequested of Guid

[<Struct>]
type Context = {
  Env: AthleteListEnv
  Props: AthleteListProps
  OnEvent: Event -> unit
}

let init(ctx: Context) =
  let model = {
    Athletes = ctx.Env.Snapshot()
    Culture = ctx.Env.Culture
  }

  model, Cmd.none

let update (ctx: Context) (msg: Msg) (model: Model) =
  match msg with
  | AthletesChanged athletes -> { model with Athletes = athletes }, Cmd.none
  | CultureChanged culture -> { model with Culture = culture }, Cmd.none
  | NewRequested ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent Event.NewRequested)
  | OpenRequested id ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent(Event.OpenRequested id))

let subscribe (ctx: Context) (_: Model) : Sub<Msg> =
  Sub.batch [
    Sub.fromObservable
      "athletes"
      (ctx.Env.Observe() |> Observable.map AthletesChanged)
    Sub.fromObservable
      "culture"
      (ctx.Env.CultureChanged |> Observable.map CultureChanged)
  ]

let goalName(culture: CultureInfo, goal: PlanGoal) : string =
  match goal with
  | Hypertrophy -> get(culture, Keys.goalHypertrophy)
  | WeightLoss -> get(culture, Keys.goalWeightLoss)
  | CardioVascularHealth -> get(culture, Keys.goalCardio)
  | BasicBodyActivation -> get(culture, Keys.goalActivation)
  | Strength -> get(culture, Keys.goalStrength)

let genderName(culture: CultureInfo, gender: Gender) : string =
  match gender with
  | Male -> get(culture, Keys.genderMale)
  | Female -> get(culture, Keys.genderFemale)

let summary(culture: CultureInfo, athlete: AthleteEntity) : string =
  [
    genderName(culture, athlete.Profile.Gender)
    athlete.Profile.Goals
    |> List.map(fun goal -> goalName(culture, goal))
    |> String.concat ", "
  ]
  |> String.concat " · "

/// The trailing glyph of a row: 16px, muted, centered by the row grid.
let inline chevron() : Control =
  PathIcon(Data = Icons.chevronRight, Width = 16.0, Height = 16.0)
    .Foreground(muted)

/// One row: the name and the profile summary; the whole row opens. The
/// summary is composed text, so it follows the model's culture; the name
/// is data and never changes.
let athleteRow
  (
    culture: IObservable<CultureInfo>,
    onOpen: Guid -> unit,
    athlete: AthleteEntity
  ) : Control =
  let detail =
    culture |> Observable.map(fun current -> summary(current, athlete))

  Button()
    .Content(contentRowObs(athlete.Profile.Name, detail, chevron))
    .OnClickHandler(fun _ _ -> onOpen athlete.Id)
    .Margin(0.0, 0.0, 0.0, 4.0)
    .Classes("row")

/// The page: static text comes from the resource dictionary; the rows
/// follow the model's athlete list and culture.
let view
  (props: AthleteListProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let culture = model |> Observable.map _.Culture
  let onOpen id = dispatch(OpenRequested id)

  let rows =
    let template =
      FuncDataTemplate<AthleteEntity>(
        (fun athlete _ -> athleteRow(culture, onOpen, athlete)),
        false
      )

    ItemsControl().ItemTemplate(template)
    |> stretchItems
    |> fun list ->
        list.BindItemsSource(
          model |> Observable.map _.Athletes,
          fun athlete -> athlete.Id
        )

  let emptyState =
    StackPanel()
      .Spacing(8.0)
      .Children(
        glyph(Icons.accountSearchOutline, 32.0, muted),
        labelKey Keys.athletesEmpty
      )
      .IsVisible(
        model |> Observable.map(fun current -> List.isEmpty current.Athletes)
      )

  let newCommand =
    iconPrimaryButtonKey(
      Keys.athletesNew,
      Icons.plus,
      (fun () -> dispatch NewRequested)
    )

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.athletesTitle
    Commands = [ newCommand ]
    Footer = noFooter
    Body = StackPanel().Spacing(8.0).Children(emptyState, rows)
  }

type AthleteList
  (
    env: AthleteListEnv,
    props: AthleteListProps,
    [<InlineIfLambda>] onEvent: Event -> unit
  ) as this =
  inherit UserControl()

  do
    let ctx = {
      Env = env
      Props = props
      OnEvent = onEvent
    }

    let program =
      Program.mkProgram
        (fun (_: AthleteListProps) -> init ctx)
        (update ctx)
        (view props)
      |> Program.withSubscription(subscribe ctx)

    let root, lifetime = Program.runWithLifecycle props program

    this.Content <- root
    props.Lifetime.AddDisposable lifetime
