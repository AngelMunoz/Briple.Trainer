module Briple.Trainer.Core.Prompts

// The render of the request to the agent prompt: the strict file
// format, the fixed part (traced parameters), what the agent decides
// (fields of its origin) and the forbidden part. The adjustment adds
// the previous plan as reference.

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests

/// System message of the generation flows: role, JSON response schema,
/// prescription rules, evidence rules, the goal profile (volume,
/// effort, structure and progression) and the content language.
val systemFor: language: ContentLanguage * goal: PlanGoal -> string

/// The Spanish generation system message.
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

/// System message of the note interpretation: the constant closed
/// surface (the protocol, the label enum, the rule that the note is
/// data). The plan's vocabulary and the note travel in the user
/// message, so plan content cannot rewrite this role.
val interpretationSystem: string

/// User message of the note interpretation: the numbered target
/// vocabulary and the note between BEGIN NOTE and END NOTE markers
/// that carry a per-request nonce.
val interpretationPrompt:
  nonce: string * targets: Target list * note: string -> string
