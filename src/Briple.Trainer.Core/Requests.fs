module Briple.Trainer.Core.Requests

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters

type Brief = {
  Goal: PlanGoal
  Adjustments: (Target * Delta list) list
  Focus: string list
  RirPerWeek: (int * string) list
  Variants: string list
}

type NewPlanRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  Counts: (string * int) list
}

type AdjustmentRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  PreviousPlan: Plan
  Counts: (string * int) list
}

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

let newRequest athlete memories weeks brief counts : NewPlanRequest = {
  Athlete = athlete
  Memories = memories
  Weeks = weeks
  Brief = brief
  Counts = counts
}

let adjustmentRequest
  athlete
  memories
  weeks
  brief
  previousPlan
  counts
  : AdjustmentRequest = {
  Athlete = athlete
  Memories = memories
  Weeks = weeks
  Brief = brief
  PreviousPlan = previousPlan
  Counts = counts
}

let ofBrief (weeks: int) (brief: Brief) : Parameter list = [
  PlanWeeks(weeks, TrainerBrief)
  AthleteVariants(brief.Variants, TrainerBrief)
  yield!
    brief.RirPerWeek
    |> List.map(fun (week, target) -> WeekRir(week, target, TrainerBrief))
  yield!
    brief.Adjustments
    |> List.map(fun (target, deltas) ->
      Adjustment(target, deltas, TrainerBrief))
]

let parametersForNew(request: NewPlanRequest) : Parameter list =
  let counts =
    request.Counts
    |> List.map(fun (variant, total) ->
      VariantCount(variant, total, TrainerBrief))

  ofProfile request.Athlete
  @ ofConfirmedMemories request.Memories
  @ ofBrief request.Weeks request.Brief
  @ counts

let countParameter
  (athlete: Athlete)
  (previousPlan: Plan)
  (variant, total)
  : Parameter =
  let previous =
    planCounts athlete previousPlan |> List.tryFind(fun (id, _) -> id = variant)

  match previous with
  | Some(_, previousTotal) when previousTotal = total ->
    VariantCount(variant, total, PreviousPlan)
  | _ -> VariantCount(variant, total, TrainerBrief)

let parametersForAdjustment(request: AdjustmentRequest) : Parameter list =
  let baseParameters =
    ofProfile request.Athlete
    @ ofConfirmedMemories request.Memories
    @ ofBrief request.Weeks request.Brief

  baseParameters
  @ (request.Counts
     |> List.map(countParameter request.Athlete request.PreviousPlan))
