module Briple.Trainer.UI.Tests.Components

// The wizard, review and note components through their public surface:
// props in, events out, driven with synthetic clicks and property
// writes. The note flow runs against a fake agent and a real store over
// a throwaway database.

open System
open System.Globalization
open System.IO
open System.Reactive.Subjects
open System.Threading.Tasks

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Headless
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open NXUI.Extensions

open Expecto

open Briple.Trainer.Strings

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.UI.Tests.Harness

// migrations under test log through the app's pipeline (silent under the
// default Serilog)
let private testLogger = Briple.Trainer.Env.loggerFactory.CreateLogger "Tests"

let private es = Language.toCulture Es

let private sampleAthlete: Athlete = {
  Name = "Pepe"
  Gender = Male
  Goals = [ Hypertrophy; Strength ]
}

let private sampleRaw =
  Briple.Trainer.Core.Plan.Writer.write {
    Version = 1
    Title = "Mes 1"
    Weeks = 1
    Generated = "2026-10-15"
    Origin = "trainer"
    Blocks = [
      {
        Gender = Male
        Variants = [
          {
            Id = "3dias"
            Title = None
            Weeks = [
              {
                Number = 1
                Days = [
                  {
                    Id = "Lunes"
                    Title = Some "Empuje"
                    Notes = []
                    Exercises = [
                      {
                        Circuit = "A"
                        Laps = "3"
                        Order = 1
                        Name = "Press de banca"
                        Reps = "8-10"
                        Rir = "2"
                        Rest = "120s"
                        Notes = "-"
                      }
                    ]
                  }
                ]
              }
            ]
          }
        ]
      }
    ]
    Extras = []
  }

let private textBoxes(root: Control) : TextBox list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? TextBox as textBox -> Some textBox
    | _ -> None)
  |> List.ofSeq

let private clickText(root: Control, text: string) : unit =
  buttonWithText(root, text) |> Option.iter click

/// Finds a radio button by its visible label.
let private radioWithText(root: Control, text: string) : RadioButton option =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? RadioButton as radio when string radio.Content = text -> Some radio
    | _ -> None)
  |> Seq.tryHead



let tests =
  testList "components" [
    testCase "memory review confirms with the accepted deltas"
    <| fun () ->
      let events = ResizeArray<Briple.Trainer.Components.MemoryReview.Event>()

      let memory =
        consolidate {
          Target = Exercise "Press de banca"
          Label = Light
          Date = DateOnly(2026, 10, 6)
        }

      let card =
        probeUi(fun () ->
          Briple.Trainer.Components.MemoryReview.MemoryReview(
            {
              Culture = es
              Id = Guid.NewGuid()
              Memory = memory
            },
            events.Add
          ))

      mount card
      settle()

      onUi(fun () -> clickText(card, get(es, Keys.memoriesConfirm)))

      let reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq events),
          (fun current ->
            current
            |> List.exists(function
              | Briple.Trainer.Components.MemoryReview.Event.Confirmed _ ->
                true
              | Briple.Trainer.Components.MemoryReview.Event.Discarded _ ->
                false))
        )

      Expect.isTrue reported "the confirm event reached the parent"

      match List.ofSeq events with
      | Briple.Trainer.Components.MemoryReview.Event.Confirmed(_, confirmed) :: _ ->
        Expect.equal confirmed.Deltas memory.Deltas "the proposal deltas travel"
      | _ -> failtest "no confirmed memory"

    testCase "memory review discards"
    <| fun () ->
      let events = ResizeArray<Briple.Trainer.Components.MemoryReview.Event>()

      let memory =
        consolidate {
          Target = Exercise "Press de banca"
          Label = Light
          Date = DateOnly(2026, 10, 6)
        }

      let id = Guid.NewGuid()

      let card =
        probeUi(fun () ->
          Briple.Trainer.Components.MemoryReview.MemoryReview(
            {
              Culture = es
              Id = id
              Memory = memory
            },
            events.Add
          ))

      mount card
      settle()

      onUi(fun () -> clickText(card, get(es, Keys.memoriesDiscard)))

      let reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq events),
          (fun current ->
            List.contains
              (Briple.Trainer.Components.MemoryReview.Event.Discarded id)
              current)
        )

      Expect.isTrue reported "the discard event reached the parent"

    testCase "the plan reads first and edits by row"
    <| fun () ->
      match Briple.Trainer.Core.Plan.Parser.Parse.parse sampleRaw with
      | Error e -> failtest $"the sample plan does not parse: {e}"
      | Ok parsed ->
        let events = ResizeArray<Briple.Trainer.Components.PlanReview.Event>()

        let review =
          probeUi(fun () ->
            Briple.Trainer.Components.PlanReview.PlanReview(
              {
                Culture = es
                Parsed = parsed
                Week = ValueNone
                Editing = false
              },
              events.Add
            ))

        mount review
        settle()

        Expect.isEmpty (List.ofSeq events) "an untouched plan reports no change"

        // reading first: the exercise renders as prose lines, no entries
        let readable =
          waitFor(
            2000,
            (fun () -> review),
            fun root ->
              hasText(root, "1. Press de banca")
              && hasText(root, "8-10 · RIR 2 · 120s")
          )

        Expect.isTrue readable "the exercise lines did not render"

        Expect.isEmpty
          (probeUi(fun () -> textBoxes review))
          "no entries before the explicit edit"

        // the explicit edit opens the row's entries
        let editButton =
          match
            probeUi(fun () -> buttonWithText(review, get(es, Keys.planEdit)))
          with
          | Some button -> button
          | None -> failtest "the edit button did not render"

        onUi(fun () -> click editButton)

        let opened =
          waitFor(
            2000,
            (fun () -> textBoxes review),
            fun entries ->
              entries |> List.exists(fun entry -> entry.Width = 90.0)
          )

        Expect.isTrue opened "the row entries did not open"

        let repsEntry =
          probeUi(fun () ->
            textBoxes review |> List.find(fun textBox -> textBox.Width = 90.0))

        onUi(fun () -> repsEntry.Text <- "6-8")

        settle()

        let lastPlan =
          events
          |> Seq.choose(function
            | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
              Some plan
            | Briple.Trainer.Components.PlanReview.Event.ViewChanged _ -> None)
          |> Seq.last

        let repsOf(plan: Plan) =
          plan.Blocks
          |> List.collect _.Variants
          |> List.collect _.Weeks
          |> List.collect _.Days
          |> List.collect _.Exercises
          |> List.find(fun exercise -> exercise.Name = "Press de banca")
          |> fun exercise -> exercise.Reps

        Expect.equal (repsOf lastPlan) "6-8" "the edit lands in the tree"

        // the revert restores the last snapshot and closes the row
        let revertButton =
          match
            probeUi(fun () -> buttonWithText(review, get(es, Keys.planRevert)))
          with
          | Some button -> button
          | None -> failtest "the revert button did not render"

        onUi(fun () -> click revertButton)

        settle()

        let restored =
          events
          |> Seq.choose(function
            | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
              Some plan
            | Briple.Trainer.Components.PlanReview.Event.ViewChanged _ -> None)
          |> Seq.last

        Expect.equal (repsOf restored) "8-10" "the snapshot comes back"

        Expect.isEmpty
          (probeUi(fun () -> textBoxes review))
          "the row closes after the revert"

    testCase "wizard brief reports every draft change"
    <| fun () ->
      let updates =
        ResizeArray<Briple.Trainer.Components.WizardBrief.BriefDraft>()

      let draft =
        Briple.Trainer.Components.WizardBrief.defaultDraft(sampleAthlete, false)

      let brief =
        probeUi(fun () ->
          Briple.Trainer.Components.WizardBrief.WizardBrief {
            Culture = es
            Athlete = sampleAthlete
            HasPrevious = false
            Draft = draft
            OnChanged = updates.Add
          })

      mount brief
      settle()

      // the default draft is the seeded one
      Expect.equal draft.Goal Hypertrophy "the athlete's first goal"

      Expect.equal draft.Frequency TresDias "the default frequency"

      Expect.equal draft.Emphasis [] "no emphasis by default"

      Expect.equal
        draft.Mode
        Briple.Trainer.Components.WizardBrief.Mode.Fresh
        "a first plan starts fresh"

      let weekTwo =
        match probeUi(fun () -> radioWithText(brief, "2")) with
        | Some radio -> radio
        | None -> failtest "the week radio did not render"

      onUi(fun () -> click weekTwo)

      let reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq updates),
          fun current -> not current.IsEmpty
        )

      Expect.isTrue reported "the change reached the page"

      let last = Seq.last updates

      Expect.equal last.Weeks 2 "the picked period"

      Expect.equal last.Goal Hypertrophy "the goal stays"

      Expect.equal last.Emphasis [] "the emphasis stays"

    test "the hub math clamps the wheel" {
      let wheel = Briple.Trainer.Components.Hub.wheelOffset

      Expect.equal (wheel(0.0, 100.0, 500.0)) 100.0 "pan forward"

      Expect.equal (wheel(400.0, 200.0, 500.0)) 500.0 "clamped at the end"

      Expect.equal (wheel(300.0, -1000.0, 500.0)) 0.0 "clamped at the start"
    }

    test "the hub leaves a plain vertical wheel to the page" {
      let step = Briple.Trainer.Components.Hub.wheelStep

      Expect.equal
        (step(Avalonia.Vector(0.0, 120.0), false))
        Briple.Trainer.Components.Hub.WheelStep.Pass
        "vertical without shift passes"

      Expect.equal
        (step(Avalonia.Vector(0.0, -120.0), false))
        Briple.Trainer.Components.Hub.WheelStep.Pass
        "vertical up passes"

      Expect.equal
        (step(Avalonia.Vector(0.0, -120.0), true))
        (Briple.Trainer.Components.Hub.WheelStep.Pan 5760.0)
        "shift maps the vertical wheel to a pan"
    }

    test "the hub overflows when the columns exceed the viewport" {
      let hubControl =
        probeUi(fun () ->
          Briple.Trainer.Components.Hub.hub {
            Title = ""
            Slots = [
              for index in 1..5 ->
                Briple.Trainer.Components.Hub.slot(
                  $"Día {index}",
                  TextBlock(Text = $"contenido {index}")
                )
            ]
            Active = ValueNone
            OnSelect = ValueNone
          })

      // a titleless hub IS the scroller: five 400px columns in a 600px
      // window must overflow, and the slot headers must render
      onUi(fun () ->
        let window =
          Window(Content = hubControl, Width = 600.0, Height = 400.0)

        window.Show() |> ignore)

      let overflow =
        waitFor(
          2000,
          (fun () -> hubControl :?> ScrollViewer),
          fun scroller ->
            scroller.Extent.Width > scroller.Viewport.Width + 100.0
        )

      Expect.isTrue overflow "the columns overflow the viewport"

      let headers =
        waitFor(
          2000,
          (fun () -> hubControl),
          fun root -> hasText(root, "Día 1") && hasText(root, "Día 5")
        )

      Expect.isTrue headers "the slot headers rendered"
    }
    test "a plain vertical wheel over the hub scrolls the page" {
      let hubControl =
        probeUi(fun () ->
          Briple.Trainer.Components.Hub.hub {
            Title = ""
            Slots = [
              for index in 1..5 ->
                Briple.Trainer.Components.Hub.slot(
                  $"Día {index}",
                  TextBlock(Text = $"contenido {index}")
                )
            ]
            Active = ValueNone
            OnSelect = ValueNone
          })

      let window, outer =
        probeUi(fun () ->
          let outer =
            ScrollViewer(
              Content =
                StackPanel().Children(hubControl, Border().Height(1200.0))
            )

          let window = Window(Content = outer, Width = 600.0, Height = 400.0)

          window.Show() |> ignore
          window, outer)

      settle()

      let point =
        probeUi(fun () ->
          match hubControl.TranslatePoint(Point(20.0, 20.0), window) with
          | point when point.HasValue -> point.Value
          | _ -> Point(20.0, 20.0))

      onUi(fun () ->
        HeadlessWindowExtensions.MouseWheel(
          window,
          point,
          Vector(0.0, -120.0),
          RawInputModifiers.None
        ))

      settle()

      let pageScrolled = probeUi(fun () -> outer.Offset.Y)

      Expect.isGreaterThan
        pageScrolled
        0.0
        "the page scrolled instead of the hub swallowing the wheel"

      // Shift maps the vertical wheel to a horizontal pan; the page stays
      onUi(fun () -> outer.Offset <- Vector(0.0, 0.0))

      onUi(fun () ->
        HeadlessWindowExtensions.MouseWheel(
          window,
          point,
          Vector(0.0, -120.0),
          RawInputModifiers.Shift
        ))

      settle()

      let hubScroller = hubControl :?> ScrollViewer

      let hubPanned, pageStayed =
        probeUi(fun () -> hubScroller.Offset.X, outer.Offset.Y)

      Expect.isGreaterThan hubPanned 0.0 "shift pans the hub horizontally"

      Expect.equal pageStayed 0.0 "shift does not scroll the page"
    }
  ]

// --- the note section --------------------------------------------------------

let private wait(task: Task<Result<'a, exn>>) : Result<'a, exn> =
  task.GetAwaiter().GetResult()

/// Answers the same reading every time, without touching the network.
type private FakeAgent(reading: NoteInterpretation) =
  interface Briple.Trainer.Env.IProvideAgent with
    member _.AgentAvailable = true

    member _.GenerateNew(_, _) = failwith "not used in this flow"

    member _.GenerateAdjustment(_, _) = failwith "not used in this flow"

    member _.ConsolidateMarks(_, _) = failwith "not used in this flow"

    member _.InterpretNote(_, _) = Task.FromResult(Ok reading)

let noteTests =
  testList "note section" [
    testCase "a note becomes a proposal and the confirmation persists it"
    <| fun () ->
      let db =
        Path.Combine(Path.GetTempPath(), $"briple-note-{Guid.NewGuid():N}.db")

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athleteId =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let plan =
        match Parse.parse sampleRaw with
        | Ok parsed -> parsed.Plan
        | Error error ->
          failtest $"the sample plan broke: {Parse.describe error}"

      let planId = Guid.NewGuid()

      let reading: NoteInterpretation = {
        Memories = [
          {
            Summary = ""
            Deltas = [ Load 5 ]
            Origin =
              Mark {
                Target = Exercise "Press de banca"
                Label = Light
                Date = DateOnly(2026, 10, 15)
              }
            Status = Proposed
            Scope = Plan
          }
        ]
        Rejections = []
      }

      let section =
        probeUi(fun () ->
          Briple.Trainer.Components.NoteSection.NoteSection(
            {
              Culture = es
              Agent = FakeAgent(reading)
              Memories = stores.Memories
              AthleteId = athleteId
              Plan = plan
              PlanId = planId
              Cancel = System.Threading.CancellationToken.None
              Initial = {
                Note = ""
                Busy = false
                Proposals = []
                Status = Briple.Trainer.Components.NoteSection.Status.Idle
              }
              OnState = ignore
            },
            ignore
          ))

      mount section
      settle()

      // the note entry is the section's only TextBox
      onUi(fun () ->
        match textBoxes section with
        | note :: _ -> note.Text <- "el press quedó liviano"
        | [] -> failtest "the note entry did not render")

      settle()

      onUi(fun () -> clickText(section, get(es, Keys.reviewNoteInterpret)))

      // the reading lands as a confirm/discard card
      let card =
        waitFor(
          2000,
          (fun () -> section),
          fun root ->
            buttonWithText(root, get(es, Keys.memoriesConfirm)) |> Option.isSome
        )

      Expect.isTrue card "the proposal card did not render"

      settle()

      let confirm =
        match
          probeUi(fun () ->
            buttonWithText(section, get(es, Keys.memoriesConfirm)))
        with
        | Some button -> button
        | None -> failtest "the confirm button vanished"

      onUi(fun () -> click confirm)

      let persisted =
        waitFor(
          2000,
          (fun () -> stores.Memories.Snapshot athleteId),
          fun entities ->
            entities
            |> List.exists(fun entity -> entity.Memory.Status = Confirmed)
        )

      Expect.isTrue persisted "the confirmation reached the store"

      let stored =
        stores.Memories.Snapshot athleteId
        |> List.find(fun entity -> entity.Memory.Status = Confirmed)

      Expect.equal stored.PlanId (ValueSome planId) "the plan id rides"

      Expect.equal stored.Memory.Scope Plan "the situational scope rides"
  ]

// --- the review page --------------------------------------------------------

let private noReading: NoteInterpretation = { Memories = []; Rejections = [] }

/// A real store with one athlete and one plan, plus the page's gate.
let private reviewSetup() =
  let db =
    Path.Combine(Path.GetTempPath(), $"briple-review-{Guid.NewGuid():N}.db")

  migrate(testLogger, db) |> ignore

  let stores = createStores(sqliteStore db, ignore)

  let athleteId =
    match wait(stores.Athletes.Add sampleAthlete) with
    | Ok entity -> entity.Id
    | Error e -> failtest $"add failed: {e}"

  let plan =
    match Parse.parse sampleRaw with
    | Ok parsed -> parsed.Plan
    | Error error -> failtest $"the sample plan broke: {Parse.describe error}"

  let entity =
    match
      wait(
        stores.Plans.Add(
          {
            AthleteId = athleteId
            Kind = "new"
            Counts = planCounts sampleAthlete plan
          },
          plan
        )
      )
    with
    | Ok entity -> entity
    | Error e -> failtest $"plan add failed: {e}"

  stores, athleteId, entity

let reviewTests =
  testList "review page" [
    testCase "a clean review is Ready, an edit is Unsaved, the save clears it"
    <| fun () ->
      let stores, _, entity = reviewSetup()

      let source, culture = liveCulture es

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/review/test",
          fun _ _ -> async { return true }
        )

      let page =
        probeUi(fun () ->
          Briple.Trainer.Views.Review.ReviewPage {
            Culture = culture
            Lead = ValueNone
            Plans = stores.Plans
            Memories = stores.Memories
            Agent = FakeAgent(noReading)
            Host = ValueNone
            Entity = entity
            Gate = gate
          })

      mount page
      settle()

      Expect.equal
        gate.PhaseValue
        Briple.Trainer.Navigation.PagePhase.Ready
        "an untouched review is ready"

      let rendered =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> hasText(root, get(es, Keys.reviewTitle))
        )

      Expect.isTrue rendered "the review rendered"

      onUi(fun () -> clickText(page, get(es, Keys.planEdit)))

      let opened =
        waitFor(
          2000,
          (fun () -> page),
          fun root ->
            textBoxes(root) |> List.exists(fun box -> box.Width = 90.0)
        )

      Expect.isTrue opened "the edit mode opened"

      let repsEntry =
        probeUi(fun () ->
          textBoxes page |> List.find(fun box -> box.Width = 90.0))

      onUi(fun () -> repsEntry.Text <- "6-8")

      let dirty =
        waitFor(
          2000,
          (fun () -> gate.PhaseValue),
          fun value ->
            value = Briple.Trainer.Navigation.PagePhase.UnsavedChanges
        )

      Expect.isTrue dirty "the edit marks the review unsaved"

      onUi(fun () -> clickText(page, get(es, Keys.reviewSave)))

      let saved =
        waitFor(
          2000,
          (fun () -> gate.PhaseValue),
          fun value -> value = Briple.Trainer.Navigation.PagePhase.Ready
        )

      Expect.isTrue saved "the save clears the phase"

      let persisted =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot entity.AthleteId),
          fun plans ->
            plans
            |> List.exists(fun plan ->
              plan.Id = entity.Id && plan.Raw.Contains "6-8")
        )

      Expect.isTrue persisted "the edited raw reached the store"

      // a culture switch rebuilds the page from its model; the draft and
      // the edit mode survive
      onUi(fun () -> source.OnNext(Language.toCulture En))

      let retitled =
        waitFor(
          2000,
          (fun () -> page),
          fun root ->
            hasText(root, get(Language.toCulture En, Keys.reviewTitle))
        )

      Expect.isTrue retitled "the review rebuilt in the new language"

      let repsAfter =
        probeUi(fun () ->
          textBoxes page
          |> List.tryFind(fun box -> box.Width = 90.0)
          |> Option.map _.Text
          |> Option.defaultValue "")

      Expect.equal repsAfter "6-8" "the draft survived the rebuild"
  ]

// --- the wizard hub ---------------------------------------------------------

let wizardTests =
  testList "wizard hub" [
    testCase "the hub moves between sections and resets on activation"
    <| fun () ->
      let db =
        Path.Combine(Path.GetTempPath(), $"briple-wizard-{Guid.NewGuid():N}.db")

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athlete =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity
        | Error e -> failtest $"add failed: {e}"

      let source, culture = liveCulture es

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/athletes/test/generate",
          fun _ _ -> async { return true }
        )

      let page =
        probeUi(fun () ->
          Briple.Trainer.Views.Generate.WizardPage(
            {
              Culture = culture
              Lead = ValueNone
              Store = stores.Raw
              Memories = stores.Memories
              Plans = stores.Plans
              Agent = FakeAgent(noReading)
              Athlete = athlete
              Confirmed = []
              Proposals = []
              Previous = ValueNone
              Gate = gate
            },
            ignore
          ))

      mount page
      settle()

      let hub =
        probeUi(fun () ->
          page.GetVisualDescendants()
          |> Seq.choose(fun visual ->
            match visual with
            | :? ScrollViewer as scroller when
              scroller.HorizontalScrollBarVisibility =
                ScrollBarVisibility.Auto
              ->
              Some scroller
            | _ -> None)
          |> Seq.tryHead)

      match hub with
      | None -> failtest "the hub did not render"
      | Some hub ->
        let briefHeader =
          match
            probeUi(fun () ->
              buttonWithText(page, get(es, Keys.wizardStepBrief)))
          with
          | Some button -> button
          | None -> failtest "the brief header did not render"

        onUi(fun () -> click briefHeader)

        let panned = waitFor(2000, (fun () -> hub.Offset.X), fun x -> x > 10.0)

        Expect.isTrue panned "the hub panned to the brief section"

        // a second activation resets the page: the hub returns to memories
        onUi(fun () -> gate.MarkShown())

        let reset = waitFor(2000, (fun () -> hub.Offset.X), fun x -> x < 1.0)

        Expect.isTrue reset "the wizard reset to the first section"

        let noStepBack =
          probeUi(fun () -> buttonWithText(page, get(es, Keys.wizardBack)))

        Expect.isNone noStepBack "the wizard has no step-back button"
  ]
