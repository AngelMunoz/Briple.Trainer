module Briple.Trainer.Views.Athletes

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Media
open NXUI.Extensions

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
type DetailPageProps = {
  Culture: PageCulture
  Lead: Control voption
  Plans: PlanStore
  Athlete: AthleteEntity
}

[<Struct>]
type Event =
  | GenerateRequested
  | ReviewRequested of planId: Guid

/// The trailing glyph of a row.
let inline chevron() : Control =
  PathIcon(Data = Icons.chevronRight, Width = 16.0, Height = 16.0)
    .Foreground(muted)

/// One plan row: the title and its metadata; the whole row opens the
/// review.
let planRow
  (culture: CultureInfo, onReview: Guid -> unit, entity: PlanEntity)
  : Control =
  let stamp = entity.CreatedAt.ToLocalTime().ToString("g", culture)
  let detail = $"{entity.Kind} · {stamp}"

  Button()
    .Content(contentRow(entity.Title, detail, chevron))
    .OnClickHandler(fun _ _ -> onReview entity.Id)
    .Margin(0.0, 0.0, 0.0, 4.0)
    .Classes("row")

let planSection
  (
    culture: CultureInfo,
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
    label(get(culture, Keys.athletesEmpty))
      .IsVisible(plans |> Observable.map List.isEmpty)

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
    .Classes("caption", "secondary")

/// The page at one culture.
let view
  (props: DetailPageProps)
  (onEvent: Event -> unit)
  (culture: CultureInfo)
  : Control =
  let t key = get(culture, key)

  let plans = props.Plans.Observe props.Athlete.Id

  let onReview id = onEvent(Event.ReviewRequested id)

  fluentPage {
    Lead = props.Lead
    Title = props.Athlete.Profile.Name
    Commands = [
      iconPrimaryButton(
        t Keys.plansGenerate,
        Icons.flash,
        (fun () -> onEvent Event.GenerateRequested)
      )
    ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          profileLine(culture, props.Athlete.Profile),
          sectionTitle(t Keys.plansTitle),
          planSection(culture, plans, onReview)
        )
    Measure = 760.0
  }

type AthleteDetailPage
  (props: DetailPageProps, [<InlineIfLambda>] onEvent: Event -> unit) as this =
  inherit UserControl()

  do
    this.Content <-
      Page.cultureView(
        props.Culture,
        (fun culture -> view props onEvent culture)
      )
