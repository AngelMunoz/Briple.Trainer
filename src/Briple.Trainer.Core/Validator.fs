module Briple.Trainer.Core.Validator

open System
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Prescription
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Goals
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

  for week, target in rirFor (profileOf brief.Goal) weeks do
    match tryMesocycleRir plan week with
    | Some actual when actual = target -> ()
    | Some actual -> violations.Add(WrongRir(week, actual, target))
    | None -> violations.Add(MissingRir(week, target))

  List.ofSeq violations

/// The adjustment keeps the previous plan's exercises per training day
/// on the variant it adjusts; a shorter period does not double the day.
let checkAdjustmentCounts
  (athlete: Athlete)
  (previousPlan: Plan)
  (plan: Plan)
  : Violation list =
  let kept = planCounts athlete plan |> List.map fst |> Set.ofList

  planCounts athlete previousPlan
  |> List.filter(fun (variant, _) -> Set.contains variant kept)
  |> List.choose(fun (variant, _) ->
    let expected = perDayCount athlete variant previousPlan
    let actual = perDayCount athlete variant plan

    if actual = expected then
      None
    else
      Some(CountChanged(variant, expected, actual)))

/// The prescription findings of one row: a compound name, a dose that is
/// not a pure range or duration, and an effort target inconsistent with
/// the dose. One row can carry more than one.
let advisoriesOf(exercise: Exercise) : PrescriptionAdvisory list = [
  // an alternative in the name, not a slash inside a real name ("90/90")
  if exercise.Name.Contains " / " then
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


// The modalities a timed row may name when the prescription is cardio.
let cardioWords = [
  "caminata"
  "cinta"
  "bicicleta"
  "elíptica"
  "eliptica"
  "remo erg"
  "ergómetro"
  "ergometro"
  "natación"
  "natacion"
  "trote"
  "marcha"
  "intervalos"
  "escaladora"
]

let isCardio(name: string) =
  let lowered = name.ToLowerInvariant()

  cardioWords |> List.exists(fun word -> lowered.Contains word)

let timedSeconds(exercise: Exercise) : int option =
  match tryParseDose exercise.Reps: Result<_, PrescriptionAdvisory> with
  | Ok(Timed(_, hi)) -> Some hi
  | _ -> None

let isRirEffort(exercise: Exercise) =
  match tryParseEffort exercise.Rir with
  | ValueSome(Effort.Rir _) -> true
  | _ -> false

let lapsOf(exercise: Exercise) =
  match Int32.TryParse exercise.Laps with
  | true, value -> max 1 value
  | _ -> 1

[<Struct>]
type DayContext = {
  Profile: GoalProfile
  Variant: Variant
  Week: Week
  Day: Day
}

/// The structural findings of one day of one week.
let checkDay(ctx: DayContext) : Violation list = [
  let day = ctx.Day
  let profile = ctx.Profile

  if day.Exercises.Length > profile.MaxRowsPerDay then
    TooManyRows(
      ctx.Variant.Id,
      ctx.Week.Number,
      day.Id,
      day.Exercises.Length,
      profile.MaxRowsPerDay
    )

  for exercise in day.Exercises do
    match timedSeconds exercise with
    | Some seconds when seconds >= 300 && not(isCardio exercise.Name) ->
      TimedStrengthBlock(day.Id, exercise.Name, seconds / 60)
    | _ -> ()

    match timedSeconds exercise with
    | Some _ when
      isRirEffort exercise || exercise.Rir.ToLowerInvariant().Contains "rir"
      ->
      TimedEffortIsRir(day.Id, exercise.Name, exercise.Rir)
    | _ -> ()

  for (circuit, rows) in day.Exercises |> List.groupBy(fun e -> e.Circuit) do
    if
      rows |> List.map(fun e -> e.Laps) |> List.distinct |> List.length > 1
    then
      CircuitLapsDiffer(day.Id, circuit)

  if profile.CardioMinutes > 0 then
    let minutes =
      day.Exercises
      |> List.filter(fun exercise -> isCardio exercise.Name)
      |> List.sumBy(fun exercise ->
        match timedSeconds exercise with
        | Some seconds -> lapsOf exercise * seconds / 60
        | None -> 0)

    if minutes < profile.CardioMinutes then
      MissingCardio(day.Id, minutes, profile.CardioMinutes)
]

/// The goal's structure rules over the whole plan.
let checkStructure (profile: GoalProfile) (plan: Plan) : Violation list = [
  for block in plan.Blocks do
    for variant in block.Variants do
      for week in variant.Weeks do
        for day in week.Days do
          yield!
            checkDay {
              Profile = profile
              Variant = variant
              Week = week
              Day = day
            }
]

let validateNew (request: NewPlanRequest) (output: string) =
  match parsePlan output: Result<_, Violation> with
  | Error failure -> Rejected [ failure ]
  | Ok parsed ->
    let formatViolations =
      parsed.Warnings |> List.map(fun warning -> FormatWarning warning)

    let parameterViolations =
      checkParameters request.Athlete request.Weeks request.Brief parsed.Plan

    match formatViolations @ parameterViolations with
    | [] ->
      let profile = profileOf request.Brief.Goal

      Valid(
        parsed.Plan,
        checkPrescription parsed.Plan @ checkStructure profile parsed.Plan
      )
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
