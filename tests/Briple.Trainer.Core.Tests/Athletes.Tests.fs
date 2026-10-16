module Briple.Trainer.Core.Tests.Athletes.Tests

// The existing plans decompose into two real athletes (the man and the
// woman, one per block) and a new athlete is defined with parameters
// alone, without touching types.

open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Tests.TestSupport

let private twoAthletesOf plan =
  let athletes = athletesOfPlan plan

  Expect.equal athletes.Length 2 "one block per athlete"

  (List.find (fun a -> a.Gender = Male) athletes,
   List.find (fun a -> a.Gender = Female) athletes)

let tests =
  testList "Athletes" [
    testCase "month 1: two athletes, one per block" (fun () ->
      match readFixture "training_plan_4w.txt" |> parse with
      | Error error -> failtest $"parse error: {Parse.describe error}"
      | Ok parsed ->
        let man, woman = twoAthletesOf parsed.Plan

        Expect.equal man.Name "hombre" "the block token names the man"

        Expect.equal woman.Name "mujer" "the block token names the woman"

        Expect.equal man.Goals [] "the plan carries no goals")

    testCase "month 2: the same two athletes" (fun () ->
      match readFixture "month2_4w.txt" |> parse with
      | Error error -> failtest $"parse error: {Parse.describe error}"
      | Ok parsed ->
        let man, _ = twoAthletesOf parsed.Plan

        Expect.equal man.Gender Male "same decomposition as month 1")

    testCase "a third test athlete is defined with parameters alone" (fun () ->
      let third = {
        Name = "Prueba"
        Gender = Female
        Goals = [ Strength ]
      }

      Expect.equal third.Goals [ Strength ] "the goals belong to the profile"

      Expect.equal third.Gender Female "the profile belongs to the athlete")
  ]
