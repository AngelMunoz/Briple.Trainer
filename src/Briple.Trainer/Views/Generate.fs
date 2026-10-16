module Briple.Trainer.Views.Generate

// The wizard as an MVUish component: the step lives in the model, the
// step host's content is bound to the step stream, and the generation is
// a Cmd whose outcome lands as a message. No manual child clearing, no
// timers, no dispatcher posts from the page.

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Media
open NXUI.Extensions

open FSharp.Control.Reactive

open MVUish.Avalonia

open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator
open Briple.Trainer.Core.Wizard
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type WizardPageProps = {
  Culture: CultureInfo
  Store: IStore
  Agent: IProvideAgent
  Athlete: AthleteEntity
  Proposals: MemoryEntity list
  Previous: PlanEntity voption
  OnFinished: Guid -> unit
  OnCancelled: unit -> unit
}

// --- pure request assembly ---

/// The previous plan when it parses; adjustments chain on it, anything
/// else falls back to a brand-new plan.
[<return: Struct>]
let (|AdjustmentBase|_|)(previous: PlanEntity voption) =
  previous
  |> ValueOption.bind(fun entity ->
    match Briple.Trainer.Core.Plan.Parser.parsePlan entity.Raw with
    | Ok parsed -> ValueSome parsed.Plan
    | _ -> ValueNone)

/// Everything the request needs, as one value instead of threaded
/// parameters.
[<Struct>]
type RequestParts = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  Counts: (string * int) list
  Previous: PlanEntity voption
}

let requestOf(parts: RequestParts) : Generation =
  match parts.Previous with
  | AdjustmentBase plan ->
    adjustmentRequest
      parts.Athlete
      parts.Memories
      parts.Weeks
      parts.Brief
      plan
      parts.Counts
    |> Generation.PlanAdjustment
  | _ ->
    newRequest parts.Athlete parts.Memories parts.Weeks parts.Brief parts.Counts
    |> Generation.NewPlan

let inline kindOf kind =
  match kind with
  | Generation.NewPlan _ -> "new"
  | Generation.PlanAdjustment _ -> "adjust"

let inline callOf(agent: IProvideAgent) =
  function
  | Generation.NewPlan request -> agent.GenerateNew request
  | Generation.PlanAdjustment request -> agent.GenerateAdjustment request

// --- the wizard state machine ---

/// Where the flow stands; `Done` carries everything the confirmation needs.
type Step =
  | Memories
  | Brief
  | Generating
  | Failed of reasons: string array
  | Done of plan: Plan * advisories: Violation list * kind: string

type Model = {
  Step: Step
  /// One entry per card that decided, confirm or discard.
  Decided: Guid list
  Confirmed: (Guid * Memory) list
  Counts: (string * int) list
}

type Msg =
  | MemoryDecided of MemoryReview.Event
  | NextRequested
  | BackToMemories
  | BriefSubmitted of weeks: int * brief: Brief * counts: (string * int) list
  | GenerationDone of run: Run * kind: string
  | GenerationFailed of reason: string
  | PlanSaved of Result<PlanEntity, exn>
  | ConfirmPlan
  | Cancelled
  /// A store write the page does not need to react to.
  | Recorded

/// What the page reports outward.
type Outcome =
  | PlanFinished of Guid
  | WizardCancelled

let init(props: WizardPageProps) =
  {
    Step = Memories
    Decided = []
    Confirmed = []
    Counts = []
  },
  Cmd.none

let update
  (props: WizardPageProps)
  (onOutcome: Outcome -> unit)
  (msg: Msg)
  (model: Model)
  =
  match msg with
  | MemoryDecided(MemoryReview.Confirmed(id, memory)) ->
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
          (fun () ->
            props.Store.UpdateMemory {
              entity with
                  Memory = confirm memory.Deltas entity.Memory
            })
          ()
          (fun _ -> Recorded))
      |> Option.defaultValue Cmd.none

    decided, persist

  | MemoryDecided(MemoryReview.Discarded id) ->
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
          (fun () ->
            props.Store.UpdateMemory {
              entity with
                  Memory = discard entity.Memory
            })
          ()
          (fun _ -> Recorded))
      |> Option.defaultValue Cmd.none

    decided, persist

  | NextRequested -> { model with Step = Brief }, Cmd.none

  | BackToMemories -> { model with Step = Memories }, Cmd.none

  | BriefSubmitted(weeks, brief, counts) ->
    let request =
      requestOf {
        Athlete = props.Athlete.Profile
        Memories = model.Confirmed |> List.map snd
        Weeks = weeks
        Brief = brief
        Counts = counts
        Previous = props.Previous
      }

    let kind = kindOf request

    let record =
      Cmd.OfTask.perform
        (fun () ->
          props.Store.AddRequest(props.Athlete.Id, kind, $"weeks={weeks}"))
        ()
        (fun _ -> Recorded)

    let run =
      Cmd.OfTask.perform (fun () -> callOf props.Agent request) () (function
        | Ok run -> GenerationDone(run, kind)
        | Error e -> GenerationFailed e.Message)

    {
      model with
          Step = Generating
          Counts = counts
    },
    Cmd.batch [ record; run ]

  | GenerationDone(run, kind) ->
    match run.Verdict with
    | Rejected violations ->
      let reasons = [|
        for violation in violations -> $"{violation.Rule}: {violation.Detail}"
      |]

      { model with Step = Failed reasons }, Cmd.none
    | Valid(plan, advisories) ->
      {
        model with
            Step = Done(plan, advisories, kind)
      },
      Cmd.none

  | GenerationFailed reason ->
    {
      model with
          Step = Failed [| reason |]
    },
    Cmd.none

  | ConfirmPlan ->
    match model.Step with
    | Done(plan, _, kind) ->
      let insert = {
        AthleteId = props.Athlete.Id
        Kind = kind
        Counts = model.Counts
      }

      model,
      Cmd.OfTask.perform
        (fun () -> props.Store.AddPlan(insert, plan))
        ()
        PlanSaved
    | _ -> model, Cmd.none

  | PlanSaved(Ok entity) ->
    onOutcome(PlanFinished entity.Id)
    model, Cmd.none

  | PlanSaved(Error e) ->
    eprintfn $"plan save failed: %O{e}"
    model, Cmd.none

  | Cancelled ->
    onOutcome WizardCancelled
    model, Cmd.none

  | Recorded -> model, Cmd.none

// --- step views ---

let inline statusLine(text: string, isError: bool) : Control =
  TextBlock()
    .Text(text)
    .FontFamily(font)
    .Foreground(if isError then errorBrush else muted)

let inline linesPanel(lines: Control array) : Control =
  StackPanel().Spacing(4.0).Children(lines)

let memoriesStep
  (props: WizardPageProps, model: IObservable<Model>, dispatch: Dispatch<Msg>)
  : Control =
  let t key = get(props.Culture, key)

  let inline resolved(m: Model) =
    List.length m.Decided = List.length props.Proposals

  let next =
    primaryButton(t Keys.wizardNext, (fun () -> dispatch NextRequested))
    |> fun primary -> primary.IsEnabled(model |> Observable.map resolved)

  let cards: Control array = [|
    for entity in props.Proposals ->
      MemoryReview.MemoryReview(
        {
          Culture = props.Culture
          Id = entity.Id
          Memory = entity.Memory
        },
        MemoryDecided >> dispatch
      )
  |]

  let decided =
    if Array.isEmpty cards then
      label(t Keys.memoriesEmpty)
    else
      linesPanel cards

  StackPanel()
    .Spacing(8.0)
    .Children(title(t Keys.wizardStepMemories), decided, next :> Control)

let briefStep(props: WizardPageProps, dispatch: Dispatch<Msg>) : Control =
  let t key = get(props.Culture, key)

  let submitted(event: WizardBrief.Event) =
    match event with
    | WizardBrief.Event.Submitted(weeks, brief, counts) ->
      dispatch(BriefSubmitted(weeks, brief, counts))
    | WizardBrief.Event.Cancelled -> dispatch Cancelled

  StackPanel()
    .Spacing(8.0)
    .Children(
      title(t Keys.wizardStepBrief),
      WizardBrief.WizardBrief(
        {
          Culture = props.Culture
          Athlete = props.Athlete.Profile
          PreviousCounts =
            props.Previous
            |> ValueOption.map _.Counts
            |> ValueOption.defaultValue []
        },
        submitted
      ),
      button(t Keys.wizardBack, (fun () -> dispatch BackToMemories))
    )

let inline generatingStep(props: WizardPageProps) : Control =
  statusLine(get(props.Culture, Keys.wizardGenerating), false)

let failedStep
  (props: WizardPageProps, dispatch: Dispatch<Msg>, reasons: string array)
  : Control =
  let t key = get(props.Culture, key)

  let reasonsPanel =
    linesPanel [| for reason in reasons -> statusLine(reason, true) |]

  StackPanel()
    .Spacing(8.0)
    .Children(
      title(t Keys.errorTitle),
      reasonsPanel,
      button(t Keys.wizardBack, (fun () -> dispatch Cancelled))
    )

let doneStep
  (
    props: WizardPageProps,
    dispatch: Dispatch<Msg>,
    plan: Plan,
    advisories: Violation list
  ) : Control =
  let t key = get(props.Culture, key)

  let advisoryPanel =
    linesPanel [|
      for advisory in advisories ->
        statusLine($"{advisory.Rule}: {advisory.Detail}", true)
    |]

  StackPanel()
    .Spacing(8.0)
    .Children(
      PlanReview.PlanReview(
        {
          Culture = props.Culture
          Parsed = { Plan = plan; Warnings = [] }
        },
        ignore
      ),
      advisoryPanel,
      primaryButton(t Keys.wizardConfirm, (fun () -> dispatch ConfirmPlan))
      :> Control,
      button(t Keys.wizardDiscard, (fun () -> dispatch Cancelled))
    )

let stepView
  (
    props: WizardPageProps,
    model: IObservable<Model>,
    dispatch: Dispatch<Msg>,
    step: Step
  ) : Control =
  match step with
  | Memories -> memoriesStep(props, model, dispatch)
  | Brief -> briefStep(props, dispatch)
  | Generating -> generatingStep props
  | Failed reasons -> failedStep(props, dispatch, reasons)
  | Done(plan, advisories, _) -> doneStep(props, dispatch, plan, advisories)

// --- the page ---

let view
  (props: WizardPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let shell(body: Control) =
    ScrollViewer()
      .Content(StackPanel().Margin(24.0).Spacing(8.0).Children(body))
    :> Control

  if not props.Agent.AgentAvailable then
    shell(
      StackPanel()
        .Spacing(8.0)
        .Children(
          title(get(props.Culture, Keys.wizardTitle)),
          label(get(props.Culture, Keys.settingsMissing))
        )
    )
  else
    // the host's content follows the step stream; distinct steps only, so
    // the memory cards keep their in-progress selections between decisions
    let steps =
      model
      |> Observable.map(fun m -> m.Step)
      |> Observable.distinctUntilChanged
      |> Observable.map(fun step -> box(stepView(props, model, dispatch, step)))

    shell(ContentControl().Content steps)

type WizardPage(props: WizardPageProps) as this =
  inherit UserControl()

  do
    let onOutcome =
      function
      | PlanFinished id -> props.OnFinished id
      | WizardCancelled -> props.OnCancelled()

    let program = Program.mkProgram init (update props onOutcome) (view props)

    this.Content <- Program.runWith props program
