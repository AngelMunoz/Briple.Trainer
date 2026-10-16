module Briple.Trainer.Core.Validator

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Prescription
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests

type Verdict =
  | Valid of plan: Plan * advisories: Violation list
  | Rejected of Violation list

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
  | _ -> violations.Add(GenderMismatch(athlete.Gender, plan.Blocks.Length))

  if plan.Weeks <> weeks then
    violations.Add(WrongWeeks(weeks, plan.Weeks))

  let variants =
    plan.Blocks
    |> List.filter(fun block -> block.Gender = athlete.Gender)
    |> List.collect(fun block -> block.Variants)

  let expected = List.sort(variantTokens brief)
  let actual = variants |> List.map(fun variant -> variant.Id) |> List.sort

  if expected <> actual then
    violations.Add(WrongVariants(expected, actual))

  let days = VariantFrequency.daysPerWeek brief.Frequency

  for variant in variants do
    let numbers = variant.Weeks |> List.map(fun week -> week.Number)

    if numbers <> [ 1..weeks ] then
      violations.Add(BadVariantWeeks(variant.Id, numbers, weeks))

    for week in variant.Weeks do
      if week.Days.Length <> days then
        violations.Add(
          WrongFrequency(variant.Id, week.Number, week.Days.Length, days)
        )

  for week, target in derivedRirPerWeek weeks do
    match tryMesocycleRir plan week with
    | Some actual when actual = target -> ()
    | Some actual -> violations.Add(WrongRir(week, actual, target))
    | None -> violations.Add(MissingRir(week, target))

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
    | Some actual -> Some(CountChanged(variant, total, actual))
    | None -> None)

/// The prescription findings of one row: a compound name, a dose that is
/// not a pure range or duration, and an effort target inconsistent with
/// the dose. One row can carry more than one.
let advisoriesOf(exercise: Exercise) : PrescriptionAdvisory list = [
  if exercise.Name.Contains "/" then
    MultipleExercises exercise.Name

  match tryParseDose exercise.Reps: Result<_, PrescriptionAdvisory> with
  | Error advisory -> advisory
  | Ok dose ->
    match tryParseEffort exercise.Rir with
    | ValueNone ->
      match dose with
      | Reps _ -> RepsWithoutEffort exercise.Name
      | Timed _ -> ()
    | ValueSome(Rir _) ->
      match dose with
      | Timed _ -> TimedWithRir exercise.Name
      | Reps _ -> ()
    | ValueSome _ -> ()
]

/// Reviews the prescription of every exercise row: one exercise per
/// name, a pure dose, and an effort target consistent with the dose.
/// Advisory only: the violations never reject the verdict, because real
/// source plans carry these patterns and the adjustment flow inherits
/// them.
let checkPrescription(plan: Plan) : Violation list =
  plan
  |> Tree.exercisesOf
  |> Seq.collect advisoriesOf
  |> Seq.map Prescription
  |> List.ofSeq

let validateNew (request: NewPlanRequest) (output: string) =
  match parsePlan output: Result<_, Violation> with
  | Error failure -> Rejected [ failure ]
  | Ok parsed ->
    let formatViolations =
      parsed.Warnings |> List.map(fun warning -> FormatWarning warning)

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
