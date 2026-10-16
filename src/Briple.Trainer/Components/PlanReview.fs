module Briple.Trainer.Components.PlanReview

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open MVUish.Avalonia

open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type ExerciseField =
  | RepsField
  | RirField
  | RestField
  | NotesField

[<Struct>]
type RowPath = {
  Block: int
  Variant: int
  Week: int
  Day: int
  Order: int
}

type Event = PlanChanged of Plan

[<Struct>]
type Props = {
  Culture: CultureInfo
  Parsed: ParsedPlan
}

type Model = { Plan: Plan }

type Msg = SetField of path: RowPath * field: ExerciseField * text: string

let init(props: Props) = { Plan = props.Parsed.Plan }, Cmd.none

let inline mapAt index ([<InlineIfLambda>] f) xs =
  xs |> List.mapi(fun i item -> if i = index then f item else item)

let applyField (field: ExerciseField) (text: string) (exercise: Exercise) =
  match field with
  | RepsField -> { exercise with Reps = text }
  | RirField -> { exercise with Rir = text }
  | RestField -> { exercise with Rest = text }
  | NotesField -> { exercise with Notes = text }

let update (msg: Msg) (model: Model) =
  match msg with
  | SetField(path, field, text) ->
    let plan = model.Plan

    let day =
      plan.Blocks
      |> List.item path.Block
      |> _.Variants
      |> List.item path.Variant
      |> _.Weeks
      |> List.item path.Week
      |> _.Days
      |> List.item path.Day

    let index =
      day.Exercises
      |> List.findIndex(fun candidate -> candidate.Order = path.Order)

    let plan = {
      plan with
          Blocks =
            plan.Blocks
            |> mapAt path.Block (fun block -> {
              block with
                  Variants =
                    block.Variants
                    |> mapAt path.Variant (fun variant -> {
                      variant with
                          Weeks =
                            variant.Weeks
                            |> mapAt path.Week (fun week -> {
                              week with
                                  Days =
                                    week.Days
                                    |> mapAt path.Day (fun current -> {
                                      current with
                                          Exercises =
                                            current.Exercises
                                            |> mapAt
                                              index
                                              (applyField field text)
                                    })
                            })
                    })
            })
    }

    { model with Plan = plan }, Cmd.none

type PlanReview(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) as this
  =
  inherit UserControl()
  let t key = get(props.Culture, key)

  do

    let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      // events out: every model emission reaches the parent
      let planTap =
        model |> Observable.subscribe(fun m -> onEvent(PlanChanged m.Plan))

      this.DetachedFromVisualTree.Add(fun _ -> planTap.Dispose())

      // the tree renders once from the parsed snapshot; edits leave
      // through dispatch and reach the parent as PlanChanged events
      let plan = props.Parsed.Plan

      let exerciseRow (path: RowPath) (exercise: Exercise) =
        let path = { path with Order = exercise.Order }

        let entryFor field initial =
          entry("", initial, fun text -> dispatch(SetField(path, field, text)))

        StackPanel()
          .Spacing(2.0)
          .Margin(12.0, 4.0, 0.0, 0.0)
          .Children(
            TextBlock()
              .Text($"{exercise.Order}. {exercise.Name}")
              .FontWeight(FontWeight.Bold)
              .FontFamily(font),
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .Children(
                (entryFor RepsField exercise.Reps).Width(90.0),
                (entryFor RirField exercise.Rir).Width(60.0),
                (entryFor RestField exercise.Rest).Width(90.0),
                (entryFor NotesField exercise.Notes).Width(260.0)
              )
          )
        :> Control

      let circuitGroup (path: RowPath) group =
        let (circuit, laps), exercises = group

        StackPanel().Spacing(0.0).Children [|

          TextBlock()
            .Text(
              if System.String.IsNullOrWhiteSpace laps then
                circuit
              else
                $"{circuit} × {laps} {t Keys.planLaps}"
            )
            .FontWeight(FontWeight.Bold)
            .Foreground(muted)
            .FontFamily(font)
          :> Control

          yield!
            exercises |> List.map(fun exercise -> exerciseRow path exercise)
        |]
        :> Control

      let daySection (path: RowPath) (day: Day) =
        let groups =
          day.Exercises
          |> List.fold
            (fun groups exercise ->
              match groups with
              | ((circuit, laps), items) :: rest when
                circuit = exercise.Circuit && laps = exercise.Laps
                ->
                ((circuit, laps), items @ [ exercise ]) :: rest
              | _ ->
                ((exercise.Circuit, exercise.Laps), [ exercise ]) :: groups)
            []
          |> List.rev

        StackPanel().Spacing(4.0).Margin(12.0, 0.0, 0.0, 0.0).Children [|

          TextBlock()
            .Text(
              match day.Title with
              | Some title -> $"{day.Id} — {title}"
              | None -> day.Id
            )
            .FontWeight(FontWeight.Bold)
            .FontFamily(font)
          :> Control

          for note in day.Notes do
            TextBlock().Text(note).Foreground(muted).FontFamily(font) :> Control

          yield! groups |> List.map(fun group -> circuitGroup path group)
        |]
        :> Control

      let weekSection (path: RowPath) (week: Week) =
        let rir = tryMesocycleRir plan week.Number

        let weekLabel =
          getf(props.Culture, Keys.planWeek, [ week.Number :> obj ])

        let badgeText =
          match rir with
          | Some rir -> $"{weekLabel} · RIR {rir}"
          | None -> weekLabel

        StackPanel()
          .Spacing(6.0)
          .Margin(0.0, 8.0)
          .Children(
            TextBlock()
              .Text(badgeText)
              .FontWeight(FontWeight.Bold)
              .Foreground(Brushes.White)
              .Background(accent)
              .Padding(8.0, 2.0)
              .FontFamily(font),
            StackPanel()
              .Children(
                [|
                  for dayIndex, day in List.indexed week.Days ->
                    daySection { path with Day = dayIndex } day
                |]
              )
          )
        :> Control

      let variantSection blockIndex variantIndex (variant: Variant) =
        StackPanel().Spacing(0.0).Margin(0.0, 8.0).Children [|
          TextBlock()
            .Text(
              match variant.Title with
              | Some title -> $"{variant.Id} — {title}"
              | None -> variant.Id
            )
            .FontSize(16.0)
            .FontWeight(FontWeight.Bold)
            .FontFamily
            font
          :> Control

          yield!
            variant.Weeks
            |> List.mapi(fun weekIndex week ->
              weekSection
                {
                  Block = blockIndex
                  Variant = variantIndex
                  Week = weekIndex
                  Day = 0
                  Order = 0
                }
                week)
        |]
        :> Control

      let sections: Control array = [|
        for blockIndex, block in plan.Blocks |> List.indexed do
          TextBlock()
            .Text(Gender.toFileToken block.Gender)
            .FontSize(18.0)
            .FontWeight(FontWeight.Bold)
            .FontFamily(font)

          for variantIndex, variant in block.Variants |> List.indexed do
            variantSection blockIndex variantIndex variant

        if not(List.isEmpty plan.Extras) then
          label(t Keys.planGuide)

          for extra in plan.Extras do
            TextBlock().Text(extra).Foreground(muted).FontFamily(font)

      |]

      StackPanel().Spacing(4.0).Children(sections) :> Control

    let program = Program.mkProgram init update view

    this.Content <- Program.runWith props program
