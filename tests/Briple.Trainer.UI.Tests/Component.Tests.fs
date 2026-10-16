module Briple.Trainer.UI.Tests.Components

// The wizard, review and note components through their public surface:
// props in, events out, driven with synthetic clicks and property
// writes. The note flow runs against a fake agent and a real store over
// a throwaway database.

open System
open System.Globalization
open System.IO
open System.Threading.Tasks

open Avalonia.Controls
open Avalonia.VisualTree

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

let private buttons(root: Control) : Button list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Button as button -> Some button
    | _ -> None)
  |> List.ofSeq

let private clickText(root: Control, text: string) : unit =
  buttons root
  |> List.tryFind(fun button -> string button.Content = text)
  |> Option.iter click

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
      match Briple.Trainer.Core.Plan.Parser.parsePlan sampleRaw with
      | Error e -> failtest $"the sample plan does not parse: {e}"
      | Ok parsed ->
        let events = ResizeArray<Briple.Trainer.Components.PlanReview.Event>()

        let review =
          probeUi(fun () ->
            Briple.Trainer.Components.PlanReview.PlanReview(
              { Culture = es; Parsed = parsed },
              events.Add
            ))

        mount review
        settle()

        Expect.isNonEmpty
          (List.ofSeq events)
          "the initial plan arrives as an event"

        // reading first: the exercise renders as a text line, no entries
        let readable =
          waitFor(
            2000,
            (fun () -> review),
            fun root -> hasText(root, "1. Press de banca · 8-10 · RIR 2 · 120s")
          )

        Expect.isTrue readable "the exercise line did not render"

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
              Some plan)
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
              Some plan)
          |> Seq.last

        Expect.equal (repsOf restored) "8-10" "the snapshot comes back"

        Expect.isEmpty
          (probeUi(fun () -> textBoxes review))
          "the row closes after the revert"

    testCase "wizard brief submits the period and the brief"
    <| fun () ->
      let events = ResizeArray<Briple.Trainer.Components.WizardBrief.Event>()

      let brief =
        probeUi(fun () ->
          Briple.Trainer.Components.WizardBrief.WizardBrief(
            {
              Culture = es
              Athlete = sampleAthlete
            },
            events.Add
          ))

      mount brief
      settle()

      onUi(fun () -> clickText(brief, get(es, Keys.wizardGenerate)))

      let reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq events),
          (fun current ->
            current
            |> List.exists(function
              | Briple.Trainer.Components.WizardBrief.Event.Submitted _ -> true
              | Briple.Trainer.Components.WizardBrief.Event.Cancelled -> false))
        )

      Expect.isTrue reported "the submit event reached the parent"

      match List.ofSeq events with
      | Briple.Trainer.Components.WizardBrief.Event.Submitted(weeks, brief) :: _ ->
        Expect.equal weeks 4 "the default period"

        Expect.equal brief.Goal Hypertrophy "the athlete's first goal"

        Expect.equal brief.Frequency TresDias "the default frequency"

        Expect.equal brief.Emphasis [] "no emphasis by default"
      | _ -> failtest "no submission"
  ]

// --- the note section --------------------------------------------------------

let private wait(task: Task<Result<'a, exn>>) : Result<'a, exn> =
  task.GetAwaiter().GetResult()

/// Answers the same reading every time, without touching the network.
type private FakeAgent(reading: NoteInterpretation) =
  interface Briple.Trainer.Env.IProvideAgent with
    member _.AgentAvailable = true

    member _.GenerateNew _ = failwith "not used in this flow"

    member _.GenerateAdjustment _ = failwith "not used in this flow"

    member _.ConsolidateMarks _ = failwith "not used in this flow"

    member _.InterpretNote(_, _) = Task.FromResult(Ok reading)

let noteTests =
  testList "note section" [
    testCase "a note becomes a proposal and the confirmation persists it"
    <| fun () ->
      let db =
        Path.Combine(Path.GetTempPath(), $"briple-note-{Guid.NewGuid():N}.db")

      migrate db |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athleteId =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let plan =
        match parsePlan sampleRaw with
        | Ok parsed -> parsed.Plan
        | Error error -> failtest $"the sample plan broke: {error.Message}"

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
