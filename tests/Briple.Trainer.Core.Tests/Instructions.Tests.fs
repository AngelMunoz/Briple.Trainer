module Briple.Trainer.Core.Tests.Instructions.Tests

// The parameters → instructions mapping: the fixed part comes from the
// request, the asked part is the inventory's agent-origin fields, and
// the forbidden part depends on the flow: the new plan owns its volume,
// the adjustment keeps the previous counts.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Instructions

let private maleAthlete = {
  Name = "hombre"
  Gender = Male
  Goals = []
}

let private brief = {
  Goal = Hypertrophy
  Frequency = TresDias
  Emphasis = []
  Adjustments = []
}

/// One male block, variant 3dias, one week, `perDay` exercises per day.
let private planWith perDay : Plan = {
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
                    for order in 1..perDay ->
                      {
                        Circuit = "A"
                        Laps = "3"
                        Order = order
                        Name = $"Ejercicio {order}"
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

let private agentFields = [
  Title
  Generated
  FileOrigin
  VariantTitle
  DayTitle
  DayNotes
  Circuit
  Laps
  Order
  Name
  Reps
  Rir
  Rest
  Notes
  Extras
]

let tests =
  testList "Instructions" [
    testCase
      "the adjustment fixes the previous counts and forbids changing them"
      (fun () ->
        let request = adjustmentRequest maleAthlete [] 4 brief (planWith 2)

        let instructions = forAdjustment request

        Expect.contains
          instructions.Fix
          (VariantCount("3dias", 2, PreviousPlan))
          "the kept counts get fixed"

        Expect.equal
          instructions.Ask
          agentFields
          "the agent produces only the fields of its origin"

        Expect.equal
          instructions.Forbid
          [ ChangeCounts; OutsideVariants; LeaveFormat ]
          "the adjustment touches neither counts, options nor format")

    testCase "the new plan owns its volume" (fun () ->
      let instructions = forNew(newRequest maleAthlete [] 4 brief)

      Expect.isFalse
        (instructions.Fix
         |> List.exists(function
           | VariantCount _ -> true
           | _ -> false))
        "no counts get fixed"

      Expect.equal
        instructions.Forbid
        [ OutsideVariants; LeaveFormat ]
        "the new plan touches neither options nor format")

    testCase "the weeks and the frequency get fixed in both flows" (fun () ->
      let newPlan = forNew(newRequest maleAthlete [] 4 brief)

      Expect.contains newPlan.Fix (PlanWeeks(4, TrainerBrief)) "weeks fixed"

      Expect.contains
        newPlan.Fix
        (AthleteVariants([ "3dias" ], TrainerBrief))
        "the frequency variant is fixed")
  ]
