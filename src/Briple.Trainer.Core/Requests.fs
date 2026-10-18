module Briple.Trainer.Core.Requests

open System
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Goals

type VariantFrequency =
  | DosDias
  | TresDias
  | CuatroDias
  | CincoDias
  | SeisDias

module VariantFrequency =

  let toFileToken frequency =
    match frequency with
    | DosDias -> "2dias"
    | TresDias -> "3dias"
    | CuatroDias -> "4dias"
    | CincoDias -> "5dias"
    | SeisDias -> "6dias"

  let tryFileToken(token: string) =
    match token.Trim().ToLowerInvariant() with
    | "2dias" -> Some DosDias
    | "3dias" -> Some TresDias
    | "4dias" -> Some CuatroDias
    | "5dias" -> Some CincoDias
    | "6dias" -> Some SeisDias
    | _ -> None

  let daysPerWeek frequency =
    match frequency with
    | DosDias -> 2
    | TresDias -> 3
    | CuatroDias -> 4
    | CincoDias -> 5
    | SeisDias -> 6

  let all = [ DosDias; TresDias; CuatroDias; CincoDias; SeisDias ]

type Emphasis =
  | Arms
  | Shoulders
  | Chest
  | Back
  | Legs
  | Glutes
  | CoreWork
  | FullBody
  | Squat
  | Deadlift
  | BenchPress
  | OverheadPress
  | OlympicLifts
  | Push
  | Pull
  | FullBodyCircuit
  | Hiit
  | StrengthCardio
  | ContinuousZone2
  | IntervalHiit
  | Mixed
  | Mobility
  | Stability
  | MovementPatterns

module Emphasis =

  // the closed catalog decided with the trainer: one list per goal
  let optionsFor(goal: PlanGoal) : Emphasis list =
    match goal with
    | Hypertrophy -> [
        Arms
        Shoulders
        Chest
        Back
        Legs
        Glutes
        CoreWork
        FullBody
      ]
    | Strength -> [
        Squat
        Deadlift
        BenchPress
        OverheadPress
        OlympicLifts
        Push
        Pull
        FullBody
      ]
    | WeightLoss -> [ FullBodyCircuit; Hiit; StrengthCardio; CoreWork ]
    | CardioVascularHealth -> [ ContinuousZone2; IntervalHiit; Mixed ]
    | BasicBodyActivation -> [ Mobility; Stability; CoreWork; MovementPatterns ]

  let toToken emphasis =
    match emphasis with
    | Arms -> "arms"
    | Shoulders -> "shoulders"
    | Chest -> "chest"
    | Back -> "back"
    | Legs -> "legs"
    | Glutes -> "glutes"
    | CoreWork -> "core"
    | FullBody -> "full body"
    | Squat -> "squat"
    | Deadlift -> "deadlift"
    | BenchPress -> "bench press"
    | OverheadPress -> "overhead press"
    | OlympicLifts -> "olympic lifts"
    | Push -> "push"
    | Pull -> "pull"
    | FullBodyCircuit -> "full-body circuit"
    | Hiit -> "hiit"
    | StrengthCardio -> "strength + cardio"
    | ContinuousZone2 -> "zone-2 continuous"
    | IntervalHiit -> "hiit intervals"
    | Mixed -> "mixed"
    | Mobility -> "mobility"
    | Stability -> "stability"
    | MovementPatterns -> "movement patterns"

  let structureHint(emphasis: Emphasis) : string option =
    match emphasis with
    | FullBodyCircuit -> Some "circuit: 4-6 exercises, 3 rounds, short rest"
    | Hiit
    | IntervalHiit ->
      Some
        "intervals: laps equal the repeats; each round is a work duration and the rest is the recovery"
    | ContinuousZone2 -> Some "one continuous cardio block of at least 20 min"
    | StrengthCardio -> Some "one strength circuit plus one cardio block"
    | Mobility
    | Stability -> Some "timed holds at rpe 3-5"
    | _ -> None

type Brief = {
  Goal: PlanGoal
  Frequency: VariantFrequency
  Emphasis: Emphasis list
  Adjustments: (Target * Delta list) list
}

type NewPlanRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
}

type AdjustmentRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  PreviousPlan: Plan
}

let variantTokens(brief: Brief) : string list = [
  VariantFrequency.toFileToken brief.Frequency
]

let planCounts (athlete: Athlete) (plan: Plan) : (string * int) list =
  plan.Blocks
  |> List.tryFind(fun block -> block.Gender = athlete.Gender)
  |> Option.map(fun block ->
    block.Variants
    |> List.map(fun variant ->
      variant.Id,
      variant.Weeks
      |> List.sumBy(fun week ->
        week.Days |> List.sumBy(fun day -> day.Exercises.Length))))
  |> Option.defaultValue []

let dayCount (athlete: Athlete) (variant: string) (plan: Plan) : int =
  plan.Blocks
  |> List.tryFind(fun block -> block.Gender = athlete.Gender)
  |> Option.bind(fun block ->
    block.Variants |> List.tryFind(fun candidate -> candidate.Id = variant))
  |> Option.map(fun variant ->
    variant.Weeks |> List.sumBy(fun week -> week.Days.Length))
  |> Option.defaultValue 0

let perDayCount (athlete: Athlete) (variant: string) (plan: Plan) : int =
  match
    planCounts athlete plan |> List.tryFind(fun (id, _) -> id = variant)
  with
  | Some(_, total) ->
    let days = dayCount athlete variant plan

    if days > 0 then
      int(Math.Round(float total / float days))
    else
      total
  | None -> 0

/// The confirmed memories a request carries: the athlete-scoped ones
/// (they outlive plans) plus the plan-scoped ones observed on the plan
/// being adjusted.
let relevantMemories
  (confirmed: (Memory * Guid voption) list, activePlan: Guid voption)
  : Memory list =
  confirmed
  |> List.filter(fun (memory, planId) ->
    memory.Status = Confirmed
    && (memory.Scope = Athlete || (memory.Scope = Plan && planId = activePlan)))
  |> List.map fst

let ofBrief (weeks: int) (brief: Brief) : Parameter list = [
  PlanWeeks(weeks, TrainerBrief)
  AthleteVariants(variantTokens brief, TrainerBrief)
  yield!
    rirFor (profileOf brief.Goal) weeks
    |> List.map(fun (week, target) -> WeekRir(week, target, Derived))
  yield!
    brief.Adjustments
    |> List.map(fun (target, deltas) ->
      Adjustment(target, deltas, TrainerBrief))
]

let parametersForNew(request: NewPlanRequest) : Parameter list =
  ofConfirmedMemories request.Memories @ ofBrief request.Weeks request.Brief

let parametersForAdjustment(request: AdjustmentRequest) : Parameter list =
  let frequency = variantTokens request.Brief

  let previousCounts =
    planCounts request.Athlete request.PreviousPlan
    |> List.filter(fun (variant, _) -> List.contains variant frequency)
    |> List.map(fun (variant, _) ->
      VariantCount(
        variant,
        perDayCount request.Athlete variant request.PreviousPlan,
        PreviousPlan
      ))

  ofConfirmedMemories request.Memories
  @ ofBrief request.Weeks request.Brief
  @ previousCounts
