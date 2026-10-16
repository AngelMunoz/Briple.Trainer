module Briple.Trainer.Core.Athletes

// The athlete layer: a trainee is a profile of their own. Training
// frequency is a decision of each plan (the brief's VariantFrequency),
// not of the profile; the plan format has no names, gender
// discriminates the athlete inside the file.

open Briple.Trainer.Core.Plan.Types

/// High-level training goal of a plan.
type PlanGoal =
  | Hypertrophy
  | WeightLoss
  | CardioVascularHealth
  | BasicBodyActivation
  | Strength

/// A trainee.
type Athlete = {
  Name: string
  Gender: Gender
  Goals: PlanGoal list
}

/// The athletes a plan covers: one per block. The name stays as the
/// lowercase gender token until the trainer completes it.
val athletesOfPlan: plan: Plan -> Athlete list
