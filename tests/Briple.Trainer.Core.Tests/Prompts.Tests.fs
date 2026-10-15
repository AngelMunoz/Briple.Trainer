module Briple.Trainer.Core.Tests.Prompts.Tests

// The prompt render: the format, the fixed part (weeks, variants,
// counts, RIR), the forbidden part, and the previous plan in the
// adjustment.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Tests.TestSupport

let private parse name =
  match readFixture name |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}"
  | Ok parsed -> parsed

let private man = {
  Name = "Pepe"
  Gender = Male
  Variants = [ "3dias"; "5dias" ]
  Goals = [ "fuerza" ]
  Focus = [ "gluteo" ]
}

let private brief = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3"); (2, "2-3"); (4, "1") ]
  Variants = [ "3dias"; "5dias" ]
}

let tests =
  testList "Prompts" [
    testCase
      "the new-plan prompt carries schema, fixed and forbidden"
      (fun () ->
        let request =
          newRequest man [] 4 brief [ ("3dias", 72); ("5dias", 72) ]

        let prompt = newPlanPrompt request

        Expect.stringContains
          prompt
          "- the plan covers 4 weeks"
          "the weeks get fixed"

        Expect.stringContains
          prompt
          "- the plan has one option per variant: 3dias, 5dias"
          "the variants get fixed"

        Expect.stringContains
          prompt
          "- week 2 targets RIR 2-3 (added automatically, do not write it)"
          "the RIR gets fixed"

        Expect.stringContains
          prompt
          "- option 3dias has exactly 72 exercises in total"
          "the counts get fixed"

        Expect.stringContains prompt "FORBIDDEN" "the prohibitions travel"

        Expect.stringContains
          system
          "RESPONSE SCHEMA"
          "the system carries the json schema"

        Expect.stringContains
          system
          "\"exercises\": ["
          "the schema declares the exercises"

        Expect.stringContains
          system
          "EVIDENCE-BASED TRAINING"
          "the system demands evidence"

        Expect.stringContains
          system
          "VARIANT ROTATION"
          "the system demands variant rotation"

        Expect.stringContains
          system
          "an exercise name never repeats in the same day of the following week"
          "the rotation is between consecutive weeks"

        Expect.stringContains
          system
          "Respond with the JSON only"
          "the output instruction goes in the system")

    testCase
      "the adjustment prompt carries the counts and the previous plan"
      (fun () ->
        let month1 = parse "training_plan_4w.txt"

        let request =
          adjustmentRequest man [] 4 brief month1.Plan [
            ("3dias", 68)
            ("5dias", 104)
          ]

        let prompt = adjustmentPrompt request

        Expect.stringContains
          prompt
          "- option 3dias has exactly 68 exercises in total"
          "count 68"

        Expect.stringContains
          prompt
          "- option 5dias has exactly 104 exercises in total"
          "count 104"

        Expect.stringContains
          prompt
          "PREVIOUS PLAN"
          "the adjustment reference travels"

        Expect.stringContains
          prompt
          "Sentadilla con barra"
          "the previous plan travels complete in the same schema")

    testCase
      "the confirmed memory enters the prompt as an adjustment"
      (fun () ->
        let memory =
          {
            Target = Exercise "Press de banca"
            Label = Light
            Date = DateOnly(2026, 10, 6)
          }
          |> consolidate
          |> confirm [ Load 5 ]

        let prompt = newPlanPrompt(newRequest man [ memory ] 4 brief [])

        Expect.stringContains
          prompt
          "- on Press de banca: load 5"
          "the memory's delta gets fixed")

    testCase
      "the trainer exemption demands the indicative in the notes"
      (fun () ->
        let memory =
          memoryOfExemption {
            Target = Day "Miércoles"
            Technique = "Sentadilla con salto"
          }

        let prompt = newPlanPrompt(newRequest man [ memory ] 4 brief [])

        Expect.stringContains
          prompt
          "- on day Miércoles: variant Sentadilla con salto — trainer exception"
          "the exemption travels with its technique"

        Expect.stringContains
          prompt
          "trainer-prescribed plus the evidence-based reason why it is not generally suggested"
          "it demands the indicative in the notes"

        Expect.stringContains
          system
          "TRAINER EXCEPTIONS: the trainer is the health expert"
          "the system yields to the prescription")

    testCase
      "the consolidation prompt carries the marks and the protocol"
      (fun () ->
        let marks = [
          {
            Target = Exercise "Press de banca"
            Label = Light
            Date = DateOnly(2026, 10, 6)
          }
          {
            Target = Day "Lunes"
            Label = NoEquipment
            Date = DateOnly(2026, 10, 7)
          }
        ]

        let prompt = consolidationPrompt marks

        Expect.stringContains
          prompt
          "- Press de banca | light | 2026-10-06"
          "the exercise's mark goes in"

        Expect.stringContains
          prompt
          "- day Lunes | no-equipment | 2026-10-07"
          "the day's mark goes in"

        Expect.stringContains
          consolidationSystem
          "target | label | summary | delta[;delta...]"
          "the response protocol goes in the system")
  ]
