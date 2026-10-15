module Briple.Trainer.Core.Tests.Requests.Tests

// "Month 2" is expressed as an adjustment over month 1; the target
// counts 68/104/136 travel in the request as traced parameters.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Tests.TestSupport

let private parseFixture name =
  match readFixture name |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}: {error.Message}"
  | Ok parsed -> parsed

let private confirmedMemory =
  {
    Target = Exercise "Press de banca"
    Label = Light
    Date = DateOnly(2026, 10, 6)
  }
  |> consolidate
  |> confirm [ Load 5 ]

let private manBrief = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3"); (2, "2-3"); (3, "2"); (4, "1") ]
  Variants = [ "3dias"; "5dias" ]
}

let tests =
  testList "Requests" [
    testCase
      "month 2 is expressed as an adjustment over month 1 with counts 68/104/136"
      (fun () ->
        let month1 = parseFixture "training_plan_4w.txt"
        let month2 = parseFixture "month2_4w.txt"

        let man =
          athletesOfPlan month1.Plan |> List.find(fun a -> a.Gender = Male)

        let woman =
          athletesOfPlan month1.Plan |> List.find(fun a -> a.Gender = Female)

        let manCounts = planCounts man month2.Plan
        let womanCounts = planCounts woman month2.Plan

        Expect.equal
          manCounts
          [ ("3dias", 68); ("5dias", 104) ]
          "target counts of the man"

        Expect.equal
          womanCounts
          [ ("5dias", 136) ]
          "target counts of the woman"

        let request =
          adjustmentRequest
            man
            [ confirmedMemory ]
            4
            manBrief
            month1.Plan
            manCounts

        let parameters = parametersForAdjustment request

        Expect.contains
          parameters
          (PlanWeeks(4, TrainerBrief))
          "the period travels"

        Expect.contains
          parameters
          (AthleteVariants([ "3dias"; "5dias" ], TrainerBrief))
          "the brief's variants"

        Expect.contains
          parameters
          (VariantCount("3dias", 68, TrainerBrief))
          "68 brief: it changes against month 1 (72)"

        Expect.contains
          parameters
          (VariantCount("5dias", 104, TrainerBrief))
          "104 brief: it changes against month 1 (72)"

        Expect.contains
          parameters
          (Adjustment(
            Exercise "Press de banca",
            [ Load 5 ],
            ConfirmedMemory confirmedMemory
          ))
          "the confirmed memory travels into the request")

    testCase
      "a count equal to the previous plan's traces to PreviousPlan"
      (fun () ->
        let month1 = parseFixture "training_plan_4w.txt"

        let man =
          athletesOfPlan month1.Plan |> List.find(fun a -> a.Gender = Male)

        let sameCounts = planCounts man month1.Plan

        let request =
          adjustmentRequest man [] 4 manBrief month1.Plan sameCounts

        Expect.contains
          (parametersForAdjustment request)
          (VariantCount("3dias", 72, PreviousPlan))
          "unchanged comes from the previous plan")

    testCase "the new plan joins profile, memories, brief and counts" (fun () ->
      let month1 = parseFixture "training_plan_4w.txt"

      let man =
        athletesOfPlan month1.Plan |> List.find(fun a -> a.Gender = Male)

      let brief = { manBrief with Variants = [ "3dias" ] }

      let parameters =
        newRequest man [ confirmedMemory ] 2 brief [ ("3dias", 36) ]
        |> parametersForNew

      Expect.contains
        parameters
        (AthleteVariants([ "3dias"; "5dias" ], AthleteProfile))
        "the profile fixes all of its variants"

      Expect.contains
        parameters
        (AthleteVariants([ "3dias" ], TrainerBrief))
        "the brief picks which ones to generate"

      Expect.contains
        parameters
        (PlanWeeks(2, TrainerBrief))
        "the brief's period"

      Expect.contains
        parameters
        (WeekRir(1, "3", TrainerBrief))
        "the brief's RIR per week"

      Expect.contains
        parameters
        (VariantCount("3dias", 36, TrainerBrief))
        "the brief's target counts")
  ]
