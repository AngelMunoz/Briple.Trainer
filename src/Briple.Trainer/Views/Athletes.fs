module Briple.Trainer.Views.Athletes

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Controls.Templates
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
open Briple.Trainer.Stores
open Briple.Trainer.Theme

[<Struct>]
type AthleteDetailEnv = {
  Plans: PlanStore
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

[<Struct>]
type DetailPageProps = {
  Lead: (unit -> Control) voption
  Athlete: AthleteEntity
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

[<Struct>]
type Event =
  | GenerateRequested
  | ReviewRequested of planId: Guid

type Model = {
  Plans: PlanEntity list
  Culture: CultureInfo
}

type Msg =
  | PlansChanged of PlanEntity list
  | CultureChanged of CultureInfo
  | GenerateRequested
  | ReviewRequested of Guid

[<Struct>]
type Context = {
  Env: AthleteDetailEnv
  Props: DetailPageProps
  OnEvent: Event -> unit
}

let init(ctx: Context) =
  let model = {
    Plans = ctx.Env.Plans.Snapshot(ctx.Props.Athlete.Id)
    Culture = ctx.Env.Culture
  }

  model, Cmd.none

let update (ctx: Context) (msg: Msg) (model: Model) =
  match msg with
  | PlansChanged plans -> { model with Plans = plans }, Cmd.none
  | CultureChanged culture -> { model with Culture = culture }, Cmd.none
  | GenerateRequested ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent Event.GenerateRequested)
  | ReviewRequested id ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent(Event.ReviewRequested id))

let subscribe (ctx: Context) (_: Model) : Sub<Msg> =
  Sub.batch [
    Sub.fromObservable
      "plans"
      (ctx.Env.Plans.Observe(ctx.Props.Athlete.Id)
       |> Observable.map PlansChanged)
    Sub.fromObservable
      "culture"
      (ctx.Env.CultureChanged |> Observable.map CultureChanged)
  ]

/// The trailing glyph of a row.
let inline chevron() : Control =
  PathIcon(Data = Icons.chevronRight, Width = 16.0, Height = 16.0)
    .Foreground(muted)

/// One plan row: the title and its metadata; the whole row opens the
/// review. The date is composed text, so it follows the model's culture.
let planRow
  (culture: IObservable<CultureInfo>, onReview: Guid -> unit, entity: PlanEntity) : Control =
  let detail =
    culture
    |> Observable.map(fun current ->
      let stamp = entity.CreatedAt.ToLocalTime().ToString("g", current)
      $"{entity.Kind} · {stamp}")

  Button()
    .Content(contentRowObs(entity.Title, detail, chevron))
    .OnClickHandler(fun _ _ -> onReview entity.Id)
    .Margin(0.0, 0.0, 0.0, 4.0)
    .Classes("row")

let planSection
  (
    culture: IObservable<CultureInfo>,
    plans: IObservable<PlanEntity list>,
    onReview: Guid -> unit
  ) : Control =
  let template =
    FuncDataTemplate<PlanEntity>(
      (fun entity _ -> planRow(culture, onReview, entity)),
      false
    )

  let rows =
    ItemsControl().ItemTemplate(template)
    |> stretchItems
    |> fun list -> list.BindItemsSource(plans, fun entity -> entity.Id)

  let emptyState =
    labelKey(Keys.athletesEmpty).IsVisible(plans |> Observable.map List.isEmpty)

  StackPanel().Spacing(8.0).Children(emptyState, rows)

let profileLine(culture: IObservable<CultureInfo>, athlete: Athlete) : Control =
  let goalText (current: CultureInfo) goal =
    match goal with
    | Hypertrophy -> get(current, Keys.goalHypertrophy)
    | WeightLoss -> get(current, Keys.goalWeightLoss)
    | CardioVascularHealth -> get(current, Keys.goalCardio)
    | BasicBodyActivation -> get(current, Keys.goalActivation)
    | Strength -> get(current, Keys.goalStrength)

  let genderName(current: CultureInfo) =
    match athlete.Gender with
    | Male -> get(current, Keys.genderMale)
    | Female -> get(current, Keys.genderFemale)

  let detail =
    culture
    |> Observable.map(fun current ->
      let goals =
        athlete.Goals |> List.map(goalText current) |> String.concat ", "

      $"{genderName current} · {goals}")

  TextBlock().Text(detail).Classes("caption", "secondary")

/// The page: static text comes from the resource dictionary; the profile
/// and plan metadata follow the model's culture.
let view
  (props: DetailPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let culture = model |> Observable.map _.Culture
  let plans = model |> Observable.map _.Plans
  let onReview id = dispatch(ReviewRequested id)

  fluentPage {
    Lead = props.Lead
    Title = pageTitle props.Athlete.Profile.Name
    Commands = [
      iconPrimaryButtonKey(
        Keys.plansGenerate,
        Icons.flash,
        (fun () -> dispatch GenerateRequested)
      )
    ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          profileLine(culture, props.Athlete.Profile),
          sectionTitleKey Keys.plansTitle,
          planSection(culture, plans, onReview)
        )
  }

type AthleteDetailPage
  (
    env: AthleteDetailEnv,
    props: DetailPageProps,
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
        (fun (_: DetailPageProps) -> init ctx)
        (update ctx)
        (view props)
      |> Program.withSubscription(subscribe ctx)

    let root, lifetime = Program.runWithLifecycle props program

    this.Content <- root
    props.Lifetime.AddDisposable lifetime
