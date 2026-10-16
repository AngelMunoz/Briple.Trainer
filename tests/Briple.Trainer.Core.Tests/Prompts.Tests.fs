module Briple.Trainer.Core.Tests.Prompts.Tests

// The prompt render: the schema, the fixed part (weeks, the frequency
// variant, the derived RIR, the kept counts of the adjustment), the
// emphasis and the forbidden part.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Prompts

let private man = {
  Name = "Pepe"
  Gender = Male
  Goals = [ Strength ]
}

let private brief = {
  Goal = Hypertrophy
  Frequency = TresDias
  Emphasis = []
  Adjustments = []
}

/// One male block, variant 3dias, one week, one exercise.
let private tinyPlan: Plan = {
  Version = 1
  Title = "anterior"
  Weeks = 1
  Generated = ""
  Origin = ""
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
                  Title = None
                  Notes = []
                  Exercises = [
                    {
                      Circuit = "A"
                      Laps = "3"
                      Order = 1
                      Name = "Press de banca"
                      Reps = "6-8"
                      Rir = "1-2"
                      Rest = "3 min"
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

let tests =
  testList "Prompts" [
    testCase
      "the new-plan prompt carries schema, fixed and forbidden"
      (fun () ->
        let request = newRequest man [] 4 brief

        let prompt = newPlanPrompt request

        Expect.stringContains
          prompt
          "- the plan covers 4 weeks"
          "the weeks get fixed"

        Expect.stringContains
          prompt
          "- the plan has one option per variant: 3dias"
          "the frequency variant gets fixed"

        Expect.stringContains
          prompt
          "- week 1 targets RIR 3 (added automatically, do not write it)"
          "the derived RIR gets fixed"

        Expect.isFalse
          (prompt.Contains "exercises in total")
          "a new plan fixes no counts"

        Expect.stringContains prompt "FORBIDDEN" "the prohibitions travel"

        Expect.stringContains
          (system Hypertrophy)
          "RESPONSE SCHEMA"
          "the system carries the json schema"

        Expect.stringContains
          (system Hypertrophy)
          "\"exercises\": ["
          "the schema declares the exercises"

        Expect.stringContains
          (system Hypertrophy)
          "EVIDENCE-BASED TRAINING"
          "the system demands evidence"

        Expect.stringContains
          (system Hypertrophy)
          "VARIANT ROTATION"
          "the system demands variant rotation"

        Expect.stringContains
          (system Hypertrophy)
          "Respond with the JSON only"
          "the output instruction goes in the system")

    testCase "the athlete line carries the goals and no focus" (fun () ->
      let prompt = newPlanPrompt(newRequest man [] 4 brief)

      Expect.stringContains prompt "Goals: strength." "the goals travel"

      Expect.isFalse (prompt.Contains "Focus:") "the focus is gone")

    testCase "the emphasis travels when the brief picks it" (fun () ->
      let withEmphasis = { brief with Emphasis = [ Arms; Back ] }

      let prompt = newPlanPrompt(newRequest man [] 4 withEmphasis)

      Expect.stringContains prompt "EMPHASIS: arms, back." "the emphasis"

      let bare = newPlanPrompt(newRequest man [] 4 brief)

      Expect.isFalse (bare.Contains "EMPHASIS") "nothing when empty")

    testCase "the goal selects the evidence block" (fun () ->
      Expect.stringContains
        (system Hypertrophy)
        "GOAL: HYPERTROPHY"
        "hypertrophy block"

      Expect.stringContains
        (system WeightLoss)
        "GOAL: WEIGHT LOSS"
        "weight loss block"

      Expect.stringContains
        (system CardioVascularHealth)
        "GOAL: CARDIOVASCULAR HEALTH"
        "cardio block"

      Expect.stringContains
        (system BasicBodyActivation)
        "GOAL: BASIC BODY ACTIVATION"
        "activation block"

      Expect.stringContains (system Strength) "GOAL: STRENGTH" "strength block"

      Expect.isFalse
        ((system WeightLoss).Contains "GOAL: HYPERTROPHY")
        "goals do not mix")

    testCase
      "the adjustment prompt carries the kept counts and the previous plan"
      (fun () ->
        let request = adjustmentRequest man [] 4 brief tinyPlan

        let prompt = adjustmentPrompt request

        Expect.stringContains
          prompt
          "- option 3dias has exactly 1 exercises in total"
          "the kept count"

        Expect.stringContains
          prompt
          "PREVIOUS PLAN"
          "the adjustment reference travels"

        Expect.stringContains
          prompt
          "Press de banca"
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

        let prompt = newPlanPrompt(newRequest man [ memory ] 4 brief)

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

        let prompt = newPlanPrompt(newRequest man [ memory ] 4 brief)

        Expect.stringContains
          prompt
          "- on day Miércoles: variant Sentadilla con salto — trainer exception"
          "the exemption travels with its technique"

        Expect.stringContains
          prompt
          "trainer-prescribed plus the evidence-based reason why it is not generally suggested"
          "it demands the indicative in the notes"

        Expect.stringContains
          (system Hypertrophy)
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
