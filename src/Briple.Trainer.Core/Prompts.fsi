module Briple.Trainer.Core.Prompts

// The render of the request to the agent prompt: the strict file
// format, the fixed part (traced parameters), what the agent decides
// (fields of its origin) and the forbidden part. The adjustment adds
// the previous plan as reference.

open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests

/// System message of the generation flows: role, JSON response schema,
/// prescription rules, evidence rules and variant rotation.
val system: string

/// System message of the mark consolidation.
val consolidationSystem: string

/// User message of the new-plan flow.
val newPlanPrompt: request: NewPlanRequest -> string

/// User message of the adjustment flow: the common part plus the
/// previous plan in the same schema.
val adjustmentPrompt: request: AdjustmentRequest -> string

/// User message of the consolidation: the recorded marks.
val consolidationPrompt: marks: FeedbackMark list -> string
