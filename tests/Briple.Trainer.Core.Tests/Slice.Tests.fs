module Briple.Trainer.Core.Tests.Slice.Tests

// The import slice over the real month-1 fixture: the two genders and
// their variants come from the file, and every expected count is read
// off the parsed tree.

open Expecto
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Slice
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Tests.TestSupport

let private blockOf(plan: Plan, gender: Gender) =
  plan.Blocks |> List.find(fun b -> b.Gender = gender)

let private variantOf(block: Block, id: string) =
  block.Variants |> List.find(fun v -> v.Id = id)

let private rowsOf(variant: Variant) =
  variant.Weeks
  |> List.sumBy(fun week ->
    week.Days |> List.sumBy(fun day -> day.Exercises.Length))

let private parsed() =
  match readFixture "training_plan_4w.txt" |> Parse.parse with
  | Ok parsed -> parsed.Plan
  | Error failure ->
    failtest $"unexpected parse error: {Parse.describe failure}"

let private genderVariants(plan: Plan) =
  plan.Blocks
  |> List.collect(fun b -> b.Variants |> List.map(fun v -> b.Gender, v.Id))

let tests =
  testList "Plan slice" [
    testCase
      "the slice keeps one block, one variant and the metadata"
      (fun () ->
        let plan = parsed()
        let man = blockOf(plan, Male)
        let threeDays = variantOf(man, "3dias")

        let expected = {
          plan with
              Blocks = [ { man with Variants = [ threeDays ] } ]
        }

        Expect.equal
          (variant(Male, "3dias", plan))
          (Some expected)
          "the sliced plan")

    testCase "the man 3dias slice keeps every row" (fun () ->
      let plan = parsed()
      let threeDays = variantOf(blockOf(plan, Male), "3dias")

      // 6 rows a day, 3 days a week, 4 weeks
      Expect.equal (rowsOf threeDays) 72 "the fixture rows of man 3dias"

      match variant(Male, "3dias", plan) with
      | Some sliced ->
        Expect.equal
          (sliced |> Tree.exercisesOf |> Seq.length)
          72
          "the slice rows"

        Expect.equal
          (genderVariants sliced)
          [ (Male, "3dias") ]
          "one block, one variant"
      | None -> failtest "the slice was None")

    testCase "the woman 5dias slice changes the block" (fun () ->
      let plan = parsed()
      let fiveDays = variantOf(blockOf(plan, Female), "5dias")

      match variant(Female, "5dias", plan) with
      | Some sliced ->
        Expect.equal (List.length sliced.Blocks) 1 "one block"

        Expect.equal
          (genderVariants sliced)
          [ (Female, "5dias") ]
          "the woman block"

        Expect.equal
          (sliced |> Tree.exercisesOf |> Seq.length)
          (rowsOf fiveDays)
          "the slice rows"
      | None -> failtest "the slice was None")

    testCase "a missing gender or variant slices to None" (fun () ->
      let plan = parsed()

      Expect.equal (variant(Male, "2dias", plan)) None "no such man variant"

      Expect.equal
        (variant(Female, "3dias", plan))
        None
        "no such woman variant")

    testCase "the slice round-trips through the writer" (fun () ->
      let plan = parsed()

      match variant(Male, "5dias", plan) with
      | None -> failtest "the slice was None"
      | Some sliced ->
        match Briple.Trainer.Core.Plan.Writer.write sliced |> Parse.parse with
        | Error failure ->
          failtest $"unexpected parse error: {Parse.describe failure}"
        | Ok again ->
          Expect.equal again.Warnings [] "no warnings on the written slice"

          Expect.equal
            again.Plan.Blocks
            sliced.Blocks
            "the blocks survive the write")
  ]
