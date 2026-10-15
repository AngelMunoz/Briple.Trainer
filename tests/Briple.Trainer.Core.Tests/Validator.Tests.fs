module Briple.Trainer.Core.Tests.Validator.Tests

// The validator of agent outputs: clean format and the request's
// parameters (gender, variants, weeks, counts, RIR). The test output is
// the man's fixture, which fulfills everything asked.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Writer
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator
open Briple.Trainer.Core.Tests.TestSupport

let private parse name =
  match readFixture name |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}"
  | Ok parsed -> parsed

let private man = {
  Name = "hombre"
  Gender = Male
  Variants = [ "3dias"; "5dias" ]
  Goals = []
  Focus = []
}

let private brief = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3"); (2, "2-3"); (4, "1") ]
  Variants = [ "3dias"; "5dias" ]
}

/// The man's fixture as the test output: his block only.
let private manOutput() =
  let month1 = parse "training_plan_4w.txt"

  let manOnly = {
    month1.Plan with
        Blocks = month1.Plan.Blocks |> List.filter(fun b -> b.Gender = Male)
  }

  write manOnly

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
    testCase "the fixture output validates as a new plan" (fun () ->
      let request = newRequest man [] 4 brief [ ("3dias", 72); ("5dias", 72) ]

      match validateNew request (manOutput()) with
      | Valid(plan, _) ->
        Expect.equal plan.Weeks 4 "the verdict carries the plan"
      | Rejected violations -> failtest $"expected Valid: %A{violations}")

    testCase
      "the fixture output validates with inherited prescription advisories"
      (fun () ->
        let request =
          newRequest man [] 4 brief [ ("3dias", 72); ("5dias", 72) ]

        match validateNew request (manOutput()) with
        | Valid(_, advisories) ->
          Expect.isNonEmpty advisories "the source plan's patterns advise"

          Expect.exists
            advisories
            (fun violation -> violation.Detail.Contains "Plancha")
            "the inherited compound name advises"
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
      let request = newRequest man [] 4 brief []
      let month1 = parse "training_plan_4w.txt"

      // the full file also carries the woman's block
      expectRule "gender" (validateNew request (write month1.Plan)))

    testCase "weeks other than the asked ones are rejected" (fun () ->
      let request = newRequest man [] 5 brief []

      expectRule "weeks" (validateNew request (manOutput())))

    testCase "a variant that was not asked is rejected" (fun () ->
      let oneVariantBrief = { brief with Variants = [ "3dias" ] }

      let request = newRequest man [] 4 oneVariantBrief []

      expectRule "variants" (validateNew request (manOutput())))

    testCase "a count that is not met is rejected" (fun () ->
      let month1 = parse "training_plan_4w.txt"

      let request =
        adjustmentRequest man [] 4 brief month1.Plan [ ("3dias", 68) ]

      expectRule "count" (validateAdjustment request (manOutput())))

    testCase "an unmet count is also rejected in a new plan" (fun () ->
      let request = newRequest man [] 4 brief [ ("3dias", 68) ]

      expectRule "count" (validateNew request (manOutput())))

    testCase "met counts validate the adjustment" (fun () ->
      let month1 = parse "training_plan_4w.txt"

      let request =
        adjustmentRequest man [] 4 brief month1.Plan [
          ("3dias", 72)
          ("5dias", 72)
        ]

      match validateAdjustment request (manOutput()) with
      | Valid _ -> ()
      | Rejected violations -> failtest $"expected Valid: %A{violations}")

    testCase "a RIR that does not match is rejected" (fun () ->
      let rirBrief = {
        brief with
            RirPerWeek = [ (1, "0-0") ]
      }

      let request = newRequest man [] 4 rirBrief []

      expectRule "rir" (validateNew request (manOutput())))

    testCase "an output that is not in the format is rejected" (fun () ->
      let request = newRequest man [] 4 brief []

      expectRule "format" (validateNew request "this is not a plan"))

    testCase "an output with format warnings is rejected" (fun () ->
      let request = newRequest man [] 4 brief []

      // unknown meta inside the plan: it parses with a warning
      let withWarning =
        (manOutput()).Replace("#meta titulo ", "#meta titulox ")

      expectRule "format" (validateNew request withWarning))
  ]
