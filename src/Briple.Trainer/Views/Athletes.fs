module Briple.Trainer.Views.Athletes

// The athlete detail page: the profile and the plan history. The plans
// section is a store subscription with keyed rows; writes go through the
// store and the emissions land as refresh messages. Memories belong to
// the plan context, so they are written and reviewed from the plan
// review page.

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open MVUish.Avalonia
open MVUish.Avalonia.Collections

open Briple.Trainer
open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.Theme

[<Struct>]
type DetailPageProps = {
  Culture: CultureInfo
  Plans: PlanStore
  Athlete: AthleteEntity
}

/// What the page reports to its host.
[<Struct>]
type Event =
  | GenerateRequested
  | ReviewRequested of planId: Guid

// --- the page state ---

type Model = { Plans: PlanEntity list }

type Msg = PlansRefreshed of PlanEntity list

let init(_: DetailPageProps) = { Plans = [] }, Cmd.none

let update (msg: Msg) (model: Model) =
  match msg with
  | PlansRefreshed plans -> { model with Plans = plans }, Cmd.none

// --- page pieces as module functions ---

/// One plan row: the title and its metadata; the whole row opens the
/// review.
let planRow
  (props: DetailPageProps, onReview: Guid -> unit, entity: PlanEntity)
  : Control =
  button(entity.Title, (fun () -> onReview entity.Id))
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Margin(0.0, 0.0, 0.0, 6.0)
    .Content(
      StackPanel()
        .Spacing(2.0)
        .Children(
          TextBlock()
            .Text(entity.Title)
            .FontSize(14.0)
            .FontWeight(FontWeight.Bold),
          TextBlock()
            .Text($"{entity.Kind} · {entity.CreatedAt.ToLocalTime():g}")
            .Foreground(muted)
            .FontFamily(font)
        )
    )
  :> Control

let planSection
  (props: DetailPageProps, model: IObservable<Model>, onReview: Guid -> unit)
  : Control =
  let t key = get(props.Culture, key)

  // the keyed binding diffs the store emissions: rows survive
  let rows =
    let template =
      FuncDataTemplate<PlanEntity>(
        (fun entity _ -> planRow(props, onReview, entity)),
        false
      )

    let list = ItemsControl().ItemTemplate(template)

    list.BindItemsSource(
      model |> Observable.map(fun m -> m.Plans),
      fun entity -> entity.Id
    )
    |> ignore

    list

  let emptyState =
    label(t Keys.athletesEmpty)
      .IsVisible(model |> Observable.map(fun m -> List.isEmpty m.Plans))

  StackPanel().Spacing(8.0).Children(emptyState, rows)

let profileLine(culture: CultureInfo, athlete: Athlete) : Control =
  let goalText goal =
    match goal with
    | Hypertrophy -> get(culture, Keys.goalHypertrophy)
    | WeightLoss -> get(culture, Keys.goalWeightLoss)
    | CardioVascularHealth -> get(culture, Keys.goalCardio)
    | BasicBodyActivation -> get(culture, Keys.goalActivation)
    | Strength -> get(culture, Keys.goalStrength)

  let goals = athlete.Goals |> List.map goalText |> String.concat ", "

  TextBlock()
    .Text(
      [ athlete.Gender |> Gender.toFileToken; goals ] |> String.concat " · "
    )
    .Foreground(muted)
    .FontFamily(font)

// --- the page ---

let view
  (props: DetailPageProps)
  (onEvent: Event -> unit)
  (model: IObservable<Model>)
  (_: Dispatch<Msg>)
  : Control =
  let t key = get(props.Culture, key)

  let onReview id = onEvent(Event.ReviewRequested id)

  ScrollViewer()
    .Content(
      StackPanel()
        .Margin(24.0)
        .Spacing(8.0)
        .Children(
          pageTitle props.Athlete.Profile.Name,
          profileLine(props.Culture, props.Athlete.Profile),
          iconPrimaryButton(
            t Keys.plansGenerate,
            Icons.flash,
            (fun () -> onEvent Event.GenerateRequested)
          )
            .HorizontalAlignment(HorizontalAlignment.Left),
          sectionTitle(t Keys.plansTitle),
          planSection(props, model, onReview)
        )
    )

type AthleteDetailPage
  (props: DetailPageProps, [<InlineIfLambda>] onEvent: Event -> unit) as this =
  inherit UserControl()

  // the plan store in: the BehaviorSubject replay fills the model at
  // once and every later write lands as a refresh
  let subscribe(props: DetailPageProps) : Sub<Msg> =
    fun dispatch ->
      props.Plans.Observe props.Athlete.Id
      |> Observable.subscribe(PlansRefreshed >> dispatch)

  do
    let program =
      Program.mkProgram init update (view props onEvent)
      |> Program.withSubscription(fun _ -> subscribe props)

    this.Content <- Program.runWith props program
