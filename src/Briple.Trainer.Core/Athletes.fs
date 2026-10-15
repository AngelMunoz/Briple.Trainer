module Briple.Trainer.Core.Athletes

open Briple.Trainer.Core.Plan.Types

/// High-level training goal of a plan.
type PlanGoal =
  | Hypertrophy
  | WeightLoss
  | CardioVascularHealth
  | BasicBodyActivation
  | Strength

type Athlete = {
  Name: string
  Gender: Gender
  Variants: string list
  Goals: PlanGoal list
  Focus: string list
}

let athletesOfPlan(plan: Plan) : Athlete list =
  plan.Blocks
  |> List.map(fun block -> {
    Name = Gender.toFileToken block.Gender
    Gender = block.Gender
    Variants = block.Variants |> List.map(fun variant -> variant.Id)
    Goals = []
    Focus = []
  })
