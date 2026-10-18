module Briple.Trainer.Core.Memories

// The feedback and memory layer: the quick marks recorded on the active
// plan and the memories consolidated from them. A memory is born
// proposed; it reaches the next request only when the trainer confirms
// it with the adjustments they accepted or chose.

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
/// writes the reason why it is not generally suggested into the
/// exercise notes.
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
  /// Minutes added to or removed from a timed block.
  | Duration of minutes: int
  /// Steps up or down an effort zone.
  | Zone of change: int
  /// A cardio modality the trainer wants instead.
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
/// predefined ones by hand. `Summary` carries only model-authored text
/// from the consolidation flow; the UI renders local memories from
/// `Origin` so no prose is fixed in the domain. `Scope` decides how far
/// the confirmed memory travels: plan-scoped ones serve the plan they
/// were observed on, athlete-scoped ones every future plan.
type Memory = {
  Summary: string
  Deltas: Delta list
  Origin: MemoryOrigin
  Status: MemoryStatus
  Scope: MemoryScope
}

/// The automatic adjustment of each label for the plan's goal: a light
/// mark raises load on strength goals and duration on cardio goals; heavy
/// lowers them. Pain and no equipment have no automatic adjustment. The
/// goal is a proxy: the trainer confirms or replaces the delta.
val automaticAdjustmentsFor: goal: PlanGoal -> label: Label -> Delta list

/// The default adjustment: the hypertrophy profile.
val automaticAdjustments: label: Label -> Delta list

/// Consolidates a mark into the proposed memory for the plan's goal.
val consolidateFor: goal: PlanGoal -> mark: FeedbackMark -> Memory

/// Consolidates a mark with the default (hypertrophy) adjustment.
val consolidate: mark: FeedbackMark -> Memory

/// Confirms the memory with the adjustments the trainer accepted or
/// picked from the predefined ones.
val confirm: deltas: Delta list -> memory: Memory -> Memory

/// Discards the memory: it never reaches the next request.
val discard: memory: Memory -> Memory

/// The memory of a trainer exemption: born confirmed, with the prescribed
/// technique as the pattern variant.
val memoryOfExemption: exemption: Exemption -> Memory
