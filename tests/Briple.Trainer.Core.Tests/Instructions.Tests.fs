module Briple.Trainer.Core.Tests.Instructions.Tests

// The parameters → instructions mapping: the fixed part comes from the
// request, the asked part is the inventory's agent-origin fields, and
// the forbidden part depends on the flow.

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
  Variants = [ "3dias"; "5dias" ]
  Goals = []
  Focus = []
}

let private brief = {
  Goal = Hypertrophy
  Adjustments = []
  Focus = []
  RirPerWeek = []
  Variants = [ "3dias"; "5dias" ]
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
      "the adjustment fixes the parameters, asks the agent fields and forbids three things"
      (fun () ->
        let candidate: Plan = {
          Version = 1
          Title = "anterior"
          Weeks = 4
          Generated = ""
          Origin = ""
          Blocks = []
          Extras = []
        }

        let request =
          adjustmentRequest maleAthlete [] 4 brief candidate [ ("3dias", 68) ]

        let instructions = forAdjustment request

        Expect.contains
          instructions.Fix
          (VariantCount("3dias", 68, TrainerBrief))
          "the counts get fixed"

        Expect.equal
          instructions.Ask
          agentFields
          "the agent produces only the fields of its origin"

        Expect.equal
          instructions.Forbid
          [ ChangeCounts; OutsideVariants; LeaveFormat ]
          "the adjustment touches neither counts, options nor format")

    testCase "the new plan fixes and forbids its counts" (fun () ->
      let instructions =
        forNew(newRequest maleAthlete [] 4 brief [ ("3dias", 68) ])

      Expect.contains
        instructions.Fix
        (VariantCount("3dias", 68, TrainerBrief))
        "the brief's counts get fixed"

      Expect.equal
        instructions.Forbid
        [ ChangeCounts; OutsideVariants; LeaveFormat ]
        "the new plan touches neither counts, options nor format")

    testCase "the weeks and the variants get fixed in both flows" (fun () ->
      let newPlan = forNew(newRequest maleAthlete [] 4 brief [])

      Expect.contains newPlan.Fix (PlanWeeks(4, TrainerBrief)) "weeks fixed"

      Expect.contains
        newPlan.Fix
        (AthleteVariants([ "3dias"; "5dias" ], TrainerBrief))
        "variants fixed")
  ]
