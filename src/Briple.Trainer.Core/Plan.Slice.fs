module Briple.Trainer.Core.Plan.Slice

open Briple.Trainer.Core.Plan.Types

let variant(gender: Gender, variantId: string, plan: Plan) : Plan option =
  plan.Blocks
  |> List.tryFind(fun block -> block.Gender = gender)
  |> Option.bind(fun block ->
    block.Variants
    |> List.tryFind(fun candidate -> candidate.Id = variantId)
    |> Option.map(fun variant -> {
      plan with
          Blocks = [ { block with Variants = [ variant ] } ]
    }))
