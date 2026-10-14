module Briple.Trainer.Core.Tests.Escritor.Tests

// El round-trip del escritor: escribir un plan parseado y volver a
// parsearlo da el mismo plan, sin warnings.

open Expecto
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Escritor
open Briple.Trainer.Core.Tests.TestSupport

let tests =
  testList "Escritor" [
    let roundTrip nombre =
      testCase $"round-trip de {nombre}" (fun () ->
        match readFixture nombre |> parsePlan with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok primero ->
          match escribir primero.Plan |> parsePlan with
          | Error error ->
            failtest $"re-parse error at line {error.Line}: {error.Message}"
          | Ok segundo ->
            Expect.equal
              segundo.Plan
              primero.Plan
              "el plan sobrevive el round-trip"

            Expect.isEmpty segundo.Warnings "sin warnings al re-parsear")

    yield roundTrip "plan_entrenamiento_4sem.txt"
    yield roundTrip "plan_mes2_4sem.txt"
  ]
