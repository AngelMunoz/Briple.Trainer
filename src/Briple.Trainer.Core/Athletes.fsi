module Briple.Trainer.Core.Athletes

// The athlete layer: every trainee is a profile of their own with the
// variants they can train. A plan carries one block per athlete and the
// block's variants are the athlete's options; the format has no names,
// gender discriminates the athlete inside the file.

open Briple.Trainer.Core.Plan.Types

/// A trainee. `Variants` are the options they can train ("3dias",
/// "5dias") for their own flexibility; they are not distinct athletes.
type Athlete = {
  Name: string
  Gender: Gender
  Variants: string list
  Goals: string list
  Focus: string list
}

/// The athletes a plan covers: one per block, with the block's variants
/// as their own. The name stays as the lowercase gender token until the
/// trainer completes it.
val athletesOfPlan: plan: Plan -> Athlete list
