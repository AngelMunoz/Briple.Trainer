module Briple.Trainer.Views.Review

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Platform.Storage
open NXUI.Extensions

open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia

open Serilog

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.Theme

[<Struct>]
type ReviewPageProps = {
  Culture: PageCulture
  Lead: Control voption
  Plans: PlanStore
  Memories: MemoryStore
  Agent: IProvideAgent
  Host: Window voption
  Entity: PlanEntity
  Gate: PageGate
}

/// The document the page shows: the stored tree and the pending edit,
/// or the parse failure that makes the plan unreadable.
type Document =
  | Broken of PlanFailure
  | Loaded of parsed: ParsedPlan * draft: Plan option

/// What the last save or export produced, kept as data so a culture
/// switch re-renders the wording in the new language.
[<Struct>]
type PageStatus =
  | NoStatus
  | Saved
  | Exported of path: string
  | Failed of actionKey: string * reason: string

type Model = {
  Entity: PlanEntity
  Baseline: string
  Document: Document
  Week: int voption
  Editing: bool
  Status: PageStatus
  Note: NoteSection.Model
}

type Msg =
  | PlanEdited of Plan
  | ViewChanged of week: int * editing: bool
  | SaveRequested
  | SaveFinished of raw: string * result: Result<unit, exn>
  | ExportRequested
  | Exported of Result<string option, exn>
  | NoteChanged of NoteSection.Model
  | NoteConfirmed of MemoryEntity
  | ReconcileRequested
  | Reconciled of Result<PlanEntity voption, exn>

let noteInitial: NoteSection.Model = {
  Note = ""
  Busy = false
  Proposals = []
  Status = NoteSection.Status.Idle
}

let init(props: ReviewPageProps) =
  let document =
    match Parse.parse props.Entity.Raw with
    | Ok parsed -> Loaded(parsed, None)
    | Error failure -> Broken failure

  {
    Entity = props.Entity
    Baseline = props.Entity.Raw
    Document = document
    Week = ValueNone
    Editing = false
    Status = PageStatus.NoStatus
    Note = noteInitial
  },
  Cmd.none

let inline isDirty(document: Document) =
  match document with
  | Loaded(_, Some _) -> true
  | _ -> false

/// The one phase rule: a running reading, pending proposals and a
/// pending draft are work leaving would lose.
let inline phaseOf(model: Model) : PagePhase =
  if model.Note.Busy then
    Working
  elif not(List.isEmpty model.Note.Proposals) || isDirty model.Document then
    UnsavedChanges
  else
    Ready

/// The raw text to persist or export: the pending tree when there is
/// one, the stored baseline otherwise.
let inline rawOf(document: Document) : string =
  match document with
  | Loaded(parsed, draft) ->
    draft
    |> Option.defaultValue parsed.Plan
    |> Briple.Trainer.Core.Plan.Writer.write
  | Broken _ -> ""

let inline statusText(culture: CultureInfo, status: PageStatus) : string =
  match status with
  | PageStatus.NoStatus -> ""
  | PageStatus.Saved -> get(culture, Keys.reviewSaved)
  | PageStatus.Exported path ->
    getf(culture, Keys.reviewExported, [ path :> obj ])
  | PageStatus.Failed(actionKey, reason) ->
    friendly(culture, get(culture, actionKey), reason)

let inline report
  (culture: CultureInfo, actionKey: string, reason: string)
  : unit =
  let action = get(culture, actionKey)
  let wording = friendly(culture, action, reason)

  Briple.Trainer.Toasts.show(action, wording, true)

/// The export picker and the write. The picker starts on the UI thread;
/// the file write is small enough to finish there.
let pickAndWrite
  (props: ReviewPageProps)
  (raw: string)
  : Async<Result<string option, exn>> = asyncEx {
  match props.Host with
  | ValueNone -> return Error(exn "export without a host window")
  | ValueSome host ->
    try
      let culture = props.Culture.Current()

      let options =
        FilePickerSaveOptions(
          Title = get(culture, Keys.reviewExport),
          SuggestedFileName = $"{props.Entity.Title}.briple",
          DefaultExtension = "briple"
        )

      let! picked = host.StorageProvider.SaveFilePickerAsync(options)

      match Option.ofObj picked with
      | None -> return Ok None
      | Some file ->
        match exportBriple(file.Path.LocalPath, raw) with
        | Ok path -> return Ok(Some path)
        | Error e -> return Error e
    with e ->
      return Error e
}

let update (props: ReviewPageProps) (msg: Msg) (model: Model) =
  match msg with
  | PlanEdited edited ->
    match model.Document with
    | Broken _ -> model, Cmd.none
    | Loaded(parsed, _) ->
      let raw = Briple.Trainer.Core.Plan.Writer.write edited

      let document =
        if raw = model.Baseline then
          Loaded({ parsed with Plan = edited }, None)
        else
          Loaded({ parsed with Plan = edited }, Some edited)

      { model with Document = document }, Cmd.none

  | ViewChanged(week, editing) ->
    {
      model with
          Week = ValueSome week
          Editing = editing
    },
    Cmd.none

  | SaveRequested ->
    match model.Document with
    | Loaded(_, Some plan) ->
      let raw = Briple.Trainer.Core.Plan.Writer.write plan

      model,
      Cmd.OfTask.perform
        (fun value -> props.Plans.UpdateRaw(props.Entity.Id, value))
        raw
        (fun result -> SaveFinished(raw, result))
    | _ -> model, Cmd.none

  | SaveFinished(raw, Ok()) ->
    let document =
      match model.Document with
      | Loaded(parsed, _) -> Loaded(parsed, None)
      | Broken failure -> Broken failure

    {
      model with
          Baseline = raw
          Document = document
          Status = PageStatus.Saved
    },
    Cmd.none

  | SaveFinished(_, Error e) ->
    let culture = props.Culture.Current()
    Log.Error(e, "plan update failed")
    report(culture, Keys.reviewSave, e.Message)

    {
      model with
          Status = PageStatus.Failed(Keys.reviewSave, e.Message)
    },
    Cmd.none

  | ExportRequested ->
    match model.Document with
    | Broken _ -> model, Cmd.none
    | Loaded _ ->
      model,
      Cmd.OfAsync.perform (pickAndWrite props) (rawOf model.Document) Exported

  | Exported(Ok(Some path)) ->
    {
      model with
          Status = PageStatus.Exported path
    },
    Cmd.none

  | Exported(Ok None) -> model, Cmd.none

  | Exported(Error e) ->
    let culture = props.Culture.Current()
    Log.Error(e, "plan export failed")
    report(culture, Keys.reviewExport, e.Message)

    {
      model with
          Status = PageStatus.Failed(Keys.reviewExport, e.Message)
    },
    Cmd.none

  | NoteChanged state -> { model with Note = state }, Cmd.none

  | NoteConfirmed _ -> model, Cmd.none

  | ReconcileRequested ->
    model,
    Cmd.OfTask.perform
      (fun id -> props.Plans.Find id)
      props.Entity.Id
      Reconciled

  | Reconciled(Ok(ValueSome entity)) ->
    // a clean page adopts the stored tree; a pending draft keeps its
    // baseline and is never overwritten in silence
    if isDirty model.Document then
      if entity.Raw <> model.Baseline then
        Log.Warning(
          "plan {PlanId} changed in the store while the review had a draft",
          entity.Id
        )

      { model with Entity = entity }, Cmd.none
    else
      let document =
        match Parse.parse entity.Raw with
        | Ok parsed -> Loaded(parsed, None)
        | Error failure -> Broken failure

      {
        model with
            Entity = entity
            Baseline = entity.Raw
            Document = document
      },
      Cmd.none

  | Reconciled(Ok ValueNone) -> model, Cmd.none

  | Reconciled(Error e) ->
    Log.Error(e, "plan reload failed")
    model, Cmd.none

let invalidBody(props: ReviewPageProps, failure: PlanFailure) : Control =
  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.reviewTitle
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          glyph(Icons.alertCircle, 24.0, errorBrush),
          TextBlock()
            .Text(
              props.Culture.Changed
              |> Observable.map(fun current ->
                getf(
                  current,
                  Keys.reviewInvalid,
                  [ Parse.describe failure :> obj ]
                ))
            )
            .Classes("body", "danger")
        )
    Measure = 760.0
  }

/// The page at one culture, over the current model.
let view
  (props: ReviewPageProps)
  (notes: Threading.CancellationToken)
  (current: Model)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  match current.Document with
  | Broken failure -> invalidBody(props, failure)
  | Loaded(parsed, draft) ->
    let plan = draft |> Option.defaultValue parsed.Plan
    let dirty = model |> Observable.map(fun m -> isDirty m.Document)

    let save =
      let button =
        iconPrimaryButtonKey(
          Keys.reviewSave,
          Icons.contentSave,
          (fun () -> dispatch SaveRequested)
        )

      button.IsEnabled(dirty)

    let export =
      iconCommandKey(
        Keys.reviewExport,
        Icons.download,
        (fun () -> dispatch ExportRequested)
      )

    let status =
      TextBlock()
        .Text(
          Observable.combineLatest model props.Culture.Changed
          |> Observable.map(fun (m, culture) -> statusText(culture, m.Status))
        )
        .Classes("caption", "secondary")

    let pending =
      TextBlock()
        .Text(Localization.text Keys.reviewUnsaved)
        .Classes("caption", "secondary")
        .IsVisible(dirty)

    let review =
      PlanReview.PlanReview(
        {
          Culture = props.Culture
          Parsed = { parsed with Plan = plan }
          Week = current.Week
          Editing = current.Editing
        },
        function
        | PlanReview.PlanChanged edited -> dispatch(PlanEdited edited)
        | PlanReview.ViewChanged(week, editing) ->
          dispatch(ViewChanged(week, editing))
      )

    let notes =
      NoteSection.NoteSection(
        {
          Culture = props.Culture
          Agent = props.Agent
          Memories = props.Memories
          AthleteId = props.Entity.AthleteId
          Plan = plan
          PlanId = props.Entity.Id
          Cancel = notes
          Initial = current.Note
          OnState = NoteChanged >> dispatch
        },
        fun _ -> ()
      )

    fluentPage {
      Lead = props.Lead
      Title = pageTitleKey Keys.reviewTitle
      Commands = [ export; save ]
      Footer = noFooter
      Body = StackPanel().Spacing(8.0).Children(status, pending, review, notes)
      Measure = 760.0
    }

type ReviewPage(props: ReviewPageProps) as this =
  inherit UserControl()

  do
    // the reading's token lives with the page; a confirmed leave asks
    // the page to stop the read through the gate's command channel
    let notesCts = new Threading.CancellationTokenSource()

    let subscribe(_: Model) : Sub<Msg> =
      fun dispatch ->
        Page.listenGate {
          Control = this
          Gate = props.Gate
          OnShown = fun () -> dispatch ReconcileRequested
          OnCancel = fun () -> notesCts.Cancel()
        }

    let render (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      Page.trackPhase(this, props.Gate, model |> Observable.map phaseOf)

      ContentControl()
        .Content(
          (model
           |> Observable.take 1
           |> Observable.map(fun current ->
             view props notesCts.Token current model dispatch))
            .ToBinding()
        )

    let program =
      Program.mkProgram init (update props) render
      |> Program.withSubscription subscribe

    this.Content <- Program.runWith props program
