module Briple.Trainer.Components.NoteSection

open System
open System.Globalization
open System.Threading
open System.Threading.Tasks

open Avalonia.Controls
open Avalonia.Controls.Templates
open IcedTasks
open IcedTasks.Polyfill.Task.Tasks
open NXUI.Extensions

open FSharp.Control.Reactive

open MVUish.Avalonia
open MVUish.Avalonia.Collections

open Serilog

open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.Theme

[<Struct>]
type Event = MemoryConfirmed of MemoryEntity

[<Struct>]
type Props = {
  Culture: CultureInfo
  Agent: IProvideAgent
  Memories: MemoryStore
  AthleteId: Guid
  /// The plan the note is observed on: its vocabulary closes the
  /// surface and its id stamps plan-scoped memories.
  Plan: Plan
  PlanId: Guid
  /// The page's token: leaving the review page cancels the reading.
  Cancel: CancellationToken
}

/// What the last reading produced, rendered next to the field.
type Status =
  | Idle
  | Reading
  | Rejected
  | Full
  | Failed of reason: string

type Model = {
  Note: string
  Busy: bool
  Proposals: (Guid * Memory) list
  Status: Status
}

/// One pass over the agent: the cap verdict or the reading.
type Reading =
  | SurfaceFull
  | Read of NoteInterpretation
  | Unreadable of exn

type Msg =
  | SetNote of string
  | InterpretRequested
  | Interpreted of Reading
  | Decided of MemoryReview.Event
  | Saved of localId: Guid * Result<MemoryEntity, exn>

/// Ten live memories is the surface cap of one plan context.
let memoryCap = 10

let init(_: Props) =
  {
    Note = ""
    Busy = false
    Proposals = []
    Status = Idle
  },
  Cmd.none

/// The live count decides before the agent is called.
let read(props: Props, note: string) : Task<Reading> = task {
  let live =
    props.Memories.Snapshot props.AthleteId
    |> List.filter(fun entity -> entity.Memory.Status <> Discarded)

  if List.length live >= memoryCap then
    return SurfaceFull
  else
    let ask: NoteAsk = {
      Targets = vocabularyOfPlan props.Plan
      Note = note
      Date = DateOnly.FromDateTime DateTime.Today
    }

    match! props.Agent.InterpretNote(ask, props.Cancel) with
    | Ok interpretation -> return Read interpretation
    | Error e -> return Unreadable e
}

/// Persists the confirmed memory; the local id travels so the update can
/// drop the proposal card when the write lands.
let saveMemory
  (props: Props)
  ((localId, memory): Guid * Memory)
  : Task<Guid * Result<MemoryEntity, exn>> = task {
  let! saved =
    props.Memories.Add(props.AthleteId, memory, ValueSome props.PlanId)

  return localId, saved
}

/// The update closes over `onEvent`: the confirmed memory is a one-shot
/// output, not a model transition.
let update (props: Props) (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | SetNote note -> { model with Note = note }, Cmd.none

  | InterpretRequested when
    not model.Busy && not(String.IsNullOrWhiteSpace model.Note)
    ->
    {
      model with
          Busy = true
          Status = Reading
    },
    Cmd.OfTask.perform read (props, model.Note.Trim()) Interpreted

  | InterpretRequested -> model, Cmd.none

  | Interpreted SurfaceFull ->
    {
      model with
          Busy = false
          Status = Full
    },
    Cmd.none

  | Interpreted(Read interpretation) ->
    // the rejections are the feedback loop: logged structured so the
    // uncovered surfaces count themselves over time
    for rejection in interpretation.Rejections do
      let motive, text =
        match rejection with
        | UnknownLabel token -> "etiqueta-desconocida", token
        | UnknownTarget text -> "objetivo-desconocido", text
        | MissingField field -> "campo-faltante", field
        | OutsideProtocol detail -> "fuera-de-protocolo", detail

      Log.Information("nota rechazada: {Motivo} {Texto}", motive, text)

    {
      model with
          Busy = false
          Status =
            if List.isEmpty interpretation.Memories then
              Rejected
            else
              Idle
          Proposals =
            interpretation.Memories
            |> List.map(fun memory -> Guid.NewGuid(), memory)
    },
    Cmd.none

  | Interpreted(Unreadable e) ->
    Log.Error(e, "note interpretation failed")

    {
      model with
          Busy = false
          Status = Failed e.Message
    },
    Cmd.none

  | Decided(MemoryReview.Confirmed(localId, memory)) ->
    let decided = confirm memory.Deltas memory

    model, Cmd.OfTask.perform (saveMemory props) (localId, decided) Saved

  | Decided(MemoryReview.Discarded localId) ->
    {
      model with
          Proposals =
            model.Proposals |> List.filter(fun (key, _) -> key <> localId)
    },
    Cmd.none

  | Saved(localId, Ok entity) ->
    onEvent(Event.MemoryConfirmed entity)

    {
      model with
          Proposals =
            model.Proposals |> List.filter(fun (key, _) -> key <> localId)
    },
    Cmd.none

  | Saved(_, Error e) ->
    Log.Error(e, "memory save failed")
    { model with Status = Failed e.Message }, Cmd.none

/// The status text and tone: resx only, rendered next to the field.
let statusText(culture: CultureInfo, status: Status) : string * bool =
  match status with
  | Idle
  | Reading -> "", false
  | Rejected ->
    getf(
      culture,
      Keys.reviewNoteRejected,
      [ get(culture, Keys.reviewNoteSurface) :> obj ]
    ),
    true
  | Full -> get(culture, Keys.reviewNoteFull), true
  | Failed reason ->
    getf(culture, Keys.reviewNoteFailed, [ reason :> obj ]), true

let view
  (props: Props)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let t key = get(props.Culture, key)

  let noteEntry = entry(t Keys.reviewNotePlaceholder, "", (SetNote >> dispatch))

  let interpret =
    let primary =
      primaryButton(
        t Keys.reviewNoteInterpret,
        (fun () -> dispatch InterpretRequested)
      )

    primary.IsEnabled(
      model
      |> Observable.map(fun m ->
        not m.Busy && not(String.IsNullOrWhiteSpace m.Note))
    )

  // one named stream, two bindings: the status reaches the text and the
  // tone (the lookup is a string format, computing it twice is free)
  let statusStream =
    model |> Observable.map(fun m -> statusText(props.Culture, m.Status))

  let status =
    TextBlock()
      .FontFamily(font)
      .Text(statusStream |> Observable.map fst)
      .Foreground(
        statusStream
        |> Observable.map(fun (_, isError) ->
          if isError then errorBrush else muted)
      )

  // the proposal cards: keyed by their local id, so a decision on one
  // card never rebuilds the others
  let proposalRow(item: Guid * Memory) : Control =
    let id, memory = item

    MemoryReview.MemoryReview(
      {
        Culture = props.Culture
        Id = id
        Memory = memory
      },
      Decided >> dispatch
    )

  let proposals =
    let template =
      FuncDataTemplate<(Guid * Memory)>((fun item _ -> proposalRow item), false)

    let list = ItemsControl().ItemTemplate(template)

    list.BindItemsSource(model |> Observable.map(fun m -> m.Proposals), fst)
    |> ignore

    list

  field(
    t Keys.reviewNoteTitle,
    fun () ->
      StackPanel()
        .Spacing(6.0)
        .Children(noteEntry, interpret, status, proposals)
  )

type NoteSection(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) as this
  =
  inherit UserControl()

  do
    let program = Program.mkProgram init (update props onEvent) (view props)

    this.Content <- Program.runWith props program
