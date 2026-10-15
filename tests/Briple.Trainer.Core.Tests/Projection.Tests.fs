module Briple.Trainer.Core.Tests.Projection.Tests

// Expecto port of the browser-lane projection tests. Positive asserts
// run against the real fixture (anchor Monday 2026-09-07); edge cases use
// synthetic plans built inline.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Tests.TestSupport

let anchor = DateOnly(2026, 9, 7) // Monday

// --- Synthetic plan builders -----------------------------------------------

let ej nombre = {
  Circuit = "A"
  Laps = "4"
  Order = 1
  Name = nombre
  Reps = "8-10"
  Rir = "2"
  Rest = "90 s"
  Notes = "-"
}

let dia id titulo dias = {
  Id = id
  Title = titulo
  Notes = []
  Exercises = dias
}

let semana numero dias = { Number = numero; Days = dias }

let opcion id semanas = {
  Id = id
  Title = None
  Weeks = semanas
}

let bloque genero opciones = { Gender = genero; Variants = opciones }

let plan semanas generos = {
  Version = 1
  Title = "test plan"
  Weeks = semanas
  Generated = ""
  Origin = ""
  Blocks = generos
  Extras = []
}

// Male / 3dias: Lun · Mié · Vie (Full Body A/B/C), one week, for anchor tests
let threeDayWeek weekNumber =
  semana weekNumber [
    dia "Lunes" (Some "Full Body A") [ ej "sentadilla" ]
    dia "Miércoles" (Some "Full Body B") [ ej "peso muerto" ]
    dia "Viernes" (Some "Full Body C") [ ej "remo" ]
  ]

let threeDayPlan =
  plan 4 [ bloque Male [ opcion "3dias" [ threeDayWeek 1; threeDayWeek 2 ] ] ]

let tests =
  testList "Projection" [
    testCase
      "fixture: session, week bounds, trainsOn, nextSession, mesociclo"
      (fun () ->
        let text = readFixture "plan_entrenamiento_4sem.txt"

        match parsePlan text with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed ->
          let p = parsed.Plan

          // 2026-09-14 falls in plan week 2; 2026-09-16 is Full Body B
          Expect.equal
            (planWeek p anchor (DateOnly(2026, 9, 14)))
            (Some 2)
            "week of 09-14"

          match trySession p Male "3dias" anchor (DateOnly(2026, 9, 16)) with
          | None -> failtest "expected a session on 09-16"
          | Some dia ->
            Expect.equal dia.Title (Some "Full Body B") "09-16 session"

          // Before and after the plan
          Expect.equal
            (trySession p Male "3dias" anchor (DateOnly(2026, 9, 6)))
            None
            "before anchor"

          Expect.equal
            (trySession p Male "3dias" anchor (DateOnly(2026, 10, 5)))
            None
            "after week 4"

          Expect.equal
            (planWeek p anchor (DateOnly(2026, 10, 5)))
            None
            "planWeek after plan"

          // 3 días trains Mon / Wed / Fri
          Expect.isTrue
            (trainsOn p Male "3dias" anchor (DateOnly(2026, 9, 14)))
            "trains Monday"

          Expect.isFalse
            (trainsOn p Male "3dias" anchor (DateOnly(2026, 9, 15)))
            "rest Tuesday"

          Expect.isTrue
            (trainsOn p Male "3dias" anchor (DateOnly(2026, 9, 18)))
            "trains Friday"

          // Rest-day line: next session from Saturday 09-12
          match nextSession p Male "3dias" anchor (DateOnly(2026, 9, 12)) with
          | None -> failtest "expected a next session after Saturday"
          | Some(nextDate, dia) ->
            Expect.isTrue
              (sameDate nextDate (DateOnly(2026, 9, 14)))
              "next session is Monday 09-14"

            Expect.equal dia.Title (Some "Full Body A") "next session title"

          // Mesociclo RIR targets from the #x extras
          Expect.equal (tryMesocycleRir p 1) (Some "3") "mesociclo week 1"
          Expect.equal (tryMesocycleRir p 2) (Some "2-3") "mesociclo week 2"
          Expect.equal (tryMesocycleRir p 4) (Some "1") "mesociclo week 4"
          Expect.equal (tryMesocycleRir p 5) None "week 5 has no RIR segment")

    testCase "custom (non-Monday) anchor shifts every window" (fun () ->
      let wednesday = DateOnly(2026, 9, 9) // anchor mid-week

      // Week 1 covers 09-09..09-15, so Monday 09-14 is still week 1
      Expect.equal
        (planWeek threeDayPlan wednesday (DateOnly(2026, 9, 14)))
        (Some 1)
        "09-14 in week 1"

      Expect.equal
        (planWeek threeDayPlan wednesday (DateOnly(2026, 9, 16)))
        (Some 2)
        "09-16 in week 2"

      // 09-14 (Monday) resolves through week 1's Lunes dia
      match
        trySession threeDayPlan Male "3dias" wednesday (DateOnly(2026, 9, 14))
      with
      | None -> failtest "expected a session on 09-14"
      | Some dia ->
        Expect.equal dia.Title (Some "Full Body A") "09-14 session via week 1")

    testCase
      "defaultAnchor: this Monday when the variant trains today, else next Monday"
      (fun () ->
        // Wednesday 09-09: the 3-day variant trains today, so this week's Monday
        Expect.isTrue
          (sameDate
            (defaultAnchor threeDayPlan Male "3dias" (DateOnly(2026, 9, 9)))
            (DateOnly(2026, 9, 7)))
          "trains today -> this Monday"

        // Tuesday 09-08: no session today, so next week's Monday
        Expect.isTrue
          (sameDate
            (defaultAnchor threeDayPlan Male "3dias" (DateOnly(2026, 9, 8)))
            (DateOnly(2026, 9, 14)))
          "rest today -> next Monday"

        // Monday itself counts as "this week's Monday"
        Expect.isTrue
          (sameDate
            (defaultAnchor threeDayPlan Male "3dias" (DateOnly(2026, 9, 7)))
            (DateOnly(2026, 9, 7)))
          "today is Monday")

    testCase "planFinish: one day past the last covered date" (fun () ->
      let finish = planFinish threeDayPlan anchor

      Expect.isTrue
        (sameDate finish (DateOnly(2026, 10, 5)))
        "4 weeks from 09-07 finish on 10-05"

      // The day before is still inside the plan; the finish reads Completed.
      Expect.equal
        (planWeek threeDayPlan anchor (addDays finish -1))
        (Some 4)
        "the day before the finish is week 4"

      Expect.equal
        (planChipState threeDayPlan anchor finish)
        Completed
        "the finish date itself is the first free day")

    testCase
      "chainedAnchor: the latest finish wins when it lands after the heuristic"
      (fun () ->
        // The Monday heuristic from Wednesday 09-09 is 09-07; a queued plan
        // finishing 10-12 pushes the start there.
        let decide finishes =
          chainedAnchor {
            Plan = threeDayPlan
            View = (Male, "3dias")
            Today = DateOnly(2026, 9, 9)
            Finishes = finishes
          }

        Expect.isTrue
          (sameDate
            (decide [ DateOnly(2026, 10, 12) ])
            (DateOnly(2026, 10, 12)))
          "queued finish -> the day after it"

        Expect.isTrue
          (sameDate
            (decide [ DateOnly(2026, 10, 12); DateOnly(2026, 11, 9) ])
            (DateOnly(2026, 11, 9)))
          "the latest of several queued finishes wins")

    testCase
      "chainedAnchor: empty finishes keep the heuristic; past finishes never backdate"
      (fun () ->
        let heuristic =
          defaultAnchor threeDayPlan Male "3dias" (DateOnly(2026, 9, 9))

        let decide finishes =
          chainedAnchor {
            Plan = threeDayPlan
            View = (Male, "3dias")
            Today = DateOnly(2026, 9, 9)
            Finishes = finishes
          }

        Expect.isTrue
          (sameDate (decide []) heuristic)
          "no plans -> the Monday heuristic"

        // Every stored plan completed long ago: stay near today.
        Expect.isTrue
          (sameDate (decide [ DateOnly(2026, 8, 3) ]) heuristic)
          "completed plans never backdate")

    testCase "unmapped Dia.Id never matches a weekday" (fun () ->
      let weirdWeeks =
        semana 1 [
          dia "Samedi" (Some "Weird") [ ej "x" ]
          dia "Lunes" (Some "Normal") [ ej "y" ]
        ]

      let saturdayPlan =
        plan 2 [ bloque Male [ opcion "1dia" [ weirdWeeks; weirdWeeks ] ] ]

      // Saturday 09-12: "Samedi" is unmapped, so the date is rest
      Expect.equal
        (trySession saturdayPlan Male "1dia" anchor (DateOnly(2026, 9, 12)))
        None
        "unmapped day skipped"

      // nextSession from Saturday skips Samedi and lands on Monday 09-14
      match
        nextSession saturdayPlan Male "1dia" anchor (DateOnly(2026, 9, 12))
      with
      | None -> failtest "expected to find Monday's session"
      | Some(_, dia) ->
        Expect.equal dia.Title (Some "Normal") "skips unmapped day")

    testCase
      "short opcion: weeks beyond its Semanas render rest, planWeek still counts"
      (fun () ->
        let shortPlan =
          plan 4 [
            bloque Male [ opcion "3dias" [ threeDayWeek 1; threeDayWeek 2 ] ]
          ]

        // Week 3 (09-21..09-27): the opcion has no week there
        Expect.equal
          (planWeek shortPlan anchor (DateOnly(2026, 9, 21)))
          (Some 3)
          "plan still counts week 3"

        Expect.equal
          (tryWeek shortPlan Male "3dias" anchor (DateOnly(2026, 9, 21)))
          None
          "opcion has no week 3"

        Expect.equal
          (trySession shortPlan Male "3dias" anchor (DateOnly(2026, 9, 21)))
          None
          "rest in week 3"

        // Inside the opcion's own range it resolves normally
        match
          tryWeek shortPlan Male "3dias" anchor (DateOnly(2026, 9, 14))
        with
        | None -> failtest "expected week 2 of the opcion"
        | Some semana -> Expect.equal semana.Number 2 "week 2 of the opcion")

    testCase "malformed mesociclo extras yield None" (fun () ->
      let p = {
        threeDayPlan with
            Extras = [
              "mesociclo | Semana 1: no RIR segment here"
              "mesociclo | Semana 2: bad | RIR"
              "mesociclo | other shape entirely"
              "decision | not a mesociclo"
            ]
      }

      Expect.equal (tryMesocycleRir p 1) None "no RIR segment"
      Expect.equal (tryMesocycleRir p 2) None "empty RIR token"
      Expect.equal (tryMesocycleRir p 3) None "no Semana 3 header"
      Expect.equal (tryMesocycleRir p 4) None "no mesociclo for week 4")

    testCase
      "weekRows: seven rows, three sessions, four rest, today bar"
      (fun () ->
        let text = readFixture "plan_entrenamiento_4sem.txt"

        match parsePlan text with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed ->
          let rows =
            weekRows
              parsed.Plan
              Male
              "3dias"
              anchor
              (DateOnly(2026, 9, 16))
              (DateOnly(2026, 9, 14))

          Expect.equal rows.Length 7 "seven rows"

          Expect.equal
            rows.[0].Date
            (DateOnly(2026, 9, 14))
            "first row is the Monday"

          let sessionRows = rows |> List.filter(fun row -> row.Session.IsSome)
          Expect.equal sessionRows.Length 3 "three sessions in week 2"
          Expect.equal (rows.Length - sessionRows.Length) 4 "four rest rows"

          Expect.isTrue rows.[2].IsToday "Wednesday carries the today bar"
          Expect.isFalse rows.[1].IsToday "Tuesday is not today"
          Expect.isTrue rows.[1].Session.IsNone "Tuesday projects as rest"

          match sessionRows.[0].Session with
          | None -> failtest "Monday row carries a dia"
          | Some dia ->
            Expect.equal dia.Title (Some "Full Body A") "Monday title")

    testCase "planChipState: before, inside, after" (fun () ->
      let text = readFixture "plan_entrenamiento_4sem.txt"

      match parsePlan text with
      | Error error -> failtest $"parse error at line {error.Line}"
      | Ok parsed ->
        let p = parsed.Plan

        Expect.equal
          (planChipState p anchor (DateOnly(2026, 9, 6)))
          (StartsOn anchor)
          "before the plan names the start date"

        Expect.equal
          (planChipState p anchor (DateOnly(2026, 9, 14)))
          (Inside(2, 4))
          "inside reads week 2 of 4"

        Expect.equal
          (planChipState p anchor (DateOnly(2026, 10, 5)))
          Completed
          "after the plan is done")
  ]
