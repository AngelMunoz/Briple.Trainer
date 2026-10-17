module Briple.Trainer.Components.PlanReview

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open FSharp.Control.Reactive

open MVUish.Avalonia

open Briple.Trainer
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Hub
open Briple.Trainer.Components.Page
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

type Event =
  | PlanChanged of Plan
  | ViewChanged of week: int * editing: bool

[<Struct>]
type Props = {
  Culture: PageCulture
  Parsed: ParsedPlan
  Week: int voption
  Editing: bool
}

type Model = {
  Plan: Plan
  /// The last committed tree: what revert restores.
  Snapshot: Plan
  /// The week the pivot shows, by number.
  Week: int
  /// The page-level edit mode: on, every row shows its fields; off,
  /// the tree reads as prose.
  Editing: bool
}

type Msg =
  | SetField of path: RowPath * field: ExerciseField * text: string
  | SetWeek of week: int
  | SetEditing of on: bool
  | RevertEdit

/// The week numbers of the plan, in tree order.
let weekNumbersOf(plan: Plan) : int list = [
  for block in plan.Blocks do
    for variant in block.Variants do
      for week in variant.Weeks -> week.Number
]

let init(props: Props) =
  let weeks = weekNumbersOf props.Parsed.Plan

  let week =
    props.Week
    |> ValueOption.filter(fun number -> List.contains number weeks)
    |> ValueOption.defaultValue(weeks |> List.tryHead |> Option.defaultValue 1)

  {
    Plan = props.Parsed.Plan
    Snapshot = props.Parsed.Plan
    Week = week
    Editing = props.Editing
  },
  Cmd.none

let inline mapAt index ([<InlineIfLambda>] f) xs =
  xs |> List.mapi(fun i item -> if i = index then f item else item)

let applyField (field: ExerciseField) (text: string) (exercise: Exercise) =
  match field with
  | RepsField -> { exercise with Reps = text }
  | RirField -> { exercise with Rir = text }
  | RestField -> { exercise with Rest = text }
  | NotesField -> { exercise with Notes = text }

/// The exercise of one path in the given tree. Paths come from the tree
/// itself; the fallback keeps the reader total when the tree moved.
let exerciseAt (plan: Plan) (path: RowPath) (fallback: Exercise) : Exercise =
  plan.Blocks
  |> List.tryItem path.Block
  |> Option.bind(_.Variants >> List.tryItem path.Variant)
  |> Option.bind(_.Weeks >> List.tryItem path.Week)
  |> Option.bind(_.Days >> List.tryItem path.Day)
  |> Option.bind(_.Exercises >> List.tryFind(fun e -> e.Order = path.Order))
  |> Option.defaultValue fallback

/// Replaces the exercise a path points to, one named level at a time.
let editExercise (path: RowPath) (edit: Exercise -> Exercise) (plan: Plan) =
  let day(current: Day) : Day = {
    current with
        Exercises =
          current.Exercises
          |> List.map(fun e -> if e.Order = path.Order then edit e else e)
  }

  let week(current: Week) : Week = {
    current with
        Days = current.Days |> mapAt path.Day day
  }

  let variant(current: Variant) : Variant = {
    current with
        Weeks = current.Weeks |> mapAt path.Week week
  }

  let block(current: Block) : Block = {
    current with
        Variants = current.Variants |> mapAt path.Variant variant
  }

  {
    plan with
        Blocks = plan.Blocks |> mapAt path.Block block
  }

/// Every transition reports outward so the page keeps the draft and the
/// navigation state across culture rebuilds. The initial model never
/// emits: an untouched plan is not a change.
let update (props: Props) (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | SetField(path, field, text) ->
    let edited = model.Plan |> editExercise path (applyField field text)

    if edited <> model.Plan then
      onEvent(Event.PlanChanged edited)

    { model with Plan = edited }, Cmd.none

  | SetWeek week ->
    onEvent(Event.ViewChanged(week, model.Editing))
    { model with Week = week }, Cmd.none

  // leaving the edit mode commits: the snapshot rides the tree
  | SetEditing false ->
    onEvent(Event.ViewChanged(model.Week, false))

    {
      model with
          Snapshot = model.Plan
          Editing = false
    },
    Cmd.none

  | SetEditing true ->
    onEvent(Event.ViewChanged(model.Week, true))
    { model with Editing = true }, Cmd.none

  | RevertEdit ->
    onEvent(Event.PlanChanged model.Snapshot)
    onEvent(Event.ViewChanged(model.Week, false))

    {
      model with
          Plan = model.Snapshot
          Editing = false
    },
    Cmd.none

/// The exercise line as prose: what reading a plan needs.
let exerciseLine(exercise: Exercise) : string =
  let parts = [
    $"{exercise.Order}. {exercise.Name}"
    exercise.Reps
    if exercise.Rir <> "-" then
      $"RIR {exercise.Rir}"
    exercise.Rest
    if exercise.Notes <> "-" then
      exercise.Notes
  ]

  String.concat " · " parts

// --- the section builders ----------------------------------------------------

/// What the section builders read, as one value: the props, the model
/// stream and the dispatch.
[<Struct>]
type SectionContext = {
  Props: Props
  Model: IObservable<Model>
  Dispatch: Dispatch<Msg>
}

/// One exercise: prose when the page reads, its fields when the page
/// edits. The typed text lives in the boxes; the tree updates live.
let exerciseRow(ctx: SectionContext, path: RowPath, exercise: Exercise) =
  let rowPath = { path with Order = exercise.Order }

  let readBody(current: Exercise) : Control =
    StackPanel()
      .Spacing(2.0)
      .Children(
        TextBlock().Text($"{current.Order}. {current.Name}").Classes("body"),
        TextBlock()
          .Text(
            [
              current.Reps
              if current.Rir <> "-" then
                $"RIR {current.Rir}"
              current.Rest
              if current.Notes <> "-" then
                current.Notes
            ]
            |> String.concat " · "
          )
          .Classes("caption", "secondary")
          .TextWrapping(TextWrapping.Wrap)
      )

  let editBody: Control =
    let entryFor field initial =
      entry(
        "",
        initial,
        fun text -> ctx.Dispatch(SetField(rowPath, field, text))
      )

    StackPanel()
      .Spacing(4.0)
      .Children(
        TextBlock()
          .Text($"{exercise.Order}. {exercise.Name}")
          .Classes("body-strong"),
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            (entryFor RepsField exercise.Reps).Width(90.0),
            (entryFor RirField exercise.Rir).Width(60.0),
            (entryFor RestField exercise.Rest).Width(90.0)
          ),
        (entryFor NotesField exercise.Notes).Width(320.0)
      )

  let body =
    ctx.Model
    |> Observable.map(fun m -> (m.Editing, exerciseAt m.Plan rowPath exercise))
    |> Observable.distinctUntilChanged
    |> Observable.map(fun (editing, current) ->
      if editing then editBody else readBody current)

  ContentControl().Content(body.ToBinding())

/// The exercises of a day grouped by circuit and laps.
let circuitGroupsOf(day: Day) : ((string * string) * Exercise list) list =
  day.Exercises
  |> List.fold
    (fun groups exercise ->
      match groups with
      | ((circuit, laps), items) :: rest when
        circuit = exercise.Circuit && laps = exercise.Laps
        ->
        ((circuit, laps), items @ [ exercise ]) :: rest
      | _ -> ((exercise.Circuit, exercise.Laps), [ exercise ]) :: groups)
    []
  |> List.rev

/// The day card body: its notes and its circuits. The hub slot header
/// carries the day's name.
let dayBody(ctx: SectionContext, path: RowPath, day: Day) : Control =
  let children: Control array = [|
    for note in day.Notes do
      TextBlock().Text(note).Classes("caption", "secondary")

    for (circuit, laps), exercises in circuitGroupsOf day do
      TextBlock()
        .Text(
          ctx.Props.Culture.Changed
          |> Observable.map(fun current ->
            if String.IsNullOrWhiteSpace laps then
              circuit
            else
              $"{circuit} × {laps} {get(current, Keys.planLaps)}")
        )
        .Classes("caption-strong", "secondary")

      for exercise in exercises do
        exerciseRow(ctx, path, exercise)
  |]

  StackPanel().Spacing(8.0).Children(children)

/// The day's slot header: its id and title.
let dayHeader(day: Day) : string =
  match day.Title with
  | Some title -> $"{day.Id} — {title}"
  | None -> day.Id

/// One variant of the shown week: its title, its week caption and its
/// days as hub columns.
let variantSection(ctx: SectionContext, path: RowPath, variant: Variant) =
  let plan = ctx.Props.Parsed.Plan
  let week = variant.Weeks[path.Week]

  let rir = tryMesocycleRir plan week.Number

  let caption =
    TextBlock()
      .Text(
        ctx.Props.Culture.Changed
        |> Observable.map(fun current ->
          let weekLabel = getf(current, Keys.planWeek, [ week.Number :> obj ])

          match rir with
          | Some rir -> $"{weekLabel} · RIR {rir}"
          | None -> weekLabel)
      )
      .Classes("caption-strong", "accent-text")
      .HorizontalAlignment(HorizontalAlignment.Left)

  let days: HubSlot list = [
    for dayIndex, day in week.Days |> List.indexed ->
      slot(dayHeader day, dayBody(ctx, { path with Day = dayIndex }, day))
  ]

  StackPanel()
    .Spacing(8.0)
    .Children(
      TextBlock()
        .Text(
          match variant.Title with
          | Some title -> $"{variant.Id} — {title}"
          | None -> variant.Id
        )
        .Classes("body-strong"),
      caption,
      Hub.hub {
        Title = ""
        Slots = days
        Active = ValueNone
        OnSelect = ValueNone
      }
    )

type PlanReview(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) =
  inherit UserControl()

  do

    let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      let plan = props.Parsed.Plan

      let ctx = {
        Props = props
        Model = model
        Dispatch = dispatch
      }

      // one week per render call: pure over the tree
      let render(weekNumber: int) : Control =
        let sections: Control array = [|
          for blockIndex, block in plan.Blocks |> List.indexed do
            for variantIndex, variant in block.Variants |> List.indexed do
              match
                variant.Weeks
                |> List.tryFindIndex(fun week -> week.Number = weekNumber)
              with
              | Some weekIndex ->
                variantSection(
                  ctx,
                  {
                    Block = blockIndex
                    Variant = variantIndex
                    Week = weekIndex
                    Day = 0
                    Order = 0
                  },
                  variant
                )
              | None -> ()
        |]

        StackPanel().Spacing(12.0).Children sections

      // the segmented week pivot: the active week reads accent
      let weekButton(weekNumber: int) : Control =
        let active = model |> Observable.map(fun m -> m.Week = weekNumber)

        Button(
          Content =
            TextBlock()
              .Text(
                props.Culture.Changed
                |> Observable.map(fun current ->
                  getf(current, Keys.planWeek, [ weekNumber :> obj ]))
              )
        )
          .Foreground(
            active |> Observable.map(fun on -> if on then accent else muted)
          )
          .FontWeight(
            active
            |> Observable.map(fun on ->
              if on then FontWeight.SemiBold else FontWeight.Normal)
          )
          .OnClickHandler(fun _ _ -> dispatch(SetWeek weekNumber))
          .Classes("chrome")

      let weekSelector =
        StackPanel().Orientation(Orientation.Horizontal).Spacing(4.0).Children [|
          for weekNumber in weekNumbersOf plan |> List.distinct ->
            weekButton weekNumber
        |]

      // the week content: one build per week number, memoized in a
      // dictionary the stream's map closes over; the host binds the
      // distinct week stream, so the pivot click and any rebuild ride
      // the same reactive path
      let built = System.Collections.Generic.Dictionary<int, Control>()

      let weekContent(weekNumber: int) : Control =
        match built.TryGetValue weekNumber with
        | true, content -> content
        | _ ->
          let content = render weekNumber
          built[weekNumber] <- content
          content

      let weekHost =
        ContentControl()
          .Content(
            (model
             |> Observable.map _.Week
             |> Observable.distinctUntilChanged
             |> Observable.map weekContent)
              .ToBinding()
          )

      // the edit toggle: Editar when reading, Listo + Revertir when
      // editing — one place, not one button per row
      let editToggle =
        ContentControl()
          .Content(
            (model
             |> Observable.map _.Editing
             |> Observable.distinctUntilChanged
             |> Observable.map(fun editing ->
               let view: Control =
                 if editing then
                   StackPanel()
                     .Orientation(Orientation.Horizontal)
                     .Spacing(8.0)
                     .Children(
                       iconButtonKey(
                         Keys.planRevert,
                         Icons.undo,
                         fun () -> dispatch RevertEdit
                       ),
                       iconButtonKey(
                         Keys.planDone,
                         Icons.check,
                         fun () -> dispatch(SetEditing false)
                       )
                     )
                 else
                   iconButtonKey(
                     Keys.planEdit,
                     Icons.pencil,
                     fun () -> dispatch(SetEditing true)
                   )

               view))
              .ToBinding()
          )

      StackPanel()
        .Spacing(8.0)
        .Children(
          DockPanel()
            .LastChildFill(false)
            .Children(weekSelector.DockLeft(), editToggle.DockRight()),
          weekHost
        )

    let program = Program.mkProgram init (update props onEvent) view
    base.Content <- Program.runWith props program
