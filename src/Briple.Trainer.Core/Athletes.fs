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
  Goals: PlanGoal list
}

let athletesOfPlan(plan: Plan) : Athlete list =
  plan.Blocks
  |> List.map(fun block -> {
    Name = Gender.toFileToken block.Gender
    Gender = block.Gender
    Goals = []
  })
