module Briple.Trainer.Core.Tests.Data.Tests

// The data boundary: the model's JSON converts to the domain or
// accumulates typed failures with their path. The {"data": "..."}
// wrapper unwraps before converting.

#nowarn "3535"

open System
open Expecto
open JDeck
open Briple.Trainer.Core.Plan.Data
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Tests.TestSupport

/// The tests' edge of the decode leaf: the DataFailure list the
/// assertions inspect.
type DataEdge =
  | Invalid of failures: DataFailure list

  interface IPlanDataError<DataEdge> with
    static member InvalidData failures = Invalid failures

let ofJson json : Result<Plan, DataFailure list> =
  match fromJson json: Result<_, DataEdge> with
  | Ok plan -> Ok plan
  | Error(Invalid failures) -> Error failures

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

/// Ok of the decode as an option, for round-trip assertions.
let private convertBack(json: string) =
  match ofJson json with
  | Ok plan -> Some plan
  | Error _ -> None

let tests =
  testList "Data" [
    testCase "the valid json converts to the domain" (fun () ->
      match ofJson validJson with
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
      let json = Encode.string validJson
      let wrapped = $"{{ \"data\": {json.ToJsonString()} }}"

      match ofJson wrapped with
      | Ok plan -> Expect.equal plan.Title "Plan de prueba" "same plan"
      | Error failures -> failtest $"expected Ok: %A{failures}")

    testCase "an unknown gender accumulates a failure" (fun () ->
      let json = validJson.Replace("\"male\"", "\"alien\"")

      match ofJson json with
      | Ok _ -> failtest "expected Error"
      | Error failures ->
        Expect.exists
          failures
          (function
          | BadGender "alien" -> true
          | _ -> false)
          "the failure names the gender")

    testCase
      "an unrecognized day accumulates a failure with its path"
      (fun () ->
        let json = validJson.Replace("\"lunes\"", "\"samedi\"")

        match ofJson json with
        | Ok _ -> failtest "expected Error"
        | Error failures ->
          Expect.exists
            failures
            (function
            | UnknownDay(path, "samedi") -> path.Contains "days[0]"
            | _ -> false)
            "the failure carries the path")

    testCase
      "an exercise without a name is rejected with a failure and path"
      (fun () ->
        let json =
          validJson.Replace("\"name\": \"Sentadilla\"", "\"name\": \" \"")

        match ofJson json with
        | Ok _ -> failtest "expected Error"
        | Error failures ->
          Expect.exists
            failures
            (function
            | EmptyExerciseName path -> path.Contains "exercises[0]"
            | _ -> false)
            "the failure carries the path")

    testCase "a broken json gives an unreadable failure" (fun () ->
      match ofJson "{ esto no es json" with
      | Ok _ -> failtest "expected Error"
      | Error failures ->
        Expect.exists
          failures
          (function
          | UnreadableJson _ -> true
          | _ -> false)
          "unreadable failures")

    testCase "every bad field is reported, not only the first" (fun () ->
      // reps and rir arrive as numbers where strings belong: two bad
      // fields, both must come back
      let json =
        validJson
          .Replace("\"reps\": \"6-8\"", "\"reps\": 68")
          .Replace("\"rir\": \"1-2\"", "\"rir\": 12")

      match ofJson json with
      | Ok _ -> failtest "expected Error"
      | Error failures ->
        let unreadable =
          failures
          |> List.filter(function
            | UnreadableJson _ -> true
            | _ -> false)

        Expect.hasLength unreadable 2 "both bad fields are reported")

    testCase "a missing week number is a failure, not a silent zero" (fun () ->
      let json = validJson.Replace("\"number\": 1,", "\"number\": null,")

      match ofJson json with
      | Ok _ -> failtest "expected Error"
      | Error failures ->
        Expect.exists
          failures
          (function
          | MissingWeekNumber path -> path.Contains "weeks[0]"
          | _ -> false)
          "the absent week number is reported")

    testCase
      "a missing exercise order is a failure, not a silent zero"
      (fun () ->
        let json = validJson.Replace("\"order\": 1,", "\"order\": null,")

        match ofJson json with
        | Ok _ -> failtest "expected Error"
        | Error failures ->
          Expect.exists
            failures
            (function
            | MissingOrder path -> path.Contains "exercises[0]"
            | _ -> false)
            "the absent order is reported")

    testCase "toJson and fromJson round-trip the single-block plan" (fun () ->
      match parse(readFixture "training_plan_4w.txt") with
      | Error error -> failtest $"parse error: {Parse.describe error}"
      | Ok parsed ->
        let manOnly = {
          parsed.Plan with
              Blocks =
                parsed.Plan.Blocks
                |> List.filter(fun block -> block.Gender = Male)
        }

        match toJson(Male, manOnly) |> Option.bind convertBack with
        | Some converted ->
          Expect.equal converted manOnly "the plan survives the round-trip"
        | None -> failtest "expected the male block to serialize")

    testCase "toJson: a plan without the asked gender is None" (fun () ->
      match parse(readFixture "training_plan_4w.txt") with
      | Error error -> failtest $"parse error: {Parse.describe error}"
      | Ok parsed ->
        let empty = { parsed.Plan with Blocks = [] }

        Expect.equal (toJson(Male, empty)) None "nothing to serialize")

    testCase "toJson serializes the block of the asked gender" (fun () ->
      match parse(readFixture "training_plan_4w.txt") with
      | Error error -> failtest $"parse error: {Parse.describe error}"
      | Ok parsed ->
        let mujer = parsed.Plan.Blocks |> List.item 1

        Expect.equal mujer.Gender Female "the fixture's second block"

        match toJson(Female, parsed.Plan) |> Option.bind convertBack with
        | Some converted ->
          Expect.equal
            converted.Blocks
            [ mujer ]
            "the asked block, not the first"
        | None -> failtest "expected the female block to serialize")
  ]
