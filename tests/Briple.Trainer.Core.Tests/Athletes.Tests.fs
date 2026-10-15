module Briple.Trainer.Core.Tests.Athletes.Tests

// The existing plans decompose into two real athletes (the man with two
// variants, the woman with one) and a new athlete is defined with
// parameters alone, without touching types.

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
    testCase "month 1: two athletes, the man with two variants" (fun () ->
      match readFixture "training_plan_4w.txt" |> parsePlan with
      | Error error -> failtest $"parse error at line {error.Line}"
      | Ok parsed ->
        let man, woman = twoAthletesOf parsed.Plan

        Expect.equal
          man.Variants
          [ "3dias"; "5dias" ]
          "the man's options are his variants, not distinct athletes"

        Expect.equal woman.Variants [ "5dias" ] "the woman trains one variant")

    testCase "month 2: the same two athletes" (fun () ->
      match readFixture "month2_4w.txt" |> parsePlan with
      | Error error -> failtest $"parse error at line {error.Line}"
      | Ok parsed ->
        let man, woman = twoAthletesOf parsed.Plan

        Expect.equal
          man.Variants
          [ "3dias"; "5dias" ]
          "same variants as month 1"

        Expect.equal woman.Variants [ "5dias" ] "one variant")

    testCase "a third test athlete is defined with parameters alone" (fun () ->
      let third = {
        Name = "Prueba"
        Gender = Female
        Variants = [ "4dias" ]
        Goals = [ Strength ]
        Focus = [ "glúteo" ]
      }

      Expect.equal
        third.Variants
        [ "4dias" ]
        "new variants without touching types"

      Expect.equal third.Gender Female "the profile belongs to the athlete")
  ]
