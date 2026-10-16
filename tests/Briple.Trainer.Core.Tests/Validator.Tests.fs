module Briple.Trainer.Core.Tests.Validator.Tests

// The validator of agent outputs: clean format and the request's
// parameters (gender, the frequency variant and its days per week, the
// weeks, the derived RIR); the adjustment also keeps the previous
// counts. The valid outputs are synthetic plans written with the same
// Writer the app uses.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Writer
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator
open Briple.Trainer.Core.Tests.TestSupport

let private man = {
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

let private dayIds = [ "Lunes"; "Martes"; "Miercoles"; "Jueves"; "Viernes" ]

/// A synthetic male plan: variant `perWeek`dias, four weeks, `perWeek`
/// training days, `perDay` exercises per day, the derived mesocycle
/// lines.
let private planOf perWeek perDay : Plan = {
  Version = 1
  Title = "Mes 1"
  Weeks = 4
  Generated = "2026-10-15"
  Origin = "trainer"
  Blocks = [
    {
      Gender = Male
      Variants = [
        {
          Id = $"{perWeek}dias"
          Title = None
          Weeks = [
            for number in 1..4 ->
              {
                Number = number
                Days = [
                  for dayIndex in 0 .. perWeek - 1 ->
                    {
                      Id = dayIds[dayIndex]
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
  Extras = [
    for week, target in derivedRirPerWeek 4 ->
      $"mesociclo | Semana {week}: objetivo | RIR {target}"
  ]
}

let private outputOf(perWeek, perDay) = write(planOf perWeek perDay)

/// The man's fixture output carries two variants.
let private manOutput() =
  let month1 =
    match readFixture "training_plan_4w.txt" |> parsePlan with
    | Error error -> failtest $"parse error at line {error.Line}"
    | Ok parsed -> parsed

  write {
    month1.Plan with
        Blocks = month1.Plan.Blocks |> List.filter(fun b -> b.Gender = Male)
  }

let private expectRule rule verdict =
  match verdict with
  | Valid _ -> failtest $"expected Rejected by {rule}"
  | Rejected violations ->
    Expect.exists
      violations
      (fun violation -> violation.Rule = rule)
      $"a {rule} violation is present"

let tests =
  testList "Validator" [
    testCase "the synthetic output validates as a new plan" (fun () ->
      let request = newRequest man [] 4 brief

      match validateNew request (outputOf(3, 2)) with
      | Valid(plan, _) ->
        Expect.equal plan.Weeks 4 "the verdict carries the plan"
      | Rejected violations -> failtest $"expected Valid: %A{violations}")

    testCase
      "checkPrescription flags qualifiers, missing effort and compound names"
      (fun () ->
        let plan: Plan = {
          Version = 1
          Title = "t"
          Weeks = 1
          Generated = ""
          Origin = ""
          Blocks = [
            {
              Gender = Male
              Variants = [
                {
                  Id = "1dia"
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
                              Laps = "2"
                              Order = 1
                              Name = "Rueda abdominal"
                              Reps = "8-12 reps por ronda"
                              Rir = "-"
                              Rest = "90 s"
                              Notes = "-"
                            }
                            {
                              Circuit = "A"
                              Laps = "2"
                              Order = 2
                              Name = "Plancha / rueda"
                              Reps = "30-45 s"
                              Rir = "1-2"
                              Rest = "90 s"
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

        let advisories = checkPrescription plan

        Expect.hasLength
          advisories
          3
          "qualifier, missing effort, compound name"

        Expect.isTrue
          (advisories
           |> List.forall(fun violation -> violation.Rule = "prescription"))
          "every advisory carries the prescription rule")

    testCase "an output with two blocks is rejected by gender" (fun () ->
      let request = newRequest man [] 4 brief

      let month1 =
        match readFixture "training_plan_4w.txt" |> parsePlan with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed -> parsed

      // the full file also carries the woman's block
      expectRule "gender" (validateNew request (write month1.Plan)))

    testCase "weeks other than the asked ones are rejected" (fun () ->
      let request = newRequest man [] 5 brief

      expectRule "weeks" (validateNew request (outputOf(3, 2))))

    testCase "a variant that was not asked is rejected" (fun () ->
      let request = newRequest man [] 4 brief

      // the fixture carries 3dias and 5dias, the brief asks for 3dias
      expectRule "variants" (validateNew request (manOutput())))

    testCase "a week with the wrong day count is rejected" (fun () ->
      let request = newRequest man [] 4 brief

      // four days per week against a three-day frequency
      expectRule "frequency" (validateNew request (outputOf(4, 2))))

    testCase
      "a mesocycle that does not match the derivation is rejected"
      (fun () ->
        let request = newRequest man [] 4 brief

        let wrong = (outputOf(3, 2)).Replace("RIR 3", "RIR 0")

        expectRule "rir" (validateNew request wrong))

    testCase "an output that is not in the format is rejected" (fun () ->
      let request = newRequest man [] 4 brief

      expectRule "format" (validateNew request "this is not a plan"))

    testCase "an output with format warnings is rejected" (fun () ->
      let request = newRequest man [] 4 brief

      // unknown meta inside the plan: it parses with a warning
      let withWarning =
        (outputOf(3, 2)).Replace("#meta titulo ", "#meta titulox ")

      expectRule "format" (validateNew request withWarning))

    testCase "the adjustment keeps the previous counts" (fun () ->
      let previous = planOf 3 2 // 3 days x 4 weeks x 2 = 24 #ej

      let shrunk = adjustmentRequest man [] 4 brief previous

      expectRule "count" (validateAdjustment shrunk (outputOf(3, 1)))

      let kept = adjustmentRequest man [] 4 brief previous

      match validateAdjustment kept (outputOf(3, 2)) with
      | Valid _ -> ()
      | Rejected violations -> failtest $"expected Valid: %A{violations}")

    testCase "an adjustment over another frequency validates" (fun () ->
      // the previous 3dias, the brief moves to five days: the kept
      // variant set is empty, so no count applies
      let previous = planOf 3 2

      let movedBrief = { brief with Frequency = CincoDias }

      let request = adjustmentRequest man [] 4 movedBrief previous

      match validateAdjustment request (outputOf(5, 2)) with
      | Valid _ -> ()
      | Rejected violations -> failtest $"expected Valid: %A{violations}")
  ]
