module Briple.Trainer.Core.Tests.Writer.Tests

// The writer's round-trip: writing a parsed plan and parsing it back
// yields the same plan, with no warnings.

open Expecto
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Writer
open Briple.Trainer.Core.Tests.TestSupport

let tests =
  testList "Writer" [
    let roundTrip name =
      testCase $"round-trip of {name}" (fun () ->
        match readFixture name |> parse with
        | Error error -> failtest $"parse error: {Parse.describe error}"
        | Ok first ->
          match write first.Plan |> parse with
          | Error error -> failtest $"re-parse error: {Parse.describe error}"
          | Ok second ->
            Expect.equal
              second.Plan
              first.Plan
              "the plan survives the round-trip"

            Expect.isEmpty second.Warnings "no warnings on re-parse")

    yield roundTrip "training_plan_4w.txt"
    yield roundTrip "month2_4w.txt"
  ]
