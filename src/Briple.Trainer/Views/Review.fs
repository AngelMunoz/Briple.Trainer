module Briple.Trainer.Views.Review

open System
open System.Globalization
open System.Reactive.Subjects

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform.Storage
open NXUI.Extensions

open FSharp.Control.Reactive
open IcedTasks
open Microsoft.Extensions.Logging
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
type ReviewEnv = {
  Plans: PlanStore
  Memories: MemoryStore
  Agent: IProvideAgent
  Host: Window voption
  Logger: Microsoft.Extensions.Logging.ILogger
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

[<Struct>]
type ReviewPageProps = {
  Lead: (unit -> Control) voption
  Entity: PlanEntity
  Gate: PageGate
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
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
  Culture: CultureInfo
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
  | CultureChanged of CultureInfo
  | ReconcileRequested
  | Reconciled of Result<PlanEntity voption, exn>

let noteInitial: NoteSection.Model = {
  Note = ""
  Busy = false
  Saving = false
  Proposals = []
  Status = NoteSection.Status.Idle
  Culture = CultureInfo.InvariantCulture
}

[<Struct>]
type Context = {
  Env: ReviewEnv
  Props: ReviewPageProps
}

let init(ctx: Context) =
  let props = ctx.Props

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
    Culture = ctx.Env.Culture
  },
  Cmd.none

let inline isDirty(document: Document) =
  match document with
  | Loaded(_, Some _) -> true
  | _ -> false

/// The one phase rule: a running reading, pending proposals and a
/// pending draft are work leaving would lose.
let inline phaseOf(model: Model) : PagePhase =
  if model.Note.Busy || model.Note.Saving then
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
  (ctx: Context)
  (culture: CultureInfo, actionKey: string, reason: string)
  : Cmd<Msg> =
  let action = get(culture, actionKey)
  let wording = friendly(culture, action, reason)

  Cmd.ofAction(fun () -> ctx.Env.Toasts.Show(action, wording, true))

/// The export picker and the write. The picker starts on the UI thread;
/// the file write is small enough to finish there.
let pickAndWrite
  (ctx: Context)
  (raw: string)
  : Async<Result<string option, exn>> = asyncEx {
  let props = ctx.Props

  match ctx.Env.Host with
  | ValueNone -> return Error(exn "export without a host window")
  | ValueSome host ->
    try
      let culture = ctx.Env.Culture

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

let update (ctx: Context) (msg: Msg) (model: Model) =
  let props = ctx.Props

  match msg with
  | CultureChanged culture -> { model with Culture = culture }, Cmd.none
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
        (fun value -> ctx.Env.Plans.UpdateRaw(props.Entity.Id, value))
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
    let culture = model.Culture

    let log =
      Cmd.ofAction(fun () -> ctx.Env.Logger.LogError(e, "plan update failed"))

    {
      model with
          Status = PageStatus.Failed(Keys.reviewSave, e.Message)
    },
    Cmd.batch [ log; report ctx (culture, Keys.reviewSave, e.Message) ]

  | ExportRequested ->
    match model.Document with
    | Broken _ -> model, Cmd.none
    | Loaded _ ->
      model,
      Cmd.OfAsync.perform (pickAndWrite ctx) (rawOf model.Document) Exported

  | Exported(Ok(Some path)) ->
    {
      model with
          Status = PageStatus.Exported path
    },
    Cmd.none

  | Exported(Ok None) -> model, Cmd.none

  | Exported(Error e) ->
    let culture = model.Culture

    let log =
      Cmd.ofAction(fun () -> ctx.Env.Logger.LogError(e, "plan export failed"))

    {
      model with
          Status = PageStatus.Failed(Keys.reviewExport, e.Message)
    },
    Cmd.batch [ log; report ctx (culture, Keys.reviewExport, e.Message) ]

  | NoteChanged state -> { model with Note = state }, Cmd.none

  | NoteConfirmed _ -> model, Cmd.none

  | ReconcileRequested ->
    model,
    Cmd.OfTask.perform
      (fun id -> ctx.Env.Plans.Find id)
      props.Entity.Id
      Reconciled

  | Reconciled(Ok(ValueSome entity)) ->
    // a clean page adopts the stored tree; a pending draft keeps its
    // baseline and is never overwritten in silence
    if isDirty model.Document then
      let warning =
        if entity.Raw <> model.Baseline then
          Cmd.ofAction(fun () ->
            ctx.Env.Logger.LogWarning(
              "plan {PlanId} changed in the store while the review had a draft",
              entity.Id
            ))
        else
          Cmd.none

      { model with Entity = entity }, warning
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
    model,
    Cmd.ofAction(fun () -> ctx.Env.Logger.LogError(e, "plan reload failed"))

/// The page header: the plan title with the review section below it.
let reviewHeader(props: ReviewPageProps, model: IObservable<Model>) : Control =
  StackPanel()
    .Spacing(2.0)
    .Children(
      pageTitle props.Entity.Title,
      TextBlock()
        .Text(
          model
          |> Observable.map(fun current ->
            get(current.Culture, Keys.reviewTitle))
        )
        .Classes("caption", "secondary")
    )

let invalidBody
  (props: ReviewPageProps, model: IObservable<Model>, failure: PlanFailure)
  : Control =
  fluentPage {
    Lead = props.Lead
    Title = reviewHeader(props, model)
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          glyph(Icons.alertCircle, 24.0, errorBrush),
          TextBlock()
            .Text(
              model
              |> Observable.map(fun current ->
                getf(
                  current.Culture,
                  Keys.reviewInvalid,
                  [ Parse.describe failure :> obj ]
                ))
            )
            .Classes("body", "danger")
        )
  }

/// Splits a "#x key | body" extra into its key and body.
let splitExtra(text: string) : string * string =
  match text.IndexOf '|' with
  | -1 -> "", text.Trim()
  | index -> text.Substring(0, index).Trim(), text.Substring(index + 1).Trim()

/// The extras the day card does not show: everything except the two
/// trailer keys and the mesocycle lines already in the week badge. A
/// bare extra has no heading of its own.
let guideGroups(plan: Plan) : (string option * string list) list =
  plan.Extras
  |> List.map splitExtra
  |> List.filter(fun (key, _) ->
    key <> "mesociclo" && key <> "descansos" && key <> "regla-circuito")
  |> List.groupBy fst
  |> List.map(fun (key, rows) ->
    (if key = "" then None else Some key), rows |> List.map snd)

/// The full plan guide, one heading per key and the body lines below.
let guideSection(plan: Plan) : Control =
  let groups = guideGroups plan

  if List.isEmpty groups then
    StackPanel()
  else
    let children: Control array = [|
      yield sectionTitleKey Keys.planGuide

      for key, lines in groups do
        match key with
        | Some key -> yield TextBlock().Text(key).Classes("body-strong")
        | None -> ()

        for line in lines do
          yield
            TextBlock()
              .Text(line)
              .Classes("caption", "secondary")
              .TextWrapping(TextWrapping.Wrap)
    |]

    StackPanel().Spacing(8.0).Children(children)

/// The page at one culture, over the current model.
let view
  (ctx: Context)
  (notes: Threading.CancellationToken)
  (current: Model)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let props = ctx.Props

  match current.Document with
  | Broken failure -> invalidBody(props, model, failure)
  | Loaded(parsed, draft) ->
    let plan = draft |> Option.defaultValue parsed.Plan

    // The note asks for the vocabulary at the time of the reading, not
    // the tree this view was built from. The subject is the page-model
    // slice: the value now, read on demand.
    let latestPlan = new BehaviorSubject<Plan>(plan)

    let planSubscription =
      model
      |> Observable.map(fun current ->
        match current.Document with
        | Loaded(parsed, draft) -> draft |> Option.defaultValue parsed.Plan
        | Broken _ -> plan)
      |> Observable.subscribe(fun value -> latestPlan.OnNext value)

    props.Lifetime.AddDisposable planSubscription
    props.Lifetime.AddDisposable latestPlan

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
        .Text(model |> Observable.map(fun m -> statusText(m.Culture, m.Status)))
        .Classes("caption", "secondary")

    let pending =
      TextBlock()
        .Text(Localization.text Keys.reviewUnsaved)
        .Classes("caption", "secondary")
        .IsVisible(dirty)

    let culture = model |> Observable.map _.Culture

    let planEnv: PlanReview.PlanReviewEnv = {
      Culture = current.Culture
      CultureChanged = culture
    }

    let review =
      PlanReview.PlanReview(
        planEnv,
        {
          Parsed = { parsed with Plan = plan }
          Week = current.Week
          Editing = current.Editing
        },
        function
        | PlanReview.PlanChanged edited -> dispatch(PlanEdited edited)
        | PlanReview.ViewChanged(week, editing) ->
          dispatch(ViewChanged(week, editing))
      )

    let noteEnv: NoteSection.NoteEnv = {
      Agent = ctx.Env.Agent
      Memories = ctx.Env.Memories
      Logger = ctx.Env.Logger
      Culture = current.Culture
      CultureChanged = model |> Observable.map _.Culture
    }

    let notes =
      NoteSection.NoteSection(
        noteEnv,
        {
          AthleteId = props.Entity.AthleteId
          Plan = fun () -> latestPlan.Value
          PlanId = props.Entity.Id
          Cancel = notes
          Initial = current.Note
          OnState = NoteChanged >> dispatch
        },
        fun _ -> ()
      )

    fluentPage {
      Lead = props.Lead
      Title = reviewHeader(props, model)
      Commands = [ export; save ]
      Footer = noFooter
      Body =
        StackPanel()
          .Spacing(8.0)
          .Children(status, pending, review, guideSection plan, notes)
    }

type ReviewPage(env: ReviewEnv, props: ReviewPageProps) as this =
  inherit UserControl()

  do
    let ctx = { Env = env; Props = props }

    // the reading's token lives with the page; a confirmed leave asks
    // the page to stop the read through the gate's command channel
    let notesCts = new Threading.CancellationTokenSource()

    let subscribe(_: Model) : Sub<Msg> =
      Sub.batch [
        Sub.ofId "gate" (fun dispatch ->
          Page.listenGate {
            Control = this
            Gate = props.Gate
            OnShown = fun () -> dispatch ReconcileRequested
            OnCancel = fun () -> notesCts.Cancel()
          })
        Sub.fromObservable
          "culture"
          (env.CultureChanged |> Observable.map CultureChanged)
      ]

    let render (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      Page.trackPhase(this, props.Gate, model |> Observable.map phaseOf)

      ContentControl()
        .Content(
          (model
           |> Observable.take 1
           |> Observable.map(fun current ->
             view ctx notesCts.Token current model dispatch))
            .ToBinding()
        )

    let program =
      Program.mkProgram
        (fun (_: ReviewPageProps) -> init ctx)
        (update ctx)
        render
      |> Program.withSubscription subscribe

    let root, lifetime = Program.runWithLifecycle props program

    this.Content <- root
    props.Lifetime.AddDisposable lifetime
