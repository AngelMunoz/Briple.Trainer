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
open Briple.Trainer.Core.Goals
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
  Language = Spanish
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
    for week, target in rirFor (profileOf Hypertrophy) 4 ->
      $"mesociclo | Semana {week}: objetivo | RIR {target}"
  ]
}

let private outputOf(perWeek, perDay) = write(planOf perWeek perDay)

/// The man's fixture output carries two variants.
let private manOutput() =
  let month1 =
    match readFixture "training_plan_4w.txt" |> parse with
    | Error error -> failtest $"parse error: {Parse.describe error}"
    | Ok parsed -> parsed

  write {
    month1.Plan with
        Blocks = month1.Plan.Blocks |> List.filter(fun b -> b.Gender = Male)
  }

/// One case predicate per expectation: the violations are a typed union,
/// so the test names the exact case it expects.
let private expectViolation isCase name verdict =
  match verdict with
  | Valid _ -> failtest $"expected Rejected by {name}"
  | Rejected violations ->
    Expect.exists violations isCase $"a {name} violation is present"

let tests =
  testList "Validator" [
    testCase "the synthetic output validates as a new plan" (fun () ->
      let request = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
      }

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
                            {
                              Circuit = "A"
                              Laps = "2"
                              Order = 3
                              Name = "Movilidad de cadera 90/90"
                              Reps = "30-45 s"
                              Rir = "rpe 3-4"
                              Rest = "30 s"
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
           |> List.forall(function
             | Prescription _ -> true
             | _ -> false))
          "every advisory is a prescription finding"

        Expect.isFalse
          (advisories
           |> List.exists(function
             | Prescription(MultipleExercises "Movilidad de cadera 90/90") ->
               true
             | _ -> false))
          "a slash inside a real name is not an alternative")

    testCase "an output with two blocks is rejected by gender" (fun () ->
      let request = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
      }

      let month1 =
        match readFixture "training_plan_4w.txt" |> parse with
        | Error error -> failtest $"parse error: {Parse.describe error}"
        | Ok parsed -> parsed

      // the full file also carries the woman's block
      expectViolation
        (function
        | GenderMismatch _ -> true
        | _ -> false)
        "gender"
        (validateNew request (write month1.Plan)))

    testCase "weeks other than the asked ones are rejected" (fun () ->
      let request = {
        Athlete = man
        Memories = []
        Weeks = 5
        Brief = brief
      }

      expectViolation
        (function
        | WrongWeeks _ -> true
        | _ -> false)
        "weeks"
        (validateNew request (outputOf(3, 2))))

    testCase "a variant that was not asked is rejected" (fun () ->
      let request = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
      }

      // the fixture carries 3dias and 5dias, the brief asks for 3dias
      expectViolation
        (function
        | WrongVariants _ -> true
        | _ -> false)
        "variants"
        (validateNew request (manOutput())))

    testCase "a week with the wrong day count is rejected" (fun () ->
      let request = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
      }

      // four days per week against a three-day frequency
      expectViolation
        (function
        | WrongFrequency _ -> true
        | _ -> false)
        "frequency"
        (validateNew request (outputOf(4, 2))))

    testCase
      "a mesocycle that does not match the derivation is rejected"
      (fun () ->
        let request = {
          Athlete = man
          Memories = []
          Weeks = 4
          Brief = brief
        }

        let wrong = (outputOf(3, 2)).Replace("RIR 3", "RIR 0")

        expectViolation
          (function
          | WrongRir _ -> true
          | _ -> false)
          "rir"
          (validateNew request wrong))

    testCase "an output that is not in the format is rejected" (fun () ->
      let request = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
      }

      expectViolation
        (function
        | BrokenFormat _ -> true
        | _ -> false)
        "broken format"
        (validateNew request "this is not a plan"))

    testCase "an output with format warnings is rejected" (fun () ->
      let request = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
      }

      // unknown meta inside the plan: it parses with a warning
      let withWarning =
        (outputOf(3, 2)).Replace("#meta titulo ", "#meta titulox ")

      expectViolation
        (function
        | FormatWarning _ -> true
        | _ -> false)
        "format warning"
        (validateNew request withWarning))

    testCase "the adjustment keeps the previous counts" (fun () ->
      let previous = planOf 3 2 // 3 days x 4 weeks x 2 = 24 #ej

      let shrunk = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
        PreviousPlan = previous
      }

      expectViolation
        (function
        | CountChanged _ -> true
        | _ -> false)
        "count"
        (validateAdjustment shrunk (outputOf(3, 1)))

      let kept = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = brief
        PreviousPlan = previous
      }

      match validateAdjustment kept (outputOf(3, 2)) with
      | Valid _ -> ()
      | Rejected violations -> failtest $"expected Valid: %A{violations}")

    testCase "an adjustment over another frequency validates" (fun () ->
      // the previous 3dias, the brief moves to five days: the kept
      // variant set is empty, so no count applies
      let previous = planOf 3 2

      let movedBrief = { brief with Frequency = CincoDias }

      let request = {
        Athlete = man
        Memories = []
        Weeks = 4
        Brief = movedBrief
        PreviousPlan = previous
      }

      match validateAdjustment request (outputOf(5, 2)) with
      | Valid _ -> ()
      | Rejected violations -> failtest $"expected Valid: %A{violations}")

    testCase "a timed strength block and a timed RIR are flagged" (fun () ->
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
                            Laps = "1"
                            Order = 1
                            Name = "Sentadilla con barra"
                            Reps = "12 min"
                            Rir = "1"
                            Rest = "2 min"
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

      let findings = checkStructure (profileOf Hypertrophy) plan

      Expect.exists
        findings
        (function
        | TimedStrengthBlock _ -> true
        | _ -> false)
        "the timed strength block is flagged"

      Expect.exists
        findings
        (function
        | TimedEffortIsRir _ -> true
        | _ -> false)
        "the timed RIR is flagged")

    testCase "the cardio minutes count the interval laps" (fun () ->
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
                            Laps = "7"
                            Order = 1
                            Name = "Bicicleta estática"
                            Reps = "3 min"
                            Rir = "rpe 6-7"
                            Rest = "1 min"
                            Notes = "-"
                          }
                          {
                            Circuit = "B"
                            Laps = "3"
                            Order = 1
                            Name = "Sentadilla goblet"
                            Reps = "8-12"
                            Rir = "3"
                            Rest = "75 s"
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

      let findings = checkStructure (profileOf CardioVascularHealth) plan

      Expect.isFalse
        (findings
         |> List.exists(function
           | MissingCardio _ -> true
           | _ -> false))
        "7 x 3 min reaches the 20 min profile")

    testCase "a day over the profile's row cap is flagged" (fun () ->
      let exercises = [
        for order in 1..9 ->
          {
            Circuit = "A"
            Laps = "3"
            Order = order
            Name = $"Ejercicio {order}"
            Reps = "8-10"
            Rir = "2"
            Rest = "2 min"
            Notes = "-"
          }
      ]

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
                        Exercises = exercises
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

      let findings = checkStructure (profileOf Hypertrophy) plan

      Expect.exists
        findings
        (function
        | TooManyRows(_, _, _, 9, 8) -> true
        | _ -> false)
        "the day carries 9 rows and the profile allows 8")

    testCase "the cardio goal accepts its own mesocycle" (fun () ->
      let planFor(targets: (int * string) list) : Plan = {
        Version = 1
        Title = "cardio"
        Weeks = 2
        Generated = "2026-10-17"
        Origin = "test"
        Blocks = [
          {
            Gender = Male
            Variants = [
              {
                Id = "3dias"
                Title = None
                Weeks = [
                  for number in 1..2 ->
                    {
                      Number = number
                      Days = [
                        for dayIndex in 0..2 ->
                          {
                            Id = dayIds[dayIndex]
                            Title = None
                            Notes = []
                            Exercises = [
                              {
                                Circuit = "A"
                                Laps = "1"
                                Order = 1
                                Name = "Caminata en cinta"
                                Reps = "25 min"
                                Rir = "zona 2"
                                Rest = "-"
                                Notes = "-"
                              }
                              {
                                Circuit = "B"
                                Laps = "3"
                                Order = 1
                                Name = "Sentadilla goblet"
                                Reps = "8-12"
                                Rir = "3"
                                Rest = "75 s"
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
          for week, target in targets ->
            $"mesociclo | Semana {week}: objetivo | RIR {target}"
        ]
      }

      let cardioBrief = {
        Goal = CardioVascularHealth
        Frequency = TresDias
        Emphasis = []
        Adjustments = []
        Language = Spanish
      }

      let request = {
        Athlete = man
        Memories = []
        Weeks = 2
        Brief = cardioBrief
      }

      match validateNew request (write(planFor [ (1, "3"); (2, "3") ])) with
      | Valid _ -> ()
      | Rejected violations -> failtest $"expected Valid: %A{violations}"

      match validateNew request (write(planFor [ (1, "3"); (2, "2") ])) with
      | Rejected violations ->
        Expect.exists
          violations
          (function
          | WrongRir _ -> true
          | _ -> false)
          "the hypertrophy progression is rejected for cardio"
      | Valid _ -> failtest "the hypertrophy progression must be rejected")
  ]
