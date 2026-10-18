module Briple.Trainer.Components.PlanReview

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open Avalonia.Input
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
open Briple.Trainer.Core.Plan.Sanitize
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prescription
open Briple.Trainer.Core.Requests
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
  Index: int
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
  /// The row that is open, when one is.
  ActiveRow: RowPath voption
  /// The page-level edit mode: on, one row can be open; off, the tree
  /// reads as prose.
  Editing: bool
}

type Msg =
  | SetField of path: RowPath * field: ExerciseField * text: string
  | SetWeek of week: int
  | BeginEdit of path: RowPath
  | EndEdit
  | RemoveRow of path: RowPath
  | AdjustLaps of path: RowPath * delta: int
  | SetEditing of on: bool
  | RevertEdit
  | Undo

/// The week numbers of the plan, in tree order.
let weekNumbersOf(plan: Plan) : int list = [
  for block in plan.Blocks do
    for variant in block.Variants do
      for week in variant.Weeks -> week.Number
]

/// The first row of the shown week, for a page that comes back in edit
/// mode.
let firstRowPath(plan: Plan, weekNumber: int) : RowPath voption =
  plan.Blocks
  |> List.tryHead
  |> Option.bind(fun block ->
    block.Variants
    |> List.tryHead
    |> Option.bind(fun variant ->
      variant.Weeks
      |> List.tryFindIndex(fun week -> week.Number = weekNumber)
      |> Option.bind(fun weekIndex ->
        variant.Weeks.[weekIndex].Days
        |> List.tryHead
        |> Option.bind(fun day ->
          if List.isEmpty day.Exercises then
            None
          else
            Some {
              Block = 0
              Variant = 0
              Week = weekIndex
              Day = 0
              Index = 0
            }))))
  |> ValueOption.ofOption

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
    ActiveRow =
      if props.Editing then
        firstRowPath(props.Parsed.Plan, week)
      else
        ValueNone
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
  |> Option.bind(_.Exercises >> List.tryItem path.Index)
  |> Option.defaultValue fallback

/// Replaces the exercise a path points to, one named level at a time.
let editExercise (path: RowPath) (edit: Exercise -> Exercise) (plan: Plan) =
  let day(current: Day) : Day = {
    current with
        Exercises = current.Exercises |> mapAt path.Index edit
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

/// Replaces the day a path points to.
let updateDayAt (path: RowPath) (update: Day -> Day) (plan: Plan) : Plan = {
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
                              Days = week.Days |> mapAt path.Day update
                        })
                })
        })
}

/// Renumbers the rows of each circuit from 1, in list order.
let renumberOrders(exercises: Exercise list) : Exercise list =
  exercises
  |> List.fold
    (fun (counts, acc) exercise ->
      let next =
        (counts |> Map.tryFind exercise.Circuit |> Option.defaultValue 0) + 1

      Map.add exercise.Circuit next counts,
      { exercise with Order = next } :: acc)
    (Map.empty, [])
  |> snd
  |> List.rev

/// Removes one row and renumbers its circuit.
let removeExercise (path: RowPath) (plan: Plan) : Plan =
  plan
  |> updateDayAt path (fun day ->
    let remaining =
      day.Exercises
      |> List.indexed
      |> List.filter(fun (index, _) -> index <> path.Index)
      |> List.map snd
      |> renumberOrders

    { day with Exercises = remaining })

/// Adds or removes one lap to every row of the row's circuit.
let adjustLaps (path: RowPath) (delta: int) (plan: Plan) : Plan =
  plan
  |> updateDayAt path (fun day ->
    match day.Exercises |> List.tryItem path.Index with
    | None -> day
    | Some exercise ->
      let current =
        match Int32.TryParse exercise.Laps with
        | true, value -> value
        | _ -> 1

      let next = max 1 (current + delta)

      {
        day with
            Exercises =
              day.Exercises
              |> List.map(fun row ->
                if row.Circuit = exercise.Circuit then
                  { row with Laps = string next }
                else
                  row)
      })

/// The fields of a row that force a week rebuild. The editable fields
/// (reps, effort, rest, notes, name) are not part of the shape, so a
/// field edit keeps the open row.
[<Struct>]
type RowShape = {
  Circuit: string
  Laps: string
  Order: int
}

type DayShape = { Rows: RowShape list }

type WeekShape = { Days: DayShape list }

type VariantShape = { Weeks: WeekShape list }

type BlockShape = { Variants: VariantShape list }

type PlanShape = { Blocks: BlockShape list }

let dayShape(day: Day) : DayShape = {
  Rows =
    day.Exercises
    |> List.map(fun exercise -> {
      Circuit = exercise.Circuit
      Laps = exercise.Laps
      Order = exercise.Order
    })
}

let weekShape(week: Week) : WeekShape = {
  Days = week.Days |> List.map dayShape
}

let variantShape(variant: Variant) : VariantShape = {
  Weeks = variant.Weeks |> List.map weekShape
}

let blockShape(block: Block) : BlockShape = {
  Variants = block.Variants |> List.map variantShape
}

/// The shape that forces a week rebuild: circuits, laps and orders.
let structureOf(plan: Plan) : PlanShape = {
  Blocks = plan.Blocks |> List.map blockShape
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

  | BeginEdit path ->
    onEvent(Event.ViewChanged(model.Week, true))

    {
      model with
          ActiveRow = ValueSome path
          Editing = true
          Snapshot = model.Plan
    },
    Cmd.none

  | EndEdit ->
    onEvent(Event.ViewChanged(model.Week, true))

    {
      model with
          ActiveRow = ValueNone
          Editing = true
          Snapshot = model.Plan
    },
    Cmd.none

  | RemoveRow path ->
    let edited = model.Plan |> removeExercise path

    onEvent(Event.PlanChanged edited)
    onEvent(Event.ViewChanged(model.Week, true))

    {
      model with
          Plan = edited
          Snapshot = edited
          ActiveRow = ValueNone
          Editing = true
    },
    Cmd.none

  | AdjustLaps(path, delta) ->
    let edited = model.Plan |> adjustLaps path delta

    onEvent(Event.PlanChanged edited)

    {
      model with
          Plan = edited
          Snapshot = edited
    },
    Cmd.none

  // leaving the edit mode commits: the snapshot rides the tree
  | SetEditing false ->
    onEvent(Event.ViewChanged(model.Week, false))

    {
      model with
          Snapshot = model.Plan
          ActiveRow = ValueNone
          Editing = false
    },
    Cmd.none

  | SetEditing true ->
    onEvent(Event.ViewChanged(model.Week, true))

    {
      model with
          Snapshot = model.Plan
          ActiveRow = ValueNone
          Editing = true
    },
    Cmd.none

  | RevertEdit ->
    onEvent(Event.PlanChanged model.Snapshot)
    onEvent(Event.ViewChanged(model.Week, true))

    {
      model with
          Plan = model.Snapshot
          ActiveRow = ValueNone
          Editing = true
    },
    Cmd.none

  | Undo ->
    onEvent(Event.PlanChanged model.Snapshot)

    {
      model with
          Plan = model.Snapshot
          ActiveRow = ValueNone
          Editing = false
    },
    Cmd.none

/// The dose line: reps, effort and rest. "-" and empty fields are
/// omitted, and the effort value travels as written (RIR, RPE or zone).
let doseText(exercise: Exercise) : string =
  [ exercise.Reps; exercise.Rir; exercise.Rest ]
  |> List.filter(fun part -> part <> "" && part <> "-")
  |> String.concat " · "

/// The notes line, absent when the plan writes "-".
let notesText(exercise: Exercise) : string option =
  if exercise.Notes = "" || exercise.Notes = "-" then
    None
  else
    Some exercise.Notes

/// Splits a "#x key | body" extra into its key and body.
let splitExtra(text: string) : string * string =
  match text.IndexOf '|' with
  | -1 -> "", text.Trim()
  | index -> text.Substring(0, index).Trim(), text.Substring(index + 1).Trim()

/// The guide lines the PWA shows after the week: descansos and
/// regla-circuito, one per key, descansos first.
let guideTrailer(plan: Plan) : string list =
  plan.Extras
  |> List.map splitExtra
  |> List.filter(fun (key, _) -> key = "descansos" || key = "regla-circuito")
  |> List.sortBy(fun (key, _) -> if key = "descansos" then 0 else 1)
  |> List.distinctBy fst
  |> List.map(fun (_, body) -> "· " + body)

/// The count of exercises and circuits in the day.
let dayBadge(culture: CultureInfo, day: Day) : string =
  let exercises = day.Exercises.Length

  let circuits =
    day.Exercises |> List.map _.Circuit |> List.distinct |> List.length

  let exerciseWord =
    get(
      culture,
      if exercises = 1 then
        Keys.reviewExercisesOne
      else
        Keys.reviewExercisesMany
    )

  let circuitWord =
    get(
      culture,
      if circuits = 1 then
        Keys.reviewCircuitsOne
      else
        Keys.reviewCircuitsMany
    )

  getf(
    culture,
    Keys.reviewDayBadge,
    [
      exercises :> obj
      exerciseWord :> obj
      circuits :> obj
      circuitWord :> obj
    ]
  )

/// The circuit heading: "Circuito A · 2 vueltas", the singular, or just
/// the letter when the plan has no laps.
let circuitHeading
  (culture: CultureInfo, circuit: string, laps: string)
  : string =
  if String.IsNullOrWhiteSpace laps || laps = "-" then
    getf(culture, Keys.reviewCircuitOnly, [ circuit :> obj ])
  elif laps = "1" then
    getf(culture, Keys.reviewCircuitOne, [ circuit :> obj ])
  else
    getf(culture, Keys.reviewCircuit, [ circuit :> obj; laps :> obj ])

/// The variant heading the athlete sees: gender, frequency and title.
let variantHeading
  (culture: CultureInfo, block: Block, variant: Variant)
  : string =
  let gender =
    get(
      culture,
      if block.Gender = Male then
        Keys.genderMale
      else
        Keys.genderFemale
    )

  let frequency =
    match VariantFrequency.tryFileToken variant.Id with
    | Some frequency ->
      getf(
        culture,
        Keys.reviewDays,
        [ VariantFrequency.daysPerWeek frequency :> obj ]
      )
    | None -> variant.Id

  let title =
    variant.Title
    |> Option.map(fun value -> $" — {value}")
    |> Option.defaultValue ""

  $"{gender} · {frequency}{title}"

/// The field a content warning points at, as prose.
let warningFieldText(culture: CultureInfo, field: ContentField) : string =
  match field with
  | PlanTitle -> get(culture, Keys.reviewFieldTitle)
  | PlanOrigin -> get(culture, Keys.reviewFieldOrigin)
  | ExerciseName name -> getf(culture, Keys.reviewFieldName, [ name :> obj ])
  | ExerciseNotes name -> getf(culture, Keys.reviewFieldNotes, [ name :> obj ])
  | ExtraLine key -> if key = "" then get(culture, Keys.planGuide) else key

/// One content warning line.
let warningText(culture: CultureInfo, warning: ContentWarning) : string =
  getf(
    culture,
    Keys.reviewContentWarning,
    [ warningFieldText(culture, warning.Field) :> obj; warning.Pattern :> obj ]
  )

// --- the section builders ----------------------------------------------------

/// What the section builders read, as one value: the props, the model
/// stream and the dispatch.
[<Struct>]
type SectionContext = {
  Props: Props
  Model: IObservable<Model>
  Dispatch: Dispatch<Msg>
}

/// The title line of one row.
let rowTitle(current: Exercise) : Control =
  TextBlock()
    .Text($"{current.Circuit}{current.Order}  {current.Name}")
    .Classes("body")

/// The dose line, when the row has one.
let doseLine(current: Exercise) : Control option =
  let dose = doseText current

  if dose = "" then
    None
  else
    Some(
      TextBlock()
        .Text(dose)
        .Classes("caption", "secondary")
        .TextWrapping(TextWrapping.Wrap)
    )

/// The notes line, when the row has one.
let notesLine(current: Exercise) : Control option =
  notesText current
  |> Option.map(fun notes ->
    TextBlock()
      .Text(notes)
      .Classes("caption", "secondary")
      .TextWrapping(TextWrapping.Wrap))

/// A vertical panel of controls.
let inline panel (spacing: float) (children: Control array) : Control =
  StackPanel().Spacing(spacing).Children(children)

/// A horizontal panel of controls.
let inline row (spacing: float) (children: Control array) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(spacing)
    .Children(children)

/// A row whose body fills the line and whose actions stay at the right.
let inline rowShell
  (body: unit -> Control)
  (actions: unit -> Control)
  : Control =
  DockPanel().LastChildFill(true).Children(actions().DockRight(), body())

let rowActions(ctx: SectionContext, path: RowPath) : Control =
  let actions =
    row 4.0 [|
      iconButtonKey(
        Keys.planEdit,
        Icons.pencil,
        fun () -> ctx.Dispatch(BeginEdit path)
      )
      iconButtonKey(
        Keys.memoriesDiscard,
        Icons.delete,
        fun () -> ctx.Dispatch(RemoveRow path)
      )
    |]

  actions.IsVisible(ctx.Model |> Observable.map(fun m -> m.Editing)) |> ignore

  actions

let editActions(ctx: SectionContext) : Control =
  row 8.0 [|
    iconButtonKey(Keys.planDone, Icons.check, fun () -> ctx.Dispatch EndEdit)
    iconButtonKey(
      Keys.planRevert,
      Icons.undo,
      fun () -> ctx.Dispatch RevertEdit
    )
  |]

/// The read body: title, dose and notes.
let readBody(ctx: SectionContext, path: RowPath, current: Exercise) : Control =
  let details = [| doseLine current; notesLine current |] |> Array.choose id

  let lines = Array.append [| rowTitle current |] details

  rowShell (fun () -> panel 2.0 lines) (fun () -> rowActions(ctx, path))

/// The values of a range editor.
[<Struct>]
type RangeSpec = { Lo: string; Hi: string; Unit: string }

/// The two numeric boxes of a rep or duration range.
let rangeEditor(spec: RangeSpec, onValue: string -> string -> unit) : Control =
  let mutable loText = spec.Lo
  let mutable hiText = spec.Hi

  let box(initial: string, set: string -> unit) =
    entry("", initial, set).Width(48.0).MaxLength(4)

  let publish() = onValue loText hiText

  row 4.0 [|
    box(
      spec.Lo,
      fun text ->
        loText <- text
        publish()
    )
    TextBlock().Text("–")
    box(
      spec.Hi,
      fun text ->
        hiText <- text
        publish()
    )
    if spec.Unit = "" then
      Localization.textBlock Keys.planReps
    else
      TextBlock().Text(spec.Unit)
  |]

/// The dose editor: two numbers for reps, two numbers and a unit for
/// duration, a single text box when the row is not a pure dose.
let doseEditor
  (ctx: SectionContext, path: RowPath, exercise: Exercise)
  : Control =
  let dispatch(value: string) =
    ctx.Dispatch(SetField(path, RepsField, value))

  let repEditor(lo: int, hi: int) : Control =
    rangeEditor(
      {
        Lo = string lo
        Hi = string hi
        Unit = ""
      },
      fun loText hiText ->
        dispatch(if loText = hiText then loText else $"{loText}-{hiText}")
    )

  let timedEditor(lo: int, hi: int) : Control =
    let divisor = if lo % 60 = 0 && hi % 60 = 0 then 60 else 1
    let unit = if divisor = 60 then "min" else "s"

    rangeEditor(
      {
        Lo = string(lo / divisor)
        Hi = string(hi / divisor)
        Unit = unit
      },
      fun loText hiText ->
        let value =
          if loText = hiText then
            $"{loText} {unit}"
          else
            $"{loText}-{hiText} {unit}"

        dispatch value
    )

  let textEditor() : Control =
    entry("", exercise.Reps, dispatch).Width(90.0).MaxLength(40)

  match (tryParseDose exercise.Reps: Result<Dose, PrescriptionAdvisory>) with
  | Ok(Reps(lo, hi)) -> repEditor(lo, hi)
  | Ok(Timed(lo, hi)) -> timedEditor(lo, hi)
  | Error _ -> textEditor()

let editBody(ctx: SectionContext, path: RowPath, exercise: Exercise) : Control =
  let entryFor field initial =
    entry("", initial, fun text -> ctx.Dispatch(SetField(path, field, text)))

  let fields =
    row 6.0 [|
      doseEditor(ctx, path, exercise)
      (entryFor RirField exercise.Rir).Width(60.0).MaxLength(40)
      (entryFor RestField exercise.Rest).Width(90.0).MaxLength(40)
    |]

  panel 4.0 [|
    TextBlock()
      .Text($"{exercise.Circuit}{exercise.Order}  {exercise.Name}")
      .Classes("body-strong")
    fields
    (entryFor NotesField exercise.Notes).Width(320.0).MaxLength(500)
    editActions ctx
  |]

/// One exercise: prose when the page reads, its fields when the page
/// edits. The typed text lives in the boxes; the tree updates live.
let exerciseRow
  (ctx: SectionContext, path: RowPath, exercise: Exercise)
  : Control =
  let body =
    ctx.Model
    |> Observable.map(fun m ->
      if m.ActiveRow = ValueSome path then
        true, exercise
      else
        false, exerciseAt m.Plan path exercise)
    |> Observable.distinctUntilChanged
    |> Observable.map(fun (active, current) ->
      if active then
        editBody(ctx, path, current)
      else
        readBody(ctx, path, current))

  ContentControl().Content(body.ToBinding())

/// The exercises of a day grouped by circuit, in first-occurrence order.
/// The pair keeps the position in the day's list, which is the row key.
let circuitGroupsOf(day: Day) : (string * (int * Exercise) list) list =
  day.Exercises
  |> List.indexed
  |> List.groupBy(fun (_, exercise) -> exercise.Circuit)

/// The day notes, when the plan has them.
let noteBlock(note: string) : Control =
  TextBlock().Text(note).Classes("caption", "secondary")

let dayNotes(day: Day) : Control array =
  day.Notes |> List.map noteBlock |> List.toArray

/// The count badge of the day.
let dayBadgeBlock(ctx: SectionContext, day: Day) : Control =
  TextBlock()
    .Text(
      ctx.Props.Culture.Changed
      |> Observable.map(fun current -> dayBadge(current, day))
    )
    .Classes("caption", "secondary")

/// The heading of one circuit, with its lap buttons.
let circuitHeader
  (ctx: SectionContext, path: RowPath, group: string * (int * Exercise) list)
  : Control =
  let circuit, rows = group
  let firstIndex, firstExercise = List.head rows
  let firstPath = { path with Index = firstIndex }

  let laps =
    ctx.Model
    |> Observable.map(fun m -> (exerciseAt m.Plan firstPath firstExercise).Laps)
    |> Observable.distinctUntilChanged

  let heading =
    Observable.combineLatest laps ctx.Props.Culture.Changed
    |> Observable.map(fun (laps, culture) ->
      circuitHeading(culture, circuit, laps))

  let adjust(delta: int) =
    ctx.Dispatch(AdjustLaps({ path with Index = firstIndex }, delta))

  let buttons =
    row 2.0 [|
      Button(Content = "-")
        .Classes("chrome")
        .OnClickHandler(fun _ _ -> adjust -1)
      Button(Content = "+")
        .Classes("chrome")
        .OnClickHandler(fun _ _ -> adjust 1)
    |]

  buttons.IsVisible(ctx.Model |> Observable.map(fun m -> m.Editing)) |> ignore

  DockPanel()
    .LastChildFill(true)
    .Children(
      buttons.DockRight(),
      TextBlock().Text(heading).Classes("caption-strong", "secondary")
    )

/// One circuit: its header and its rows.
let circuitBlock
  (
    ctx: SectionContext,
    path: RowPath,
    indexed: int * (string * (int * Exercise) list)
  ) : Control array =
  let groupIndex, group = indexed
  let rows = group |> snd

  let header: Control array =
    let divider: Control =
      Border().Height(1.0).Background(muted).Margin(0.0, 4.0, 0.0, 4.0)

    Array.append (if groupIndex > 0 then [| divider |] else [||]) [|
      circuitHeader(ctx, path, group)
    |]

  let exercises =
    rows
    |> List.map(fun (index, exercise) ->
      exerciseRow(ctx, { path with Index = index }, exercise))
    |> List.toArray

  Array.append header exercises

/// The day card body: its notes, its count and its circuits. The hub
/// slot header carries the day's name.
let dayBody(ctx: SectionContext, path: RowPath, day: Day) : Control =
  let circuits =
    day
    |> circuitGroupsOf
    |> List.indexed
    |> List.map(fun indexed -> circuitBlock(ctx, path, indexed))
    |> List.toArray
    |> Array.concat

  panel
    8.0
    (Array.concat [| dayNotes day; [| dayBadgeBlock(ctx, day) |]; circuits |])

/// The day's slot header: its id and title.
let dayHeader(day: Day) : string =
  match day.Title with
  | Some title -> $"{day.Id} — {title}"
  | None -> day.Id

/// The variant heading: gender, frequency and title.
let variantHeadingBlock
  (ctx: SectionContext, block: Block, variant: Variant)
  : Control =
  TextBlock()
    .Text(
      ctx.Props.Culture.Changed
      |> Observable.map(fun current -> variantHeading(current, block, variant))
    )
    .Classes("body-strong")

/// The week caption: week number, plan length and mesocycle effort.
let weekCaption(ctx: SectionContext, plan: Plan, week: Week) : Control =
  let rir = tryMesocycleRir plan week.Number

  TextBlock()
    .Text(
      ctx.Props.Culture.Changed
      |> Observable.map(fun current ->
        let weekLabel =
          getf(
            current,
            Keys.reviewWeekBadge,
            [ week.Number :> obj; plan.Weeks :> obj ]
          )

        match rir with
        | Some rir -> $"{weekLabel} · RIR {rir}"
        | None -> weekLabel)
    )
    .Classes("caption-strong", "accent-text")
    .HorizontalAlignment(HorizontalAlignment.Left)

/// The days of one week as hub columns.
let daysHub(ctx: SectionContext, path: RowPath, week: Week) : Control =
  let days: HubSlot list =
    week.Days
    |> List.indexed
    |> List.map(fun (dayIndex, day) ->
      slot(dayHeader day, dayBody(ctx, { path with Day = dayIndex }, day)))

  Hub.hub {
    Title = ""
    Slots = days
    Active = ValueNone
    OnSelect = ValueNone
  }

/// The two guide lines the PWA shows after the week.
let guideLine(line: string) : Control =
  TextBlock().Text(line).Classes("caption", "secondary")

let guideTrailerBlocks(plan: Plan) : Control array =
  plan |> guideTrailer |> List.map guideLine |> List.toArray

/// One variant of the shown week: its heading, its week caption, its
/// days as hub columns and the guide trailer.
let variantSection
  (ctx: SectionContext, plan: Plan, (path, variant): RowPath * Variant)
  : Control =
  let block = plan.Blocks.[path.Block]
  let week = variant.Weeks[path.Week]

  panel
    8.0
    (Array.concat [|
      [|
        variantHeadingBlock(ctx, block, variant)
        weekCaption(ctx, plan, week)
        daysHub(ctx, path, week)
      |]
      guideTrailerBlocks plan
    |])

/// One week of the tree.
let renderWeek(ctx: SectionContext, plan: Plan, weekNumber: int) : Control =
  let sections =
    plan.Blocks
    |> List.indexed
    |> List.collect(fun (blockIndex, block) ->
      block.Variants
      |> List.indexed
      |> List.choose(fun (variantIndex, variant) ->
        variant.Weeks
        |> List.tryFindIndex(fun week -> week.Number = weekNumber)
        |> Option.map(fun weekIndex ->
          let path = {
            Block = blockIndex
            Variant = variantIndex
            Week = weekIndex
            Day = 0
            Index = 0
          }

          variantSection(ctx, plan, (path, variant)))))
    |> List.toArray

  panel 12.0 sections

/// The segmented week pivot: the active week reads accent.
let weekButton(ctx: SectionContext, weekNumber: int) : Control =
  let active = ctx.Model |> Observable.map(fun m -> m.Week = weekNumber)

  Button(
    Content =
      TextBlock()
        .Text(
          ctx.Props.Culture.Changed
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
    .OnClickHandler(fun _ _ -> ctx.Dispatch(SetWeek weekNumber))
    .Classes("chrome")

let weekSelector(ctx: SectionContext, plan: Plan) : Control =
  weekNumbersOf plan
  |> List.distinct
  |> List.map(fun weekNumber -> weekButton(ctx, weekNumber))
  |> List.toArray
  |> row 4.0

/// What the week host asks the cache to render.
[<Struct>]
type WeekRequest = {
  Week: int
  Shape: PlanShape
  Plan: Plan
}

/// The cached week content: rebuilt when the week or its shape changes.
/// A field edit keeps the shape, so the open row does not lose focus.
let weekContent
  (
    ctx: SectionContext,
    cache: System.Collections.Generic.Dictionary<int * PlanShape, Control>,
    request: WeekRequest
  ) : Control =
  match cache.TryGetValue((request.Week, request.Shape)) with
  | true, content -> content
  | _ ->
    let content = renderWeek(ctx, request.Plan, request.Week)
    cache[(request.Week, request.Shape)] <- content
    content

let weekHost
  (
    ctx: SectionContext,
    cache: System.Collections.Generic.Dictionary<int * PlanShape, Control>
  ) : Control =
  ContentControl()
    .Content(
      (ctx.Model
       |> Observable.map(fun m -> (m.Week, structureOf m.Plan), m.Plan)
       |> Observable.distinctUntilChangedKey fst
       |> Observable.map(fun ((week, shape), plan) ->
         weekContent(
           ctx,
           cache,
           {
             Week = week
             Shape = shape
             Plan = plan
           }
         )))
        .ToBinding()
    )

/// One content warning line.
let warningLine(culture: CultureInfo, warning: ContentWarning) : Control =
  TextBlock()
    .Text(warningText(culture, warning))
    .Classes("caption", "danger")
    .TextWrapping(TextWrapping.Wrap)

let warningPanel
  (culture: CultureInfo, warnings: ContentWarning list)
  : Control =
  warnings
  |> List.map(fun warning -> warningLine(culture, warning))
  |> List.toArray
  |> panel 4.0

let warningHost(ctx: SectionContext) : Control =
  let warnings =
    ctx.Model
    |> Observable.map(fun m -> m.Plan |> contentWarnings)
    |> Observable.distinctUntilChanged

  ContentControl()
    .Content(
      (Observable.combineLatest warnings ctx.Props.Culture.Changed
       |> Observable.map(fun (warnings, culture) ->
         warningPanel(culture, warnings)))
        .ToBinding()
    )

let undoButton(ctx: SectionContext) : Control =
  let button =
    iconButtonKey(Keys.planRevert, Icons.undo, fun () -> ctx.Dispatch Undo)

  button.IsEnabled(ctx.Model |> Observable.map(fun m -> m.Plan <> m.Snapshot))
  |> ignore

  button

let editButton(ctx: SectionContext) : Control =
  iconButtonKey(
    Keys.planEdit,
    Icons.pencil,
    fun () -> ctx.Dispatch(SetEditing true)
  )

let doneButton(ctx: SectionContext) : Control =
  row 8.0 [|
    iconButtonKey(
      Keys.planDone,
      Icons.check,
      fun () -> ctx.Dispatch(SetEditing false)
    )
  |]

/// The page-level edit switch. Row actions and lap buttons only appear
/// while it is on.
let editToggle(ctx: SectionContext) : Control =
  ContentControl()
    .Content(
      (ctx.Model
       |> Observable.map _.Editing
       |> Observable.distinctUntilChanged
       |> Observable.map(fun editing ->
         if editing then doneButton ctx else editButton ctx))
        .ToBinding()
    )

let topBar(ctx: SectionContext, plan: Plan) : Control =
  let actions = row 8.0 [| editToggle ctx; undoButton ctx |]

  DockPanel()
    .LastChildFill(false)
    .Children(weekSelector(ctx, plan).DockLeft(), actions.DockRight())

type PlanReview(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) as this
  =
  inherit UserControl()

  do

    let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      this.AddHandler(
        InputElement.KeyDownEvent,
        EventHandler<KeyEventArgs>(fun _ args ->
          if
            args.Key = Key.Z && args.KeyModifiers.HasFlag(KeyModifiers.Control)
          then
            dispatch Undo
            args.Handled <- true)
      )

      let ctx = {
        Props = props
        Model = model
        Dispatch = dispatch
      }

      let cache =
        System.Collections.Generic.Dictionary<int * PlanShape, Control>()

      panel 8.0 [|
        topBar(ctx, props.Parsed.Plan)
        warningHost ctx
        weekHost(ctx, cache)
      |]

    let program = Program.mkProgram init (update props onEvent) view
    base.Content <- Program.runWith props program
