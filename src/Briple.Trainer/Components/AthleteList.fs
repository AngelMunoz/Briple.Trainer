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
  Lead: Control voption
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

/// The trailing glyph of a row: 16px, muted, centered by the row grid.
let inline chevron() : Control =
  PathIcon(Data = Icons.chevronRight, Width = 16.0, Height = 16.0)
    .Foreground(muted)

/// One row: the name and the profile summary; the whole row opens.
let athleteRow
  (culture: CultureInfo, onOpen: Guid -> unit, athlete: AthleteEntity)
  : Control =
  let summary = [
    athlete.Profile.Gender |> Gender.toFileToken
    athlete.Profile.Goals
    |> List.map(fun goal -> goalName(culture, goal))
    |> String.concat ", "
  ]

  Button()
    .Content(
      contentRow(athlete.Profile.Name, String.concat " · " summary, chevron)
    )
    .OnClickHandler(fun _ _ -> onOpen athlete.Id)
    .Margin(0.0, 0.0, 0.0, 4.0)
    .Classes("row")

/// The page at one culture.
let view
  (props: AthleteListProps)
  (onEvent: Event -> unit)
  (culture: CultureInfo)
  : Control =
  let t key = get(culture, key)

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
        label(t Keys.athletesEmpty)
      )
      .IsVisible(props.Athletes |> Observable.map List.isEmpty)

  let newCommand =
    iconPrimaryButton(
      t Keys.athletesNew,
      Icons.plus,
      (fun () -> onEvent Event.NewRequested)
    )

  fluentPage {
    Lead = props.Lead
    Title = t Keys.athletesTitle
    Commands = [ newCommand ]
    Footer = noFooter
    Body = StackPanel().Spacing(8.0).Children(emptyState, rows)
    Measure = 760.0
  }

type AthleteList
  (props: AthleteListProps, [<InlineIfLambda>] onEvent: Event -> unit) as this =
  inherit UserControl()

  do
    this.Content <-
      Page.cultureView(
        props.Culture,
        (fun culture -> view props onEvent culture)
      )
