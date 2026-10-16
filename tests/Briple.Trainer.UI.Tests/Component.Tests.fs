module Briple.Trainer.UI.Tests.Components

// The wizard and review components through their public surface: props in,
// events out, driven with synthetic clicks and property writes.

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.VisualTree

open Expecto

open Briple.Trainer.Strings

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.UI.Tests.Harness

let private es = Language.toCulture Es

let private sampleAthlete: Athlete = {
  Name = "Pepe"
  Gender = Male
  Variants = [ "3dias" ]
  Goals = [ Hypertrophy ]
  Focus = []
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

    testCase "plan review edits flow back as the full plan tree"
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

        // the reps entry is the first 90-wide TextBox of the row
        let repsEntry =
          probeUi(fun () ->
            textBoxes review
            |> List.tryFind(fun textBox -> textBox.Width = 90.0))

        match repsEntry with
        | None -> failtest "the reps entry did not render"
        | Some entry ->
          onUi(fun () -> entry.Text <- "6-8")

          settle()

          let lastPlan =
            events
            |> Seq.choose(function
              | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
                Some plan)
            |> Seq.last

          let exercise =
            lastPlan.Blocks
            |> List.collect _.Variants
            |> List.collect _.Weeks
            |> List.collect _.Days
            |> List.collect _.Exercises
            |> List.find(fun exercise -> exercise.Name = "Press de banca")

          Expect.equal exercise.Reps "6-8" "the edit lands in the tree"

          Expect.equal exercise.Rir "2" "the other fields stay verbatim"

    testCase "wizard brief submits weeks, brief and counts"
    <| fun () ->
      let events = ResizeArray<Briple.Trainer.Components.WizardBrief.Event>()

      let brief =
        probeUi(fun () ->
          Briple.Trainer.Components.WizardBrief.WizardBrief(
            {
              Culture = es
              Athlete = sampleAthlete
              PreviousCounts = []
            },
            events.Add
          ))

      mount brief
      settle()

      // the counts entry for "3dias" is the first TextBox of the form
      onUi(fun () ->
        match textBoxes brief with
        | count :: _ -> count.Text <- "10"
        | [] -> failtest "the counts entry did not render")

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
      | Briple.Trainer.Components.WizardBrief.Event.Submitted(weeks,
                                                              brief,
                                                              counts) :: _ ->
        Expect.equal weeks 4 "the default period"

        Expect.equal brief.Goal Hypertrophy "the default goal"

        Expect.equal brief.Variants [ "3dias" ] "the preselected variants"

        Expect.equal counts [ "3dias", 10 ] "the counts"
      | _ -> failtest "no submission"
  ]
