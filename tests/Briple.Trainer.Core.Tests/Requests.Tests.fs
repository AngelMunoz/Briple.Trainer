module Briple.Trainer.Core.Tests.Requests.Tests

// The request contract: the brief carries goal, frequency and emphasis;
// the RIR progression derives from the period; the adjustment inherits
// the counts of the previous plan on the variant it keeps.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Goals
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Tests.TestSupport

let private parseFixture name =
  match readFixture name |> parse with
  | Error error -> failtest $"parse error: {Parse.describe error}"
  | Ok parsed -> parsed

let private confirmedMemory =
  {
    Target = Exercise "Press de banca"
    Label = Light
    Date = DateOnly(2026, 10, 6)
  }
  |> consolidate
  |> confirm [ Load 5 ]

let private man = {
  Name = "hombre"
  Gender = Male
  Goals = []
}

let private manBrief = {
  Goal = Hypertrophy
  Frequency = TresDias
  Emphasis = []
  Adjustments = []
  Language = Spanish
}

let tests =
  testList "Requests" [
    testCase "the RIR progression follows the goal profile" (fun () ->
      Expect.equal
        (rirFor (profileOf Hypertrophy) 4)
        [ (1, "3"); (2, "2"); (3, "1"); (4, "0") ]
        "hypertrophy: 3-2-1-0"

      Expect.equal
        (rirFor (profileOf Hypertrophy) 5)
        [ (1, "3"); (2, "2"); (3, "1"); (4, "0"); (5, "0") ]
        "it floors at the last value"

      Expect.equal
        (rirFor (profileOf CardioVascularHealth) 4)
        [ (1, "3"); (2, "3"); (3, "2"); (4, "2") ]
        "cardio: 3-3-2-2"

      Expect.equal
        (rirFor (profileOf BasicBodyActivation) 2)
        [ (1, "4"); (2, "4") ]
        "activation starts at 4")

    testCase "the brief's tokens are its one frequency" (fun () ->
      Expect.equal (variantTokens manBrief) [ "3dias" ] "tres dias"

      Expect.equal
        (variantTokens { manBrief with Frequency = CincoDias })
        [ "5dias" ]
        "cinco dias")

    testCase "the emphasis catalog depends on the goal" (fun () ->
      Expect.hasLength (Emphasis.optionsFor Hypertrophy) 8 "hypertrophy"

      Expect.hasLength (Emphasis.optionsFor CardioVascularHealth) 3 "cardio"

      Expect.equal
        (Emphasis.optionsFor Hypertrophy |> List.head)
        Arms
        "the hypertrophy catalog starts with arms"

      Expect.equal
        (Emphasis.toToken ContinuousZone2)
        "zone-2 continuous"
        "token")

    testCase "the adjustment inherits the previous counts it keeps" (fun () ->
      let month1 = parseFixture "training_plan_4w.txt"

      let request = {
        Athlete = man
        Memories = [ confirmedMemory ]
        Weeks = 4
        Brief = manBrief
        PreviousPlan = month1.Plan
      }

      let parameters = parametersForAdjustment request

      Expect.contains
        parameters
        (PlanWeeks(4, TrainerBrief))
        "the period travels"

      Expect.contains
        parameters
        (AthleteVariants([ "3dias" ], TrainerBrief))
        "the brief's frequency is the only variant"

      Expect.contains
        parameters
        (VariantCount("3dias", 6, PreviousPlan))
        "3dias keeps its previous 6 per training day"

      Expect.isFalse
        (parameters |> List.contains(VariantCount("5dias", 6, PreviousPlan)))
        "the frequency it drops does not travel"

      Expect.contains
        parameters
        (WeekRir(1, "3", Derived))
        "the derived RIR travels with its origin"

      Expect.contains
        parameters
        (Adjustment(
          Exercise "Press de banca",
          [ Load 5 ],
          ConfirmedMemory confirmedMemory
        ))
        "the confirmed memory travels into the request")

    testCase "the relevant memories follow the scope" (fun () ->
      let planA = Guid.NewGuid()
      let planB = Guid.NewGuid()

      let athleteScoped = {
        consolidate {
          Target = Day "Miércoles"
          Label = Pain
          Date = DateOnly(2026, 10, 1)
        } with
            Status = Confirmed
            Scope = Athlete
      }

      let onPlanA = {
        consolidate {
          Target = Exercise "Sentadilla"
          Label = Light
          Date = DateOnly(2026, 10, 2)
        } with
            Status = Confirmed
            Scope = Plan
      }

      let onPlanB = {
        onPlanA with
            Origin =
              Mark {
                Target = Exercise "Peso muerto"
                Label = Heavy
                Date = DateOnly(2026, 10, 3)
              }
      }

      let stillProposed = { onPlanA with Status = Proposed }

      let confirmed = [
        athleteScoped, ValueNone
        onPlanA, ValueSome planA
        onPlanB, ValueSome planB
        stillProposed, ValueSome planA
      ]

      // adjusting plan A: the athlete trait plus plan A's own fact
      Expect.equal
        (relevantMemories(confirmed, ValueSome planA))
        [ athleteScoped; onPlanA ]
        "the athlete ones plus the active plan's"

      // adjusting plan B: plan A's fact stays behind
      Expect.equal
        (relevantMemories(confirmed, ValueSome planB))
        [ athleteScoped; onPlanB ]
        "only the active plan's facts ride"

      // no active plan: nothing plan-scoped travels
      Expect.equal
        (relevantMemories(confirmed, ValueNone))
        [ athleteScoped ]
        "only the athlete ones")

    testCase "the new plan joins memories and brief without counts" (fun () ->
      let parameters =
        {
          Athlete = man
          Memories = [ confirmedMemory ]
          Weeks = 2
          Brief = manBrief
        }
        |> parametersForNew

      Expect.contains
        parameters
        (AthleteVariants([ "3dias" ], TrainerBrief))
        "the brief picks the variant"

      Expect.contains parameters (PlanWeeks(2, TrainerBrief)) "the period"

      Expect.contains
        parameters
        (WeekRir(1, "3", Derived))
        "the derived RIR travels"

      Expect.isFalse
        (parameters
         |> List.exists(function
           | VariantCount _ -> true
           | _ -> false))
        "a new plan fixes no counts")
  ]
