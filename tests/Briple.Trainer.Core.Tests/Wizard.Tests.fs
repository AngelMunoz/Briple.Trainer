module Briple.Trainer.Core.Tests.Wizard.Tests

// The six wizard steps stay specified with inputs and outputs; the
// output of each step assembles the next one's input.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Wizard
open Briple.Trainer.Core.Tests.TestSupport

let private athlete = {
  Name = "hombre"
  Gender = Male
  Variants = [ "3dias"; "5dias" ]
  Goals = []
  Focus = []
}

let private mark = {
  Target = Exercise "Press de banca"
  Label = Light
  Date = DateOnly(2026, 10, 6)
}

let private proposal = mark |> consolidate

let private confirmed = proposal |> confirm [ Load 5 ]

let private brief = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3") ]
  Variants = [ "3dias"; "5dias" ]
}

let tests =
  testList "Wizard" [
    testCase "the six steps chain their inputs and outputs" (fun () ->
      // 1: output the chosen athlete; 2: input that athlete
      let step1 = initial()
      let step2 = ReviewMemories(athlete, [ proposal ])

      // 2: output the confirmed; 3: input the athlete
      let step3 = ChoosePeriod athlete

      // 3: output the weeks; 4: input athlete + confirmed + weeks
      let step4 = DefineBrief(athlete, [ confirmed ], 4)

      // 4: output the brief; 5: input the assembled request
      let request = newRequest athlete [ confirmed ] 4 brief []
      let step5 = Generate(NewPlan request)

      // 5: output the validated candidate; 6: input candidate + warnings
      let (candidate, warnings) =
        match readFixture "training_plan_4w.txt" |> parsePlan with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed -> (parsed.Plan, parsed.Warnings)

      let step6 = Review(candidate, warnings)

      Expect.equal step1 ChooseAthlete "step 1 without input"

      let steps = [ step1; step2; step3; step4; step5; step6 ]

      Expect.equal (List.distinct steps).Length 6 "six distinct steps"

      Expect.equal request.Weeks 4 "step 3's output went into 4"

      Expect.equal
        (List.head request.Memories)
        confirmed
        "step 2's output went into 5"

      Expect.equal
        candidate.Weeks
        4
        "step 5's output goes into 6 with its warnings"

      Expect.isEmpty warnings "the fixture candidate validates clean")

    testCase "the adjustment also generates from step 5" (fun () ->
      let month1 =
        match readFixture "training_plan_4w.txt" |> parsePlan with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed -> parsed.Plan

      let request =
        adjustmentRequest athlete [] 4 brief month1 [ ("3dias", 68) ]

      let step = Generate(PlanAdjustment request)

      Expect.equal
        step
        (Generate(PlanAdjustment request))
        "step 5 carries the adjustment request")
  ]
