module Briple.Trainer.Core.Requests

// The request contract: what travels to the agent on each flow. Requests
// are self-contained — athlete, confirmed memories, period, brief and
// target counts — with no conversation memory; the adjustment adds the
// full previous plan.

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters

/// The trainer's brief: loose adjustments, focus, RIR per week and the
/// variants to generate (a subset of the athlete's).
type Brief = {
  Adjustments: (Target * Delta list) list
  Focus: string list
  RirPerWeek: (int * string) list
  Variants: string list
}

/// Brand-new plan; the target counts per variant travel explicitly and
/// the validator checks them in the output.
type NewPlanRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  Counts: (string * int) list
}

/// Adjustment over the previous plan; the target counts travel
/// explicitly and the validator checks them in the output.
type AdjustmentRequest = {
  Athlete: Athlete
  Memories: Memory list
  Weeks: int
  Brief: Brief
  PreviousPlan: Plan
  Counts: (string * int) list
}

/// The #ej count of each athlete variant in a plan.
val planCounts: athlete: Athlete -> plan: Plan -> (string * int) list

val newRequest:
  athlete: Athlete ->
  memories: Memory list ->
  weeks: int ->
  brief: Brief ->
  counts: (string * int) list ->
    NewPlanRequest

val adjustmentRequest:
  athlete: Athlete ->
  memories: Memory list ->
  weeks: int ->
  brief: Brief ->
  previousPlan: Plan ->
  counts: (string * int) list ->
    AdjustmentRequest

/// The traced parameters of a new plan: profile, memories, brief and
/// counts; with no previous plan, counts always trace to the brief.
val parametersForNew: request: NewPlanRequest -> Parameter list

/// The traced parameters of the adjustment: the new-plan ones plus the
/// counts; a count equal to the previous plan's traces to PreviousPlan,
/// a different one to TrainerBrief.
val parametersForAdjustment: request: AdjustmentRequest -> Parameter list
