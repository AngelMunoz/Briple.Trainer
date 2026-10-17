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
/// review. The date is composed text, so it follows the live culture.
let planRow
  (culture: PageCulture, onReview: Guid -> unit, entity: PlanEntity)
  : Control =
  let detail =
    culture.Changed
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
    culture: PageCulture,
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

let profileLine(culture: PageCulture, athlete: Athlete) : Control =
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
    culture.Changed
    |> Observable.map(fun current ->
      let goals =
        athlete.Goals |> List.map(goalText current) |> String.concat ", "

      $"{genderName current} · {goals}")

  TextBlock().Text(detail).Classes("caption", "secondary")

/// The page: static text comes from the resource dictionary; the profile
/// and plan metadata follow the culture stream.
let view (props: DetailPageProps) (onEvent: Event -> unit) : Control =
  let culture = props.Culture
  let plans = props.Plans.Observe props.Athlete.Id

  let onReview id = onEvent(Event.ReviewRequested id)

  fluentPage {
    Lead = props.Lead
    Title = pageTitle props.Athlete.Profile.Name
    Commands = [
      iconPrimaryButtonKey(
        Keys.plansGenerate,
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
          sectionTitleKey Keys.plansTitle,
          planSection(culture, plans, onReview)
        )
    Measure = 760.0
  }

type AthleteDetailPage
  (props: DetailPageProps, [<InlineIfLambda>] onEvent: Event -> unit) as this =
  inherit UserControl()

  do this.Content <- view props onEvent
