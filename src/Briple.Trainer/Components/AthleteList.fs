module Briple.Trainer.Components.AthleteList

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type AthleteListProps = {
  Culture: CultureInfo
  Athletes: AthleteEntity list
  OnNew: unit -> unit
  OnOpen: Guid -> unit
}

let view(props: AthleteListProps) : Control =
  let t key = get(props.Culture, key)

  let goalName goal =
    match goal with
    | Hypertrophy -> t Keys.goalHypertrophy
    | WeightLoss -> t Keys.goalWeightLoss
    | CardioVascularHealth -> t Keys.goalCardio
    | BasicBodyActivation -> t Keys.goalActivation
    | Strength -> t Keys.goalStrength

  let rows = [|
    for athlete in props.Athletes ->
      let summary = [
        athlete.Profile.Gender |> Gender.toFileToken
        String.concat ", " athlete.Profile.Variants

        athlete.Profile.Goals |> List.map goalName |> String.concat ", "
      ]

      let row =
        button(athlete.Profile.Name, (fun () -> props.OnOpen athlete.Id))

      row.HorizontalContentAlignment(HorizontalAlignment.Left) |> ignore

      row.Content(
        StackPanel()
          .Spacing(2.0)
          .Children(
            TextBlock().Text(athlete.Profile.Name).FontSize(16.0).FontWeight
              FontWeight.Bold,
            TextBlock()
              .Text(String.concat " · " summary)
              .FontFamily(font)
              .Foreground(muted)
          )
      )
      |> ignore

      row :> Control
  |]

  let listContent =
    match rows with
    | [||] -> label(t Keys.athletesEmpty)
    | _ -> StackPanel().Spacing(8.0).Children(rows) :> Control

  let newButton =
    let primary = primaryButton(t Keys.athletesNew, props.OnNew)

    primary.HorizontalAlignment HorizontalAlignment.Left

  page(
    t Keys.athletesTitle,
    StackPanel().Spacing(8.0).Children(newButton :> Control, listContent)
  )
