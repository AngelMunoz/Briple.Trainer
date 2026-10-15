module Briple.Trainer.Core.Tests.Data.Tests

// The data boundary: the model's JSON converts to the domain or
// accumulates failures with their path. The {"data": "..."} wrapper
// unwraps before converting.

open System
open System.Text.Json
open Expecto
open Briple.Trainer.Core.Plan.Data
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Tests.TestSupport

let validJson =
  """
{
  "title": "Plan de prueba",
  "weeks": 2,
  "generated": "2026-10-14",
  "origin": "spike",
  "gender": "male",
  "variants": [
    {
      "id": "3dias",
      "title": "Full body",
      "weeks": [
        {
          "number": 1,
          "days": [
            {
              "id": "lunes",
              "title": "A",
              "exercises": [
                {
                  "circuit": "A",
                  "laps": "4",
                  "order": 1,
                  "name": "Sentadilla",
                  "reps": "6-8",
                  "rir": "1-2",
                  "rest": "3 min",
                  "notes": "-"
                }
              ]
            }
          ]
        }
      ]
    }
  ],
  "extras": [ "guia | calienta 10 min" ]
}
"""

let tests =
  testList "Data" [
    testCase "the valid json converts to the domain" (fun () ->
      match fromJson validJson with
      | Ok plan ->
        Expect.equal plan.Title "Plan de prueba" "title"
        Expect.equal plan.Weeks 2 "weeks"
        Expect.equal plan.Blocks.Length 1 "one block"

        let block = List.head plan.Blocks

        Expect.equal block.Gender Male "converted gender"

        let variant = List.head block.Variants

        Expect.equal variant.Id "3dias" "variant"

        let week = List.head variant.Weeks
        let day = List.head week.Days

        Expect.equal day.Id "lunes" "lowercase day"

        let exercise = List.head day.Exercises

        Expect.equal exercise.Name "Sentadilla" "exercise"
        Expect.equal exercise.Order 1 "integer order"

        Expect.equal plan.Extras [ "guia | calienta 10 min" ] "extras"
      | Error failures -> failtest $"expected Ok: %A{failures}")

    testCase "a json wrapped in data unwraps" (fun () ->
      let wrapped = $"{{ \"data\": {JsonSerializer.Serialize validJson} }}"

      match fromJson wrapped with
      | Ok plan -> Expect.equal plan.Title "Plan de prueba" "same plan"
      | Error failures -> failtest $"expected Ok: %A{failures}")

    testCase "an unknown gender accumulates a failure" (fun () ->
      let json = validJson.Replace("\"male\"", "\"alien\"")

      match fromJson json with
      | Ok _ -> failtest "expected Error"
      | Error failures ->
        Expect.exists
          failures
          (fun failure -> failure.Contains "gender")
          "the failure names the gender")

    testCase
      "an unrecognized day accumulates a failure with its path"
      (fun () ->
        let json = validJson.Replace("\"lunes\"", "\"samedi\"")

        match fromJson json with
        | Ok _ -> failtest "expected Error"
        | Error failures ->
          Expect.exists
            failures
            (fun failure ->
              failure.Contains "days[0]" && failure.Contains "samedi")
            "the failure carries the path")

    testCase
      "an exercise without a name is rejected with a failure and path"
      (fun () ->
        let json =
          validJson.Replace("\"name\": \"Sentadilla\"", "\"name\": \" \"")

        match fromJson json with
        | Ok _ -> failtest "expected Error"
        | Error failures ->
          Expect.exists
            failures
            (fun failure ->
              failure.Contains "exercises[0]" && failure.Contains "empty name")
            "the failure carries the path")

    testCase "a broken json gives an unreadable failure" (fun () ->
      match fromJson "{ esto no es json" with
      | Ok _ -> failtest "expected Error"
      | Error failures ->
        Expect.exists
          failures
          (fun failure -> failure.Contains "unreadable")
          "unreadable failures")

    testCase "toJson and fromJson round-trip the single-block plan" (fun () ->
      match readFixture "training_plan_4w.txt" |> parsePlan with
      | Error error -> failtest $"parse error: {error.Message}"
      | Ok parsed ->
        let manOnly = {
          parsed.Plan with
              Blocks =
                parsed.Plan.Blocks
                |> List.filter(fun block -> block.Gender = Male)
        }

        match toJson manOnly |> fromJson with
        | Ok converted ->
          Expect.equal converted manOnly "the plan survives the round-trip"
        | Error failures -> failtest $"expected Ok: %A{failures}")
  ]
