module Briple.Trainer.Core.Memories

open System

open Briple.Trainer.Core.Athletes

/// Quick marks available on an exercise or a day.
type Label =
  | Light
  | Heavy
  | Pain
  | NoEquipment

/// The target of a mark inside the active plan.
type Target =
  | Exercise of name: string
  | Day of id: string

/// A quick mark recorded on the active plan.
type FeedbackMark = {
  Target: Target
  Label: Label
  Date: DateOnly
}

/// Trainer exemption: a technique the evidence rules do not suggest,
/// prescribed on purpose. The trainer does not justify it; the system
/// writes the why into the exercise notes.
type Exemption = { Target: Target; Technique: string }

/// The origin of a memory: an athlete's mark or a trainer exemption.
type MemoryOrigin =
  | Mark of FeedbackMark
  | Exemption of exemption: Exemption

/// Predefined parametric adjustment: the five the trainer answers with.
/// The numeric ones are relative changes; load is a percentage.
type Delta =
  | Rir of change: int
  | Reps of change: int
  | Sets of change: int
  | Load of percentage: int
  | PatternVariant of name: string
  | Duration of minutes: int
  | Zone of change: int
  | Modality of name: string

/// Life cycle of a memory.
type MemoryStatus =
  | Proposed
  | Confirmed
  | Discarded

/// How long a memory lives: the plan it was observed on, or the athlete.
type MemoryScope =
  | Plan
  | Athlete

/// A relevant fact that persists between plans. `Deltas` empty means the
/// label has no automatic adjustment and the trainer picks one of the
/// predefined ones by hand. `Summary` is authored by the model in the
/// consolidation flow; local consolidation leaves it empty and the UI
/// renders the fact from `Origin`. `Scope` decides how far the confirmed
/// memory travels.
type Memory = {
  Summary: string
  Deltas: Delta list
  Origin: MemoryOrigin
  Status: MemoryStatus
  Scope: MemoryScope
}

let automaticAdjustmentsFor (goal: PlanGoal) (label: Label) : Delta list =
  match label with
  | Light ->
    match goal with
    | Hypertrophy
    | Strength -> [ Load 5 ]
    | WeightLoss
    | CardioVascularHealth
    | BasicBodyActivation -> [ Duration 5 ]
  | Heavy ->
    match goal with
    | Hypertrophy
    | Strength -> [ Load -5 ]
    | WeightLoss
    | CardioVascularHealth
    | BasicBodyActivation -> [ Duration -5 ]
  | Pain
  | NoEquipment -> []

let automaticAdjustments(label: Label) : Delta list =
  automaticAdjustmentsFor Hypertrophy label

/// Consolidates a mark into the proposed memory with the goal's automatic
/// adjustment. Marks are situational: the scope starts at the plan. No
/// summary prose here: the summary is the UI's or the model's job, never
/// the domain's.
let consolidateFor (goal: PlanGoal) (mark: FeedbackMark) : Memory = {
  Summary = ""
  Deltas = automaticAdjustmentsFor goal mark.Label
  Origin = Mark mark
  Status = Proposed
  Scope = Plan
}

let consolidate(mark: FeedbackMark) : Memory = consolidateFor Hypertrophy mark

/// Confirms the memory with the adjustments the trainer accepted or
/// picked from the predefined ones.
let confirm (deltas: Delta list) (memory: Memory) : Memory = {
  memory with
      Deltas = deltas
      Status = Confirmed
}

/// Discards the memory: it never reaches the next request.
let discard(memory: Memory) : Memory = { memory with Status = Discarded }

/// The memory of a trainer exemption: born confirmed, with the prescribed
/// technique as the pattern variant. A prescription describes how the
/// trainer wants this athlete trained, so it outlives plans.
let memoryOfExemption(exemption: Exemption) : Memory = {
  Summary = ""
  Deltas = [ PatternVariant exemption.Technique ]
  Origin = Exemption exemption
  Status = Confirmed
  Scope = Athlete
}
