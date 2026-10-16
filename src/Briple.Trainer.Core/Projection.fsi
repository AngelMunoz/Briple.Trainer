module Briple.Trainer.Core.Plan.Projection

// Pure date -> session core. No DOM, no IndexedDB: every screen renders
// from these functions. The assembly of week windows, day matching and
// mesocycle extraction is internal.

open System
open Briple.Trainer.Core.Plan.Types

val daysBetween: a: DateOnly -> b: DateOnly -> int

val addDays: date: DateOnly -> days: int -> DateOnly

val sameDate: a: DateOnly -> b: DateOnly -> bool

val weekdayOf: date: DateOnly -> DayOfWeek

val mondayOf: date: DateOnly -> DateOnly

/// The plan view the projection reads: the plan, the block's gender and
/// the variant. One record instead of the three parameters every
/// function threaded before.
type View = {
  Plan: Plan
  Gender: Gender
  VariantId: string
}

/// The view fixed in time: plan week one starts on the anchor's Monday.
type Window = { View: View; Anchor: DateOnly }

/// The variant the view names; None when the plan has no such block or
/// variant.
val tryVariantOf: view: View -> Variant option

/// The day for a date inside the plan range; None before, after, or on rest.
val trySession: window: Window -> date: DateOnly -> Day option

/// 1-based plan week number for a date; None outside the plan range.
val planWeek: window: Window -> date: DateOnly -> int option

/// The week for a date; None when the variant has no week there.
val tryWeek: window: Window -> date: DateOnly -> Week option

/// True when the variant has a session on the date.
val trainsOn: window: Window -> date: DateOnly -> bool

/// Next session at or after the date; for the rest-day line.
val nextSession: window: Window -> date: DateOnly -> (DateOnly * Day) option

/// RIR target text for a 1-based plan week ("2-3"); None when absent or malformed.
val tryMesocycleRir: plan: Plan -> week: int -> string option

/// Default anchor: the Monday of the current week when the variant
/// trains today, else the next Monday.
val defaultAnchor: view: View -> today: DateOnly -> DateOnly

/// First free date after the plan's run: the anchor plus the plan's full
/// week count. The first date planChipState reads as Completed.
val planFinish: plan: Plan -> anchor: DateOnly -> DateOnly

/// Everything the staged anchor weighs: the view, the day of the
/// decision, and the first free dates of the stored plans.
type AnchorDecision = {
  View: View
  Today: DateOnly
  Finishes: DateOnly list
}

/// The staged-plan default anchor: the Monday heuristic of the decision's
/// view, or the first free date after the latest finish when that lands
/// later. An empty Finishes list is the no-plans-yet case; a finish in the
/// past never backdates the start.
val chainedAnchor: decision: AnchorDecision -> DateOnly

/// One row of the visible week: the date, whether it is today, and the
/// session projected for it. `Session = None` renders as a rest row.
type WeekRow = {
  Date: DateOnly
  IsToday: bool
  Session: Day option
}

/// The seven dates of the week of `selected`, Monday first.
val weekRows:
  window: Window -> today: DateOnly -> selected: DateOnly -> WeekRow list

/// Plan progress for a date: the week counter inside the plan, the start
/// date before it, done after it.
type PlanChip =
  | Inside of week: int * ofTotal: int
  | StartsOn of DateOnly
  | Completed

val planChipState: plan: Plan -> anchor: DateOnly -> date: DateOnly -> PlanChip
