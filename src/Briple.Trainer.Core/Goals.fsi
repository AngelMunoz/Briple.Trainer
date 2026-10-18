module Briple.Trainer.Core.Goals

// The goal as data: volume, effort, structure and progression per plan
// goal. The prompt renders it; the validator checks the numeric fields.
// The English phrases are prompt text owned by Core, not UI prose.

open Briple.Trainer.Core.Athletes

/// How the work of a day is organized.
type Structure =
  | StraightSets
  | Circuit
  | SteadyAndIntervals
  | ShortCircuit

/// What changes from week to week.
type Rotation =
  | WeeklyVariant
  | MainLiftFixed
  | EveryTwoWeeks
  | Minimal

/// The contract of one plan goal.
type GoalProfile = {
  Goal: PlanGoal
  WeeklyVolume: string
  RepRange: string
  Effort: string
  RirWeeks: int list
  CardioMinutes: int
  MaxRowsPerDay: int
  Rest: string
  Structure: Structure
  Rotation: Rotation
}

/// The profile of a goal.
val profileOf: goal: PlanGoal -> GoalProfile

/// The mesocycle RIR of each week: the profile's list, floored at its
/// last value.
val rirFor: profile: GoalProfile -> weeks: int -> (int * string) list

/// The RIR numbers of each week, without the text.
val rirWeeks: profile: GoalProfile -> weeks: int -> int list
