module Briple.Trainer.Core.Memories

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

/// Life cycle of a memory.
type MemoryStatus =
  | Proposed
  | Confirmed
  | Discarded

/// A relevant fact that persists between plans. `Deltas` empty means the
/// label has no automatic adjustment and the trainer picks one of the
/// predefined ones by hand. `Summary` is authored by the model in the
/// consolidation flow; local consolidation leaves it empty and the UI
/// renders the fact from `Origin`.
type Memory = {
  Summary: string
  Deltas: Delta list
  Origin: MemoryOrigin
  Status: MemoryStatus
}

/// The automatic adjustment of each label: light raises load, heavy
/// lowers it; pain and no equipment have no automatic adjustment.
let automaticAdjustments(label: Label) : Delta list =
  match label with
  | Light -> [ Load 5 ]
  | Heavy -> [ Load -5 ]
  | Pain -> []
  | NoEquipment -> []

/// Consolidates a mark into the proposed memory with its automatic
/// adjustment. No summary prose here: the summary is the UI's or the
/// model's job, never the domain's.
let consolidate(mark: FeedbackMark) : Memory = {
  Summary = ""
  Deltas = automaticAdjustments mark.Label
  Origin = Mark mark
  Status = Proposed
}

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
/// technique as the pattern variant.
let memoryOfExemption(exemption: Exemption) : Memory = {
  Summary = ""
  Deltas = [ PatternVariant exemption.Technique ]
  Origin = Exemption exemption
  Status = Confirmed
}
