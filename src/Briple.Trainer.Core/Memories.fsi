module Briple.Trainer.Core.Memories

// The feedback and memory layer: the quick marks recorded on the active
// plan and the memories consolidated from them. A memory is born
// proposed; it reaches the next request only when the trainer confirms
// it with the adjustments they accepted or chose.

open System

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

/// Life cycle of a memory.
type MemoryStatus =
  | Proposed
  | Confirmed
  | Discarded

/// A relevant fact that persists between plans. `Deltas` empty means the
/// label has no automatic adjustment and the trainer picks one of the
/// predefined ones by hand. `Summary` carries only model-authored text
/// from the consolidation flow; the UI renders local memories from
/// `Origin` so no prose is fixed in the domain.
type Memory = {
  Summary: string
  Deltas: Delta list
  Origin: MemoryOrigin
  Status: MemoryStatus
}

/// The automatic adjustment of each label: light raises load, heavy
/// lowers it; pain and no equipment have no automatic adjustment.
val automaticAdjustments: label: Label -> Delta list

/// Consolidates a mark into the proposed memory with its automatic
/// adjustment.
val consolidate: mark: FeedbackMark -> Memory

/// Confirms the memory with the adjustments the trainer accepted or
/// picked from the predefined ones.
val confirm: deltas: Delta list -> memory: Memory -> Memory

/// Discards the memory: it never reaches the next request.
val discard: memory: Memory -> Memory

/// The memory of a trainer exemption: born confirmed, with the prescribed
/// technique as the pattern variant.
val memoryOfExemption: exemption: Exemption -> Memory
