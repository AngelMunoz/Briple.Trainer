module Briple.Trainer.Views.Generate

// The wizard as an MVUish component: the step lives in the model, the
// step host's content is bound to the step stream, and the generation is
// a Cmd whose outcome lands as a message. No manual child clearing, no
// timers, no dispatcher posts from the page.

open System
open System.Globalization
open System.Threading

open Avalonia
open Avalonia.Animation
open Avalonia.Controls
open Avalonia.Media
open NXUI.Extensions

open FSharp.Control.Reactive

open IcedTasks

open MVUish.Avalonia

open Briple.Trainer
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
open Briple.Trainer.Stores
open Briple.Trainer.Theme

open Serilog

[<Struct>]
type WizardPageProps = {
  Culture: CultureInfo
  /// The raw storage: the audit line only.
  Store: IStore
  Memories: MemoryStore
  Plans: PlanStore
  Agent: IProvideAgent
  Athlete: AthleteEntity
  /// The confirmed memories relevant to this generation: athlete-scoped
  /// ones plus the plan-scoped ones of the plan being adjusted.
  Confirmed: Memory list
  Proposals: MemoryEntity list
  Previous: PlanEntity voption
}

// --- pure request assembly ---

/// The previous plan when it parses; adjustments chain on it, anything
/// else falls back to a brand-new plan.
[<return: Struct>]
let (|AdjustmentBase|_|)(previous: PlanEntity voption) =
  previous
  |> ValueOption.bind(fun entity ->
    match Briple.Trainer.Core.Plan.Parser.Parse.parse entity.Raw with
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

// --- violation rendering: typed cases to one localized line each ---
// (the Generate.fsi hides all of these; the page class is the export)

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

/// One user-facing line per violation case, in the page culture. The
/// broken-format detail stays technical (single-language, from Core):
/// the file format is the domain, like the parse warnings themselves.
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

/// Persists the edited proposal and re-emits the athlete's slice.
let updateMemory (store: MemoryStore) (entity: MemoryEntity) =
  store.Update entity

/// Writes the audit line of one generation.
let addRequest
  (store: IStore)
  (athleteId: Guid, kind: string, payload: string)
  =
  store.AddRequest(athleteId, kind, payload)

// --- the wizard state machine ---

/// Where the flow stands; `Done` carries everything the confirmation needs.
type Step =
  | Memories
  | Brief
  | Generating
  | Failed of
    reasons: string array *
    last: (int * Brief * WizardBrief.Mode) voption
  | Done of plan: Plan * advisories: Violation list * kind: string

type Model = {
  Step: Step
  /// One entry per card that decided, confirm or discard.
  Decided: Guid list
  Confirmed: (Guid * Memory) list
  /// The last submitted brief, what a retry re-sends: period, brief and
  /// the starting point the user picked.
  LastBrief: (int * Brief * WizardBrief.Mode) voption
  /// The generation in flight; cancelled when the wizard exits.
  Run: CancellationTokenSource voption
}

type Msg =
  | MemoryDecided of MemoryReview.Event
  | NextRequested
  | BackToMemories
  | BriefSubmitted of weeks: int * brief: Brief * mode: WizardBrief.Mode
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
    LastBrief = ValueNone
    Run = ValueNone
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
          (updateMemory props.Memories)
          {
            entity with
                Memory = confirm memory.Deltas entity.Memory
          }
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
          (updateMemory props.Memories)
          {
            entity with
                Memory = discard entity.Memory
          }
          (fun _ -> Recorded))
      |> Option.defaultValue Cmd.none

    decided, persist

  | NextRequested -> { model with Step = Brief }, Cmd.none

  | BackToMemories -> { model with Step = Memories }, Cmd.none

  | BriefSubmitted(weeks, brief, mode) ->
    let request =
      requestOf {
        Athlete = props.Athlete.Profile
        Memories = props.Confirmed @ (model.Confirmed |> List.map snd)
        Weeks = weeks
        Brief = brief
        // a fresh plan starts clean: no previous counts to honor
        Previous =
          (match props.Previous, mode with
           | ValueSome entity, WizardBrief.Mode.Adjust -> ValueSome entity
           | _ -> ValueNone)
      }

    let kind = kindOf request

    let record =
      Cmd.OfTask.perform
        (addRequest props.Store)
        (props.Athlete.Id,
         kind,
         $"weeks={weeks};freq={VariantFrequency.toFileToken brief.Frequency}")
        (fun _ -> Recorded)

    // one run, one CTS: leaving the wizard or a new run cancels it
    model.Run
    |> ValueOption.iter(fun cts ->
      cts.Cancel()
      cts.Dispose())

    let cts = new CancellationTokenSource()

    let run =
      Cmd.OfTask.perform (callOf(props.Agent, cts.Token)) request (function
        | Ok run -> GenerationDone(run, kind)
        | Error e -> GenerationFailed e.Message)

    {
      model with
          Step = Generating
          LastBrief = ValueSome(weeks, brief, mode)
          Run = ValueSome cts
    },
    Cmd.batch [ record; run ]

  | GenerationDone(run, kind) ->
    model.Run |> ValueOption.iter(fun cts -> cts.Dispose())

    match run.Verdict with
    | Rejected violations ->
      let reasons = [|
        for violation in violations ->
          describeViolation(props.Culture, violation)
      |]

      {
        model with
            Step = Failed(reasons, model.LastBrief)
            Run = ValueNone
      },
      Cmd.none
    | Valid(plan, advisories) ->
      {
        model with
            Step = Done(plan, advisories, kind)
            Run = ValueNone
      },
      Cmd.none

  | GenerationFailed reason ->
    model.Run |> ValueOption.iter(fun cts -> cts.Dispose())

    {
      model with
          Step = Failed([| reason |], model.LastBrief)
          Run = ValueNone
    },
    Cmd.none

  | ConfirmPlan ->
    match model.Step with
    | Done(plan, _, kind) ->
      // the recorded counts are what the plan carries, not an input
      let insert = {
        AthleteId = props.Athlete.Id
        Kind = kind
        Counts = planCounts props.Athlete.Profile plan
      }

      model, Cmd.OfTask.perform props.Plans.Add (insert, plan) PlanSaved
    | _ -> model, Cmd.none

  | PlanSaved(Ok entity) ->
    model.Run |> ValueOption.iter(fun cts -> cts.Dispose())

    onOutcome(PlanFinished entity.Id)

    { model with Run = ValueNone }, Cmd.none

  | PlanSaved(Error e) ->
    Log.Error(e, "plan save failed")
    model, Cmd.none

  | Cancelled ->
    // the user left mid-run: the generation stops with the wizard
    model.Run
    |> ValueOption.iter(fun cts ->
      cts.Cancel()
      cts.Dispose())

    onOutcome WizardCancelled

    { model with Run = ValueNone }, Cmd.none

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
    let step =
      iconButton(
        t Keys.wizardNext,
        Icons.chevronRight,
        (fun () -> dispatch NextRequested)
      )

    step.IsEnabled(model |> Observable.map resolved)

  let decided =
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

    if Array.isEmpty cards && List.isEmpty props.Confirmed then
      label(t Keys.memoriesEmpty)
    else
      linesPanel [|
        // the confirmed adjustments riding this generation: visible, so
        // the step never claims there is nothing to carry
        if not(List.isEmpty props.Confirmed) then
          yield sectionTitle(t Keys.wizardApplies)

          for memory in props.Confirmed do
            if not(String.IsNullOrWhiteSpace memory.Summary) then
              yield
                TextBlock()
                  .Text(memory.Summary)
                  .FontFamily(font)
                  .Foreground(muted)

        yield! cards
      |]

  fluentPage {
    Title = t Keys.wizardStepMemories
    Commands = []
    Footer = { Secondary = []; Primary = [ next ] }
    Body = decided
    Measure = 760.0
  }

let briefStep(props: WizardPageProps, dispatch: Dispatch<Msg>) : Control =
  let t key = get(props.Culture, key)

  let submitted(event: WizardBrief.Event) =
    match event with
    | WizardBrief.Event.Submitted(weeks, brief, mode) ->
      dispatch(BriefSubmitted(weeks, brief, mode))
    | WizardBrief.Event.Cancelled -> dispatch Cancelled

  let back =
    iconButton(
      t Keys.wizardBack,
      Icons.chevronLeft,
      (fun () -> dispatch BackToMemories)
    )

  fluentPage {
    Title = t Keys.wizardStepBrief
    Commands = []
    Footer = { Secondary = [ back ]; Primary = [] }
    Body =
      WizardBrief.WizardBrief(
        {
          Culture = props.Culture
          Athlete = props.Athlete.Profile
          HasPrevious = props.Previous.IsSome
        },
        submitted
      )
    Measure = 760.0
  }

/// The generation runs for one or two minutes: an indeterminate bar, the
/// status line, and the way out in the command bar.
let generatingStep(props: WizardPageProps, dispatch: Dispatch<Msg>) : Control =
  let cancel =
    iconButton(
      get(props.Culture, Keys.wizardCancelGeneration),
      Icons.stop,
      (fun () -> dispatch Cancelled)
    )

  fluentPage {
    Title = get(props.Culture, Keys.wizardTitle)
    Commands = []
    Footer = { Secondary = [ cancel ]; Primary = [] }
    Body =
      StackPanel()
        .Spacing(12.0)
        .Margin(0.0, 16.0, 0.0, 16.0)
        .Children(
          ProgressBar(IsIndeterminate = true, Maximum = 100.0, MinWidth = 240.0),
          statusLine(get(props.Culture, Keys.wizardGenerating), false)
        )
    Measure = 560.0
  }

/// What the failed step renders from, as one value.
[<Struct>]
type FailureContext = {
  Props: WizardPageProps
  Reasons: string array
  Last: (int * Brief * WizardBrief.Mode) voption
}

let failedStep(ctx: FailureContext, dispatch: Dispatch<Msg>) : Control =
  let t key = get(ctx.Props.Culture, key)

  // the retry re-sends the last brief instead of walking the steps again
  let retry =
    iconPrimaryButton(
      t Keys.wizardRetry,
      Icons.refresh,
      fun () ->
        ctx.Last
        |> ValueOption.iter(fun (weeks, brief, mode) ->
          dispatch(BriefSubmitted(weeks, brief, mode)))
    )

  let exit =
    iconButton(t Keys.wizardExit, Icons.logout, (fun () -> dispatch Cancelled))

  let reasonsPanel =
    linesPanel [| for reason in ctx.Reasons -> statusLine(reason, true) |]

  fluentPage {
    Title = t Keys.errorTitle
    Commands = []
    Footer = {
      Secondary = [ exit ]
      Primary = [ retry ]
    }
    Body = reasonsPanel
    Measure = 760.0
  }

/// What the done step renders from, as one value.
[<Struct>]
type DoneContext = {
  Props: WizardPageProps
  Plan: Plan
  Advisories: Violation list
}

let doneStep(ctx: DoneContext, dispatch: Dispatch<Msg>) : Control =
  let t key = get(ctx.Props.Culture, key)

  let advisoryPanel =
    linesPanel [|
      for advisory in ctx.Advisories ->
        statusLine(describeViolation(ctx.Props.Culture, advisory), true)
    |]

  // discarding throws away one or two minutes of generation: it asks
  let discard() =
    asyncEx {
      let! sure =
        Briple.Trainer.Dialogs.confirmAsync {
          Title = t Keys.wizardDiscardTitle
          Body = t Keys.wizardDiscardBody
          Accept = t Keys.wizardDiscard
          Cancel = t Keys.commonCancel
        }

      if sure then
        dispatch Cancelled
    }
    |> Async.StartImmediate

  fluentPage {
    Title = t Keys.wizardTitle
    Commands = []
    Footer = {
      Secondary = [ iconButton(t Keys.wizardDiscard, Icons.delete, discard) ]
      Primary = [
        iconPrimaryButton(
          t Keys.wizardConfirm,
          Icons.check,
          (fun () -> dispatch ConfirmPlan)
        )
      ]
    }
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          PlanReview.PlanReview(
            {
              Culture = ctx.Props.Culture
              Parsed = { Plan = ctx.Plan; Warnings = [] }
            },
            ignore
          ),
          advisoryPanel
        )
    Measure = 760.0
  }

/// What a step renders from, as one value.
[<Struct>]
type StepContext = {
  Props: WizardPageProps
  Model: IObservable<Model>
  Dispatch: Dispatch<Msg>
}

let stepView(ctx: StepContext, step: Step) : Control =
  match step with
  | Memories -> memoriesStep(ctx.Props, ctx.Model, ctx.Dispatch)
  | Brief -> briefStep(ctx.Props, ctx.Dispatch)
  | Generating -> generatingStep(ctx.Props, ctx.Dispatch)
  | Failed(reasons, last) ->
    failedStep(
      {
        Props = ctx.Props
        Reasons = reasons
        Last = last
      },
      ctx.Dispatch
    )
  | Done(plan, advisories, _) ->
    doneStep(
      {
        Props = ctx.Props
        Plan = plan
        Advisories = advisories
      },
      ctx.Dispatch
    )

// --- the page ---

let view
  (props: WizardPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  if not props.Agent.AgentAvailable then
    fluentPage {
      Title = get(props.Culture, Keys.wizardTitle)
      Commands = []
      Footer = noFooter
      Body = label(get(props.Culture, Keys.settingsMissing))
      Measure = 560.0
    }
  else
    // the host's content follows the step stream; distinct steps only, so
    // the memory cards keep their in-progress selections between
    // decisions, and the swap crossfades
    let steps =
      model
      |> Observable.map(fun m -> m.Step)
      |> Observable.distinctUntilChanged
      |> Observable.map(fun step ->
        stepView(
          {
            Props = props
            Model = model
            Dispatch = dispatch
          },
          step
        ))

    TransitioningContentControl()
      .PageTransition(CrossFade(TimeSpan.FromMilliseconds 150.0))
      .Content(steps.ToBinding())

type WizardPage
  (props: WizardPageProps, [<InlineIfLambda>] onOutcome: Outcome -> unit) as this
  =
  inherit UserControl()

  do
    let program = Program.mkProgram init (update props onOutcome) (view props)

    this.Content <- Program.runWith props program
