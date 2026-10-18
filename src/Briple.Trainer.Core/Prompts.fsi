module Briple.Trainer.Core.Prompts

// The render of the request to the agent prompt: the strict file
// format, the fixed part (traced parameters), what the agent decides
// (fields of its origin) and the forbidden part. The adjustment adds
// the previous plan as reference.

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests

/// System message of the generation flows: role, JSON response schema,
/// prescription rules, evidence rules and the goal profile (volume,
/// effort, structure and progression).
val system: goal: PlanGoal -> string

/// System message of the mark consolidation.
val consolidationSystem: string

/// User message of the new-plan flow: the athlete, the plan's goal, the
/// structure of the goal profile and the mandatory values. The mesociclo
/// lines are not printed; the app adds them.
val newPlanPrompt: request: NewPlanRequest -> string

/// User message of the adjustment flow: the common part plus the
/// previous plan in the same schema and the per-day counts it keeps.
val adjustmentPrompt: request: AdjustmentRequest -> string

/// User message of the consolidation: the recorded marks.
val consolidationPrompt: marks: FeedbackMark list -> string

/// System message of the note interpretation: the closed surface (the
/// protocol, the label enum, the plan's target vocabulary) and the rule
/// that the note is data, never instructions.
val interpretationSystem: targets: Target list -> string

/// User message of the note interpretation: the trainer's note, verbatim.
val interpretationNote: note: string -> string
