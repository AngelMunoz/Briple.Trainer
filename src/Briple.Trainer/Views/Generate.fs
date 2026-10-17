module Briple.Trainer.Views.Generate

open System
open System.Globalization
open System.Threading

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia

open Serilog

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator
open Briple.Trainer.Core.Wizard
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type WizardPageProps = {
  Culture: PageCulture
  Lead: Control voption
  Store: IStore
  Memories: MemoryStore
  Plans: PlanStore
  Agent: IProvideAgent
  Athlete: AthleteEntity
  Confirmed: Memory list
  Proposals: MemoryEntity list
  Previous: PlanEntity voption
  Gate: PageGate
}

type Outcome =
  | PlanFinished of Guid
  | WizardCancelled

type Draft = WizardBrief.BriefDraft

type Section =
  | MemoriesSection
  | BriefSection

/// Why a run stopped, kept as data so a culture switch re-renders the
/// reasons in the new language.
type Failure =
  | RejectedPlan of Violation list
  | Unavailable of reason: string

type Phase =
  | Editing
  | Generating
  | Failed of failure: Failure * last: Draft voption
  | Done of plan: Plan * advisories: Violation list * kind: string

type Model = {
  Section: Section
  Phase: Phase
  Decided: Guid list
  Confirmed: (Guid * Memory) list
  Brief: Draft
  Last: Draft voption
  Token: CancellationTokenSource voption
}

type Msg =
  | MemoryDecided of MemoryReview.Event
  | GoSection of Section
  | BriefChanged of Draft
  | GenerateRequested
  | GenerationDone of Result<Run, string> * kind: string
  | PlanSaved of Result<PlanEntity, exn>
  | ConfirmPlan
  | Retry
  | DiscardRequested
  | Discarded
  | Ignored
  | Exit
  | Finished of Guid
  | Left
  | RunAbandoned
  | Shown
  | Recorded

// --- pure request assembly ---

[<return: Struct>]
let (|AdjustmentBase|_|)(previous: PlanEntity voption) =
  previous
  |> ValueOption.bind(fun entity ->
    match Briple.Trainer.Core.Plan.Parser.Parse.parse entity.Raw with
    | Ok parsed -> ValueSome parsed.Plan
    | _ -> ValueNone)

[<Struct>]
type RequestParts = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  Previous: PlanEntity voption
}

let requestOf(parts: RequestParts) : Generation =
  match parts.Previous with
  | AdjustmentBase plan ->
    {
      Athlete = parts.Athlete
      Memories = parts.Memories
      Weeks = parts.Weeks
      Brief = parts.Brief
      PreviousPlan = plan
    }
    |> Generation.PlanAdjustment
  | _ ->
    {
      Athlete = parts.Athlete
      Memories = parts.Memories
      Weeks = parts.Weeks
      Brief = parts.Brief
    }
    |> Generation.NewPlan

let inline kindOf kind =
  match kind with
  | Generation.NewPlan _ -> "new"
  | Generation.PlanAdjustment _ -> "adjust"

let inline briefOf(draft: Draft) : Brief = {
  Goal = draft.Goal
  Frequency = draft.Frequency
  Emphasis = draft.Emphasis
  Adjustments = []
}

let genderName(culture: CultureInfo, gender: Gender) =
  match gender with
  | Male -> get(culture, Keys.genderMale)
  | Female -> get(culture, Keys.genderFemale)

let describeDataFailure (culture: CultureInfo) (failure: DataFailure) : string =
  match failure with
  | UnreadableJson message ->
    getf(culture, Keys.dataUnreadableJson, [ message :> obj ])
  | BadGender raw -> getf(culture, Keys.dataBadGender, [ raw :> obj ])
  | MissingTitle -> get(culture, Keys.dataMissingTitle)
  | BadWeeks value -> getf(culture, Keys.dataBadWeeks, [ value :> obj ])
  | EmptyVariantId path ->
    getf(culture, Keys.dataEmptyVariantId, [ path :> obj ])
  | MissingWeekNumber path ->
    getf(culture, Keys.dataMissingWeekNumber, [ path :> obj ])
  | MissingOrder path -> getf(culture, Keys.dataMissingOrder, [ path :> obj ])
  | UnknownDay(path, raw) ->
    getf(culture, Keys.dataUnknownDay, [ path :> obj; raw :> obj ])
  | EmptyExerciseName path ->
    getf(culture, Keys.dataEmptyExerciseName, [ path :> obj ])

let describeAdvisory
  (culture: CultureInfo)
  (advisory: PrescriptionAdvisory)
  : string =
  match advisory with
  | NotPureRange raw -> getf(culture, Keys.advisoryNotPureRange, [ raw :> obj ])
  | UnknownUnit raw -> getf(culture, Keys.advisoryUnknownUnit, [ raw :> obj ])
  | MultipleExercises name ->
    getf(culture, Keys.advisoryMultipleExercises, [ name :> obj ])
  | RepsWithoutEffort name ->
    getf(culture, Keys.advisoryRepsWithoutEffort, [ name :> obj ])
  | TimedWithRir name ->
    getf(culture, Keys.advisoryTimedWithRir, [ name :> obj ])

let describeViolation(culture: CultureInfo, violation: Violation) : string =
  match violation with
  | GenderMismatch(expected, found) ->
    getf(
      culture,
      Keys.violationGenderMismatch,
      [ genderName(culture, expected) :> obj; found :> obj ]
    )
  | WrongWeeks(expected, found) ->
    getf(culture, Keys.violationWeeks, [ expected :> obj; found :> obj ])
  | WrongVariants(expected, found) ->
    let e = String.concat ", " expected
    let f = String.concat ", " found

    getf(culture, Keys.violationVariants, [ e :> obj; f :> obj ])
  | BadVariantWeeks(variant, found, expected) ->
    let weeks = found |> List.map string |> String.concat ", "

    getf(
      culture,
      Keys.violationVariantWeeks,
      [ variant :> obj; weeks :> obj; expected :> obj ]
    )
  | WrongFrequency(variant, week, found, expected) ->
    getf(
      culture,
      Keys.violationFrequency,
      [ variant :> obj; found :> obj; week :> obj; expected :> obj ]
    )
  | WrongRir(week, found, expected) ->
    getf(
      culture,
      Keys.violationRir,
      [ week :> obj; found :> obj; expected :> obj ]
    )
  | MissingRir(week, expected) ->
    getf(culture, Keys.violationMissingRir, [ week :> obj; expected :> obj ])
  | CountChanged(variant, expected, found) ->
    getf(
      culture,
      Keys.violationCount,
      [ variant :> obj; expected :> obj; found :> obj ]
    )
  | BrokenFormat failure ->
    Briple.Trainer.Core.Plan.Parser.Parse.describe failure
  | FormatWarning warning ->
    getf(
      culture,
      Keys.violationFormatWarning,
      [ warning.Line :> obj; warning.Message :> obj ]
    )
  | Data failures ->
    failures |> List.map(describeDataFailure culture) |> String.concat "; "
  | Prescription advisory -> describeAdvisory culture advisory

let inline callOf(agent: IProvideAgent, ct: CancellationToken) =
  function
  | Generation.NewPlan request -> agent.GenerateNew(request, ct)
  | Generation.PlanAdjustment request -> agent.GenerateAdjustment(request, ct)

let updateMemory (store: MemoryStore) (entity: MemoryEntity) =
  store.Update entity

let addRequest
  (store: IStore)
  (athleteId: Guid, kind: string, payload: string)
  =
  store.AddRequest(athleteId, kind, payload)

// --- the page state machine ---

let init(props: WizardPageProps) =
  {
    Section = MemoriesSection
    Phase = Editing
    Decided = []
    Confirmed = []
    Brief =
      WizardBrief.defaultDraft(props.Athlete.Profile, props.Previous.IsSome)
    Last = ValueNone
    Token = ValueNone
  },
  Cmd.none

let inline resolved(props: WizardPageProps, model: Model) =
  List.length model.Decided = List.length props.Proposals

let inline phaseOf(model: Model) : PagePhase =
  match model.Phase with
  | Generating -> Working
  | Done _ -> UnsavedChanges
  | Editing
  | Failed _ -> Ready

let sectionIndex section =
  match section with
  | MemoriesSection -> 0
  | BriefSection -> 1

let sectionOfIndex index =
  if index = 0 then MemoriesSection else BriefSection

let cancel(token: CancellationTokenSource voption) =
  token
  |> ValueOption.iter(fun source ->
    source.Cancel()
    source.Dispose())

let requestFor(props: WizardPageProps, model: Model, draft: Draft) =
  requestOf {
    Athlete = props.Athlete.Profile
    Memories = props.Confirmed @ (model.Confirmed |> List.map snd)
    Weeks = draft.Weeks
    Brief = briefOf draft
    Previous =
      match props.Previous, draft.Mode with
      | ValueSome entity, WizardBrief.Mode.Adjust -> ValueSome entity
      | _ -> ValueNone
  }

/// One run, one CTS: leaving the wizard or a new run cancels it.
let startRun
  (props: WizardPageProps, model: Model, draft: Draft)
  : Model * Cmd<Msg> =
  cancel model.Token

  let request = requestFor(props, model, draft)
  let kind = kindOf request
  let cts = new CancellationTokenSource()

  let record =
    Cmd.OfTask.perform
      (addRequest props.Store)
      (props.Athlete.Id,
       kind,
       $"weeks={draft.Weeks};freq={VariantFrequency.toFileToken draft.Frequency}")
      (fun _ -> Recorded)

  let run =
    Cmd.OfTask.perform (callOf(props.Agent, cts.Token)) request (fun result ->
      GenerationDone(Result.mapError (fun (e: exn) -> e.Message) result, kind))

  {
    model with
        Phase = Generating
        Brief = draft
        Last = ValueSome draft
        Token = ValueSome cts
  },
  Cmd.batch [ record; run ]

let update
  (props: WizardPageProps)
  (onOutcome: Outcome -> unit)
  (msg: Msg)
  (model: Model)
  =
  match msg with
  | MemoryDecided(MemoryReview.Event.Confirmed(id, memory)) ->
    let decided = {
      model with
          Decided =
            if List.contains id model.Decided then
              model.Decided
            else
              id :: model.Decided
          Confirmed =
            (id, memory)
            :: List.filter (fun (key, _) -> key <> id) model.Confirmed
    }

    let persist =
      props.Proposals
      |> List.tryFind(fun entity -> entity.Id = id)
      |> Option.map(fun entity ->
        Cmd.OfTask.perform
          (updateMemory props.Memories)
          {
            entity with
                Memory = confirm memory.Deltas entity.Memory
          }
          (fun _ -> Recorded))
      |> Option.defaultValue Cmd.none

    decided, persist

  | MemoryDecided(MemoryReview.Event.Discarded id) ->
    let decided = {
      model with
          Decided =
            if List.contains id model.Decided then
              model.Decided
            else
              id :: model.Decided
          Confirmed = List.filter (fun (key, _) -> key <> id) model.Confirmed
    }

    let persist =
      props.Proposals
      |> List.tryFind(fun entity -> entity.Id = id)
      |> Option.map(fun entity ->
        Cmd.OfTask.perform
          (updateMemory props.Memories)
          {
            entity with
                Memory = discard entity.Memory
          }
          (fun _ -> Recorded))
      |> Option.defaultValue Cmd.none

    decided, persist

  | GoSection section ->
    match model.Phase with
    | Editing -> { model with Section = section }, Cmd.none
    | _ -> model, Cmd.none

  | BriefChanged draft -> { model with Brief = draft }, Cmd.none

  | GenerateRequested when model.Phase = Editing && resolved(props, model) ->
    startRun(props, model, model.Brief)

  | GenerateRequested -> model, Cmd.none

  | Retry ->
    match model.Phase, model.Last with
    | Failed _, ValueSome draft -> startRun(props, model, draft)
    | _ -> model, Cmd.none

  | GenerationDone(Ok run, kind) ->
    model.Token |> ValueOption.iter(fun source -> source.Dispose())

    match run.Verdict with
    | Rejected violations ->
      {
        model with
            Phase = Failed(RejectedPlan violations, ValueSome model.Brief)
            Token = ValueNone
      },
      Cmd.none
    | Valid(plan, advisories) ->
      {
        model with
            Phase = Done(plan, advisories, kind)
            Token = ValueNone
      },
      Cmd.none

  | GenerationDone(Error reason, _) ->
    model.Token |> ValueOption.iter(fun source -> source.Dispose())

    {
      model with
          Phase = Failed(Unavailable reason, ValueSome model.Brief)
          Token = ValueNone
    },
    Cmd.none

  | ConfirmPlan ->
    match model.Phase with
    | Done(plan, _, kind) ->
      let insert = {
        AthleteId = props.Athlete.Id
        Kind = kind
        Counts = planCounts props.Athlete.Profile plan
      }

      model, Cmd.OfTask.perform props.Plans.Add (insert, plan) PlanSaved
    | _ -> model, Cmd.none

  // the phase must reach Ready before the navigation asks the guard:
  // the message runs after the state update
  | PlanSaved(Ok entity) ->
    cancel model.Token

    {
      model with
          Phase = Editing
          Token = ValueNone
    },
    Cmd.ofMsg(Finished entity.Id)

  | Finished entityId ->
    onOutcome(PlanFinished entityId)
    model, Cmd.none

  | PlanSaved(Error e) ->
    let culture = props.Culture.Current()
    Log.Error(e, "plan save failed")

    let action = get(culture, Keys.reviewSave)

    Briple.Trainer.Toasts.show(
      action,
      friendly(culture, action, e.Message),
      true
    )

    model, Cmd.none

  | DiscardRequested ->
    match model.Phase with
    | Done _ ->
      let culture = props.Culture.Current()

      let ask =
        Briple.Trainer.Dialogs.confirmAsync {
          Title = get(culture, Keys.wizardDiscardTitle)
          Body = get(culture, Keys.wizardDiscardBody)
          Accept = get(culture, Keys.wizardDiscard)
          Cancel = get(culture, Keys.commonCancel)
        }

      model,
      Cmd.OfAsync.perform (fun () -> ask) () (fun sure ->
        if sure then Discarded else Ignored)
    | _ -> model, Cmd.none

  | Discarded ->
    cancel model.Token

    {
      model with
          Phase = Editing
          Token = ValueNone
    },
    Cmd.ofMsg Left

  | Ignored -> model, Cmd.none

  | Exit ->
    cancel model.Token

    {
      model with
          Phase = Editing
          Token = ValueNone
    },
    Cmd.ofMsg Left

  | Left ->
    onOutcome WizardCancelled
    model, Cmd.none

  | RunAbandoned ->
    cancel model.Token

    {
      model with
          Phase = Editing
          Token = ValueNone
    },
    Cmd.none

  | Shown ->
    // a fresh visit, even when the router served a cached page
    cancel model.Token

    {
      Section = MemoriesSection
      Phase = Editing
      Decided = []
      Confirmed = []
      Brief =
        WizardBrief.defaultDraft(props.Athlete.Profile, props.Previous.IsSome)
      Last = ValueNone
      Token = ValueNone
    },
    Cmd.none

  | Recorded -> model, Cmd.none

// --- views ---

let inline statusLine(text: string, isError: bool) : Control =
  let tone = if isError then "danger" else "secondary"

  TextBlock().Text(text).Classes("body", tone).TextWrapping(TextWrapping.Wrap)

let inline linesPanel(lines: Control array) : Control =
  StackPanel().Spacing(4.0).Children(lines)

let inline stepCaption(culture: CultureInfo, number: int) : Control =
  label(getf(culture, Keys.wizardStepOf, [ number :> obj; 2 :> obj ]))

let memoriesColumn
  (
    props: WizardPageProps,
    model: IObservable<Model>,
    culture: CultureInfo,
    dispatch: Dispatch<Msg>
  ) : Control =
  let t key = get(culture, key)

  let cards: Control array = [|
    for entity in props.Proposals ->
      MemoryReview.MemoryReview(
        {
          Culture = culture
          Id = entity.Id
          Memory = entity.Memory
        },
        MemoryDecided >> dispatch
      )
  |]

  // the adjustments that were already confirmed when the wizard
  // opened; the cards below carry the decisions of this visit
  let confirmedRows =
    props.Confirmed
    |> List.filter(fun memory -> not(String.IsNullOrWhiteSpace memory.Summary))
    |> List.map(fun memory -> label memory.Summary)
    |> Array.ofList
    |> linesPanel

  let next =
    let button =
      iconPrimaryButton(
        t Keys.wizardNext,
        Icons.chevronRight,
        (fun () -> dispatch(GoSection BriefSection))
      )

    button.IsEnabled(model |> Observable.map(fun m -> resolved(props, m)))

  StackPanel()
    .Spacing(8.0)
    .Children(
      stepCaption(culture, 1),
      (if Array.isEmpty cards then
         label(t Keys.memoriesEmpty)
       else
         linesPanel cards),
      (if List.isEmpty props.Confirmed then
         StackPanel() :> Control
       else
         sectionTitle(t Keys.wizardApplies)),
      confirmedRows,
      next.HorizontalAlignment(HorizontalAlignment.Left)
    )

let briefColumn
  (
    props: WizardPageProps,
    draft: Draft,
    model: IObservable<Model>,
    culture: CultureInfo,
    dispatch: Dispatch<Msg>
  ) : Control =
  let t key = get(culture, key)

  let brief =
    WizardBrief.WizardBrief(
      {
        Culture = culture
        Athlete = props.Athlete.Profile
        HasPrevious = props.Previous.IsSome
        Draft = draft
        OnChanged = BriefChanged >> dispatch
      }
    )

  let generate =
    let button =
      iconPrimaryButton(
        t Keys.wizardGenerate,
        Icons.play,
        (fun () -> dispatch GenerateRequested)
      )

    button.IsEnabled(model |> Observable.map(fun m -> resolved(props, m)))

  StackPanel()
    .Spacing(8.0)
    .Children(
      stepCaption(culture, 2),
      brief,
      generate.HorizontalAlignment(HorizontalAlignment.Left)
    )

let actionRow(actions: Control list) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .HorizontalAlignment(HorizontalAlignment.Right)
    .Children(List.toArray actions)

let editingView
  (
    props: WizardPageProps,
    current: Model,
    model: IObservable<Model>,
    culture: CultureInfo,
    dispatch: Dispatch<Msg>
  ) : Control =
  let t key = get(culture, key)

  let hub =
    Hub.hub {
      Title = ""
      Slots = [
        Hub.slot(
          t Keys.wizardStepMemories,
          memoriesColumn(props, model, culture, dispatch)
        )
        Hub.slot(
          t Keys.wizardStepBrief,
          briefColumn(props, current.Brief, model, culture, dispatch)
        )
      ]
      Active =
        ValueSome(model |> Observable.map(fun m -> sectionIndex m.Section))
      OnSelect =
        ValueSome(fun index -> dispatch(GoSection(sectionOfIndex index)))
    }

  fluentPage {
    Lead = props.Lead
    Title = t Keys.wizardTitle
    Commands = []
    Footer = noFooter
    Body = hub
    Measure = 760.0
  }

let generatingView
  (props: WizardPageProps, culture: CultureInfo, dispatch: Dispatch<Msg>)
  : Control =
  let t key = get(culture, key)

  let cancel =
    iconButton(
      t Keys.wizardCancelGeneration,
      Icons.stop,
      (fun () -> dispatch Exit)
    )

  fluentPage {
    Lead = props.Lead
    Title = t Keys.wizardTitle
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(16.0)
        .Margin(0.0, 32.0, 0.0, 32.0)
        .Children(
          ProgressBar(
            IsIndeterminate = true,
            Maximum = 100.0,
            Width = 240.0,
            HorizontalAlignment = HorizontalAlignment.Center
          ),
          statusLine(t Keys.wizardGenerating, false)
            .HorizontalAlignment(HorizontalAlignment.Center),
          actionRow [ cancel ]
        )
    Measure = 560.0
  }

let failedView
  (
    props: WizardPageProps,
    failure: Failure,
    last: Draft voption,
    culture: CultureInfo,
    dispatch: Dispatch<Msg>
  ) : Control =
  let t key = get(culture, key)

  let reasons =
    match failure with
    | RejectedPlan violations ->
      violations
      |> List.map(fun violation -> describeViolation(culture, violation))
      |> Array.ofList
    | Unavailable reason -> [| reason |]

  let retry =
    let button =
      iconPrimaryButton(
        t Keys.wizardRetry,
        Icons.refresh,
        (fun () -> dispatch Retry)
      )

    button.IsEnabled(ValueOption.isSome last)

  let exit =
    iconButton(t Keys.wizardExit, Icons.logout, (fun () -> dispatch Exit))

  let reasonsPanel =
    linesPanel [| for reason in reasons -> statusLine(reason, true) |]

  fluentPage {
    Lead = props.Lead
    Title = t Keys.errorTitle
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(12.0)
        .Children(reasonsPanel, actionRow [ exit; retry ])
    Measure = 760.0
  }

let doneView
  (
    props: WizardPageProps,
    plan: Plan,
    advisories: Violation list,
    culture: CultureInfo,
    dispatch: Dispatch<Msg>
  ) : Control =
  let t key = get(culture, key)

  let advisoryLines =
    advisories
    |> List.map(fun advisory ->
      statusLine(describeViolation(culture, advisory), true))
    |> Array.ofList

  let preview =
    PlanReview.PlanReview(
      {
        Culture = culture
        Parsed = { Plan = plan; Warnings = [] }
        Week = ValueNone
        Editing = false
      },
      ignore
    )

  let discard =
    iconButton(
      t Keys.wizardDiscard,
      Icons.delete,
      (fun () -> dispatch DiscardRequested)
    )

  let confirm =
    iconPrimaryButton(
      t Keys.wizardConfirm,
      Icons.check,
      (fun () -> dispatch ConfirmPlan)
    )

  fluentPage {
    Lead = props.Lead
    Title = t Keys.wizardTitle
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          preview,
          (if Array.isEmpty advisoryLines then
             StackPanel() :> Control
           else
             linesPanel advisoryLines),
          actionRow [ discard; confirm ]
        )
    Measure = 760.0
  }

let view
  (props: WizardPageProps)
  (culture: CultureInfo)
  (current: Model)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  match current.Phase with
  | Editing -> editingView(props, current, model, culture, dispatch)
  | Generating -> generatingView(props, culture, dispatch)
  | Failed(failure, last) -> failedView(props, failure, last, culture, dispatch)
  | Done(plan, advisories, _) ->
    doneView(props, plan, advisories, culture, dispatch)

type WizardPage
  (props: WizardPageProps, [<InlineIfLambda>] onOutcome: Outcome -> unit) as this
  =
  inherit UserControl()

  do
    let subscribe(_: Model) : Sub<Msg> =
      fun dispatch ->
        Page.listenGate {
          Control = this
          Gate = props.Gate
          OnShown = fun () -> dispatch Shown
          OnCancel = fun () -> dispatch RunAbandoned
        }

    let viewWithCulture (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      Page.trackPhase(this, props.Gate, model |> Observable.map phaseOf)

      Page.cultureHost {
        Culture = props.Culture
        Model = model
        Build =
          fun culture current dispatch ->
            view props culture current model dispatch
        Dispatch = dispatch
      }

    let program =
      Program.mkProgram init (update props onOutcome) viewWithCulture
      |> Program.withSubscription subscribe

    this.Content <- Program.runWith props program
