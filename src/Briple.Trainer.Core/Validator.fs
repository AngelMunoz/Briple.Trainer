module Briple.Trainer.Core.Validator

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Prescription
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests

type Violation = { Rule: string; Detail: string }

type Verdict =
  | Valid of plan: Plan * advisories: Violation list
  | Rejected of Violation list

let violation rule detail = { Rule = rule; Detail = detail }

/// Checks a plan already converted to the domain against the parameters
/// of the request: gender, the brief's frequency, the weeks and the
/// derived RIR.
let checkParameters
  (athlete: Athlete)
  (weeks: int)
  (brief: Brief)
  (plan: Plan)
  : Violation list =
  let violations = ResizeArray<Violation>()

  match plan.Blocks with
  | [ block ] when block.Gender = athlete.Gender -> ()
  | _ ->
    violations.Add(
      violation
        "gender"
        $"expected only the {Gender.toProtocol athlete.Gender} block, found {plan.Blocks.Length}"
    )

  if plan.Weeks <> weeks then
    violations.Add(
      violation "weeks" $"expected {weeks}, the plan declares {plan.Weeks}"
    )

  let variants =
    plan.Blocks
    |> List.filter(fun block -> block.Gender = athlete.Gender)
    |> List.collect(fun block -> block.Variants)

  let expected = List.sort(variantTokens brief)
  let actual = variants |> List.map(fun variant -> variant.Id) |> List.sort

  if expected <> actual then
    violations.Add(
      violation "variants" $"expected {expected}, the plan has {actual}"
    )

  let days = VariantFrequency.daysPerWeek brief.Frequency

  for variant in variants do
    let numbers = variant.Weeks |> List.map(fun week -> week.Number)

    if numbers <> [ 1..weeks ] then
      violations.Add(
        violation
          "variant-weeks"
          $"the variant {variant.Id} has weeks {numbers}, expected 1..{weeks}"
      )

    for week in variant.Weeks do
      if week.Days.Length <> days then
        violations.Add(
          violation
            "frequency"
            $"the variant {variant.Id} trains {week.Days.Length} days in week {week.Number}, expected {days}"
        )

  for week, target in derivedRirPerWeek weeks do
    match tryMesocycleRir plan week with
    | Some actual when actual = target -> ()
    | Some actual ->
      violations.Add(
        violation
          "rir"
          $"week {week} must declare RIR {target}, declares {actual}"
      )
    | None ->
      violations.Add(
        violation "rir" $"week {week} does not declare RIR {target}"
      )

  List.ofSeq violations

/// The adjustment keeps the previous plan's exercise counts on the
/// variant it adjusts.
let checkAdjustmentCounts
  (athlete: Athlete)
  (previousPlan: Plan)
  (plan: Plan)
  : Violation list =
  let kept = planCounts athlete plan |> Map.ofList

  planCounts athlete previousPlan
  |> List.filter(fun (variant, _) -> Map.containsKey variant kept)
  |> List.choose(fun (variant, total) ->
    match Map.tryFind variant kept with
    | Some actual when actual = total -> None
    | Some actual ->
      Some(
        violation
          "count"
          $"the variant {variant} must keep {total} #ej, has {actual}"
      )
    | None -> None)

/// Reviews the prescription of every exercise row: one exercise per
/// name, a pure dose, and an effort target consistent with the dose.
/// Advisory only: the violations never reject the verdict, because real
/// source plans carry these patterns and the adjustment flow inherits
/// them.
let checkPrescription(plan: Plan) : Violation list =
  let advisories = ResizeArray<Violation>()

  for block in plan.Blocks do
    for variant in block.Variants do
      for week in variant.Weeks do
        for day in week.Days do
          for exercise in day.Exercises do
            if exercise.Name.Contains "/" then
              advisories.Add(
                violation
                  "prescription"
                  $"the name holds more than one exercise: '{exercise.Name}' (alternatives go in notes)"
              )

            match tryParseDose exercise.Reps with
            | Error detail -> advisories.Add(violation "prescription" detail)
            | Ok dose ->
              match tryParseEffort exercise.Rir with
              | Error detail -> advisories.Add(violation "prescription" detail)
              | Ok ValueNone ->
                match dose with
                | Reps _ ->
                  advisories.Add(
                    violation
                      "prescription"
                      $"rep-based exercise without an effort target: '{exercise.Name}'"
                  )
                | Timed _ -> ()
              | Ok(ValueSome(Rir _)) ->
                match dose with
                | Timed _ ->
                  advisories.Add(
                    violation
                      "prescription"
                      $"timed exercise with an RIR target instead of rpe or zone: '{exercise.Name}'"
                  )
                | Reps _ -> ()
              | Ok(ValueSome _) -> ()

  List.ofSeq advisories

let validateNew (request: NewPlanRequest) (output: string) =
  match parsePlan output with
  | Error error ->
    Rejected [ violation "format" $"line {error.Line}: {error.Message}" ]
  | Ok parsed ->
    let formatViolations =
      parsed.Warnings
      |> List.map(fun warning ->
        violation "format" $"line {warning.Line}: {warning.Message}")

    let parameterViolations =
      checkParameters request.Athlete request.Weeks request.Brief parsed.Plan

    match formatViolations @ parameterViolations with
    | [] -> Valid(parsed.Plan, checkPrescription parsed.Plan)
    | violations -> Rejected violations

/// Validates the output of an adjustment: the same, plus the counts of
/// the previous plan on the variant the adjustment keeps.
let validateAdjustment (request: AdjustmentRequest) (output: string) =
  let asNew = {
    Athlete = request.Athlete
    Memories = request.Memories
    Weeks = request.Weeks
    Brief = request.Brief
  }

  match validateNew asNew output with
  | Rejected violations -> Rejected violations
  | Valid(plan, advisories) ->
    match checkAdjustmentCounts request.Athlete request.PreviousPlan plan with
    | [] -> Valid(plan, advisories)
    | violations -> Rejected violations
