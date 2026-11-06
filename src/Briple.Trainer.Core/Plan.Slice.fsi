module Briple.Trainer.Core.Plan.Slice

// The import slice of the plan tree. A .briple file is a multi-plan
// container: one block per gender, one variant per option, and an
// athlete trains one variant of one block. This module reduces a parsed
// file to that single plan. The plan-level metadata and the #x extras
// stay: the extras carry the mesociclo and decision prose a review
// renders.

open Briple.Trainer.Core.Plan.Types

/// The plan reduced to the variant `variantId` of the gender block:
/// one block, one variant, the plan metadata and the extras kept. None
/// when the plan has no block of that gender or no variant of that id.
val variant: gender: Gender * variantId: string * plan: Plan -> Plan option
