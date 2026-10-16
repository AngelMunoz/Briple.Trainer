module Briple.Trainer.Views.Athletes

// The athlete detail page as an MVUish component: the memories and plans
// sections are bound to the model stream, and every store write asks for
// a refresh through a Cmd. No page rebuilds, no manual child mutations.

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia

open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type DetailPageProps = {
  Culture: CultureInfo
  Store: IStore
  Athlete: AthleteEntity
  Memories: MemoryEntity list
  Plans: PlanEntity list
  OnGenerate: unit -> unit
  OnReview: Guid -> unit
}

// --- the page state ---

type Model = {
  Memories: MemoryEntity list
  Plans: PlanEntity list
}

type Msg =
  | MarkAdded of target: string * label: Label
  | MemoryDecided of MemoryReview.Event
  | Refreshed of Model
  /// A store write the page does not need to react to.
  | Recorded

let init(props: DetailPageProps) =
  {
    Memories = props.Memories
    Plans = props.Plans
  },
  Cmd.none

let refresh(props: DetailPageProps) : Cmd<Msg> =
  let load() = asyncEx {
    let! memories = props.Store.ListMemories props.Athlete.Id
    let! plans = props.Store.ListPlans props.Athlete.Id

    let empty =
      function
      | Ok xs -> xs
      | Error _ -> []

    return {
      Memories = empty memories
      Plans = empty plans
    }
  }

  Cmd.OfAsync.perform load () Refreshed

let update (props: DetailPageProps) (msg: Msg) (model: Model) =
  match msg with
  | MarkAdded(target, label) ->
    let mark = {
      Target = Exercise target
      Label = label
      Date = DateOnly.FromDateTime(DateTime.Today)
    }

    let record =
      Cmd.OfTask.perform
        (fun () -> props.Store.AddMemory(props.Athlete.Id, consolidate mark))
        ()
        (fun _ -> Recorded)

    model, Cmd.batch [ record; refresh props ]

  | MemoryDecided event ->
    let idOf =
      function
      | MemoryReview.Confirmed(id, _)
      | MemoryReview.Discarded id -> id

    let persist =
      model.Memories
      |> List.tryFind(fun entity -> entity.Id = idOf event)
      |> Option.map(fun entity ->
        let decided =
          match event with
          | MemoryReview.Confirmed(_, memory) ->
            confirm memory.Deltas entity.Memory
          | MemoryReview.Discarded _ -> discard entity.Memory

        Cmd.OfTask.perform
          (fun () -> props.Store.UpdateMemory { entity with Memory = decided })
          ()
          (fun _ -> Recorded))
      |> Option.defaultValue Cmd.none

    model, Cmd.batch [ persist; refresh props ]

  | Refreshed fresh -> fresh, Cmd.none

  | Recorded -> model, Cmd.none

// --- page pieces as module functions ---

/// The quick-mark form: a target name and the four labels; the mark
/// itself is dated today and decided by the update.
let markForm(props: DetailPageProps, dispatch: Dispatch<Msg>) : Control =
  let t key = get(props.Culture, key)

  let targetRef = ref ""

  let labelRef = ref Light

  let labelFrom(picked: string) =
    match picked with
    | _ when picked = t Keys.labelHeavy -> Heavy
    | _ when picked = t Keys.labelPain -> Pain
    | _ when picked = t Keys.labelNoEquipment -> NoEquipment
    | _ -> Light

  let targetInput =
    entry(t Keys.markTarget, "", (fun text -> targetRef.Value <- text))

  let labelChips =
    multiChips(
      [
        t Keys.labelLight
        t Keys.labelHeavy
        t Keys.labelPain
        t Keys.labelNoEquipment
      ],
      [ t Keys.labelLight ],
      (fun picked -> labelRef.Value <- labelFrom picked)
    )

  let add() =
    dispatch(MarkAdded(targetRef.Value.Trim(), labelRef.Value))

  let form =
    StackPanel()
      .Spacing(6.0)
      .Children(targetInput, labelChips, button(t Keys.memoriesAddMark, add))

  field(t Keys.memoriesAddMark, (fun () -> form))

let memorySection
  (props: DetailPageProps, model: IObservable<Model>, dispatch: Dispatch<Msg>)
  : Control =
  let t key = get(props.Culture, key)

  let render(m: Model) : Control =
    let cards: Control array = [|
      for entity in m.Memories do
        if entity.Memory.Status = Proposed then
          MemoryReview.MemoryReview(
            {
              Culture = props.Culture
              Id = entity.Id
              Memory = entity.Memory
            },
            MemoryDecided >> dispatch
          )
    |]

    if Array.isEmpty cards then
      label(t Keys.memoriesEmpty)
    else
      StackPanel().Spacing(4.0).Children cards

  ContentControl().Content(model |> Observable.map(render >> box))

let planSection(props: DetailPageProps, model: IObservable<Model>) : Control =
  let t key = get(props.Culture, key)

  let render(m: Model) : Control =
    let rows: Control array = [|
      for entity in m.Plans ->
        button(entity.Title, (fun () -> props.OnReview entity.Id))
          .HorizontalContentAlignment(HorizontalAlignment.Left)
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
    |]

    if Array.isEmpty rows then
      label(t Keys.athletesEmpty)
    else
      StackPanel().Spacing(6.0).Children rows

  ContentControl().Content(model |> Observable.map(render >> box))

let profileLine(athlete: Athlete) : Control =
  TextBlock()
    .Text(
      [
        athlete.Gender |> Gender.toFileToken
        String.concat ", " athlete.Variants
        String.concat ", " athlete.Focus
      ]
      |> String.concat " · "
    )
    .Foreground(muted)
    .FontFamily(font)

// --- the page ---

let view
  (props: DetailPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let t key = get(props.Culture, key)

  ScrollViewer()
    .Content(
      StackPanel()
        .Margin(24.0)
        .Spacing(8.0)
        .Children(
          title props.Athlete.Profile.Name,
          profileLine props.Athlete.Profile,
          primaryButton(t Keys.plansGenerate, props.OnGenerate)
            .HorizontalAlignment(HorizontalAlignment.Left),
          title(t Keys.memoriesTitle),
          markForm(props, dispatch),
          memorySection(props, model, dispatch),
          title(t Keys.plansTitle),
          planSection(props, model)
        )
    )

type AthleteDetailPage(props: DetailPageProps) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram init (update props) (view props)

    this.Content <- Program.runWith props program
