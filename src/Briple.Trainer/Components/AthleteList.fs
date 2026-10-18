module Briple.Trainer.Components.AthleteList

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
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
open Briple.Trainer.Theme

[<Struct>]
type AthleteListProps = {
  Culture: PageCulture
  Lead: (unit -> Control) voption
  Athletes: IObservable<AthleteEntity list>
}

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
/// summary is composed text, so it follows the live culture; the name is
/// data and never changes.
let athleteRow
  (culture: PageCulture, onOpen: Guid -> unit, athlete: AthleteEntity)
  : Control =
  let detail =
    culture.Changed |> Observable.map(fun current -> summary(current, athlete))

  Button()
    .Content(contentRowObs(athlete.Profile.Name, detail, chevron))
    .OnClickHandler(fun _ _ -> onOpen athlete.Id)
    .Margin(0.0, 0.0, 0.0, 4.0)
    .Classes("row")

/// The page: static text comes from the resource dictionary; the row
/// summaries follow the culture stream.
let view (props: AthleteListProps) (onEvent: Event -> unit) : Control =
  let culture = props.Culture
  let onOpen id = onEvent(Event.OpenRequested id)

  let rows =
    let template =
      FuncDataTemplate<AthleteEntity>(
        (fun athlete _ -> athleteRow(culture, onOpen, athlete)),
        false
      )

    ItemsControl().ItemTemplate(template)
    |> stretchItems
    |> fun list ->
        list.BindItemsSource(props.Athletes, fun athlete -> athlete.Id)

  let emptyState =
    StackPanel()
      .Spacing(8.0)
      .Children(
        glyph(Icons.accountSearchOutline, 32.0, muted),
        labelKey Keys.athletesEmpty
      )
      .IsVisible(props.Athletes |> Observable.map List.isEmpty)

  let newCommand =
    iconPrimaryButtonKey(
      Keys.athletesNew,
      Icons.plus,
      (fun () -> onEvent Event.NewRequested)
    )

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.athletesTitle
    Commands = [ newCommand ]
    Footer = noFooter
    Body = StackPanel().Spacing(8.0).Children(emptyState, rows)
  }

type AthleteList
  (props: AthleteListProps, [<InlineIfLambda>] onEvent: Event -> unit) as this =
  inherit UserControl()

  do this.Content <- view props onEvent
