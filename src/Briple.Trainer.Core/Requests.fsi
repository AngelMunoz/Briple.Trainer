module Briple.Trainer.Core.Requests

// The request contract: what travels to the agent on each flow. Requests
// are self-contained — athlete, confirmed memories, period and brief —
// with no conversation memory; the adjustment adds the full previous
// plan. Counts and RIR per week are no longer trainer inputs: RIR is
// derived from the period, and the counts of the adjustment come from
// the previous plan.

open System
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters

/// The weekly training frequency chosen for one plan. Its file token is
/// the variant id of the .briple format ("3dias").
type VariantFrequency =
  | DosDias
  | TresDias
  | CuatroDias
  | CincoDias
  | SeisDias

module VariantFrequency =

  /// The .briple variant token.
  val toFileToken: VariantFrequency -> string

  /// Reads a .briple variant token.
  val tryFileToken: token: string -> VariantFrequency option

  /// Training days per week.
  val daysPerWeek: VariantFrequency -> int

  /// Every frequency, from two to six days.
  val all: VariantFrequency list

/// The plan's emphasis: a closed catalog, one list per goal. Multiple
/// optional; the goal alone already shapes the plan.
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

  /// The catalog of one goal, in display order.
  val optionsFor: goal: PlanGoal -> Emphasis list

  /// The English prompt token.
  val toToken: Emphasis -> string

/// The trainer's brief: the goal, the weekly frequency and the emphasis
/// of this plan; the confirmed memories arrive as adjustments.
type Brief = {
  Goal: PlanGoal
  Frequency: VariantFrequency
  Emphasis: Emphasis list
  Adjustments: (Target * Delta list) list
}

/// Brand-new plan.
type NewPlanRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
}

/// Adjustment over the previous plan; the counts of the variants the
/// adjustment keeps trace to the previous plan.
type AdjustmentRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  PreviousPlan: Plan
}

/// The mesocycle RIR progression derived from the period: it starts at
/// RIR 3 and drops one per week, floor 0.
val derivedRirPerWeek: weeks: int -> (int * string) list

/// The variant tokens of a brief: the one frequency it carries.
val variantTokens: brief: Brief -> string list

/// The #ej count of each variant in a plan, for the athlete's block.
val planCounts: athlete: Athlete -> plan: Plan -> (string * int) list

/// The confirmed memories a request carries: the athlete-scoped ones
/// (they outlive plans) plus the plan-scoped ones observed on the plan
/// being adjusted. Proposed and discarded never travel.
val relevantMemories:
  confirmed: (Memory * Guid voption) list * activePlan: Guid voption ->
    Memory list

/// The traced parameters of a new plan: profile, confirmed memories and
/// brief, plus the derived RIR progression.
val parametersForNew: request: NewPlanRequest -> Parameter list

/// The traced parameters of the adjustment: the new-plan ones plus the
/// counts of the variants the previous plan shares with the brief's
/// frequency.
val parametersForAdjustment: request: AdjustmentRequest -> Parameter list
