module Plan.Projection

// Pure date -> session core. No DOM, no IndexedDB: every
// screen renders from these functions. The assembly of week windows, day
// matching and mesociclo extraction is internal.

open System
open Plan.Types

val daysBetween: a: DateOnly -> b: DateOnly -> int

val addDays: date: DateOnly -> days: int -> DateOnly

val sameDate: a: DateOnly -> b: DateOnly -> bool

val weekdayOf: date: DateOnly -> DayOfWeek

val mondayOf: date: DateOnly -> DateOnly

/// The dia for a date inside the plan range; None before, after, or on rest.
val trySession:
  plan: Plan ->
  genero: Genero ->
  opcionId: string ->
  anchor: DateOnly ->
  date: DateOnly ->
    Dia option

/// 1-based plan week number for a date; None outside the plan range.
val planWeek: plan: Plan -> anchor: DateOnly -> date: DateOnly -> int option

/// The semana for a date; None when the opcion has no week there.
val trySemana:
  plan: Plan ->
  genero: Genero ->
  opcionId: string ->
  anchor: DateOnly ->
  date: DateOnly ->
    Semana option

/// True when the variant has a session on the date.
val trainsOn:
  plan: Plan ->
  genero: Genero ->
  opcionId: string ->
  anchor: DateOnly ->
  date: DateOnly ->
    bool

/// Next session at or after the date; for the rest-day line.
val nextSession:
  plan: Plan ->
  genero: Genero ->
  opcionId: string ->
  anchor: DateOnly ->
  date: DateOnly ->
    (DateOnly * Dia) option

/// RIR target text for a 1-based plan week ("2-3"); None when absent or malformed.
val tryMesocicloRir: plan: Plan -> week: int -> string option

/// Default anchor: the Monday of the current week when the variant
/// trains today, else the next Monday.
val defaultAnchor:
  plan: Plan ->
  genero: Genero ->
  opcionId: string ->
  today: DateOnly ->
    DateOnly

/// First free date after the plan's run: the anchor plus the plan's full
/// week count. The first date planChipState reads as Completed.
val planFinish: plan: Plan -> anchor: DateOnly -> DateOnly

/// Everything the staged anchor weighs: the plan, the variant it opens on,
/// the day of the decision, and the first free dates of the stored plans.
type AnchorDecision = {
  Plan: Plan
  View: Genero * string
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
  Session: Dia option
}

/// The seven dates of the week of `selected`, Monday first.
val weekRows:
  plan: Plan ->
  genero: Genero ->
  opcionId: string ->
  anchor: DateOnly ->
  today: DateOnly ->
  selected: DateOnly ->
    WeekRow list

/// Plan progress for a date: the week counter inside the plan, the start
/// date before it, done after it.
type PlanChip =
  | Inside of week: int * ofTotal: int
  | StartsOn of DateOnly
  | Completed

val planChipState: plan: Plan -> anchor: DateOnly -> date: DateOnly -> PlanChip
