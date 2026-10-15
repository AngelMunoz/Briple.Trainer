module Briple.Trainer.Core.Plan.Projection

// Pure date -> session core. No DOM, no IndexedDB: every
// screen renders from these functions.
//
// Plan week N is the 7-day window [anchor + 7(N-1), anchor + 7(N-1) + 6]; a
// negative week offset is before the anchor. tryWeek is bounded by the
// variant's own Weeks list while planWeek is bounded by the plan's Semanas
// meta, so a shorter variant renders rest days for weeks the plan still
// counts. An unmapped Day.Id never matches a weekday (the parser warned at
// parse time): those dates are rest.

open System
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser

// DateOnly math goes through DateTime on purpose: the module then relies only
// on constructor/Year/Month/Day, which the Fable runtime guarantees.
let daysBetween (a: DateOnly) (b: DateOnly) : int =
  let ta = DateTime(a.Year, a.Month, a.Day)
  let tb = DateTime(b.Year, b.Month, b.Day)
  int(Math.Floor((tb - ta).TotalDays))

let addDays (date: DateOnly) (days: int) : DateOnly =
  let dt = DateTime(date.Year, date.Month, date.Day).AddDays(float days)
  DateOnly(dt.Year, dt.Month, dt.Day)

let sameDate (a: DateOnly) (b: DateOnly) : bool =
  a.Year = b.Year && a.Month = b.Month && a.Day = b.Day

let weekdayOf(date: DateOnly) : DayOfWeek =
  DateTime(date.Year, date.Month, date.Day).DayOfWeek

let mondayOf(date: DateOnly) : DateOnly =
  let dow = weekdayOf date
  let offset = if dow = DayOfWeek.Sunday then -6 else 1 - int dow
  addDays date offset

// Floor division on purpose: dates before the anchor divide into negative
// weeks that must round down, not toward zero.
let weekOffset (anchor: DateOnly) (date: DateOnly) : int =
  int(Math.Floor(float(daysBetween anchor date) / 7.0))

let tryVariant
  (plan: Plan)
  (gender: Gender)
  (variantId: string)
  : Variant option =
  plan.Blocks
  |> List.tryFind(fun block -> block.Gender = gender)
  |> Option.bind(fun block ->
    block.Variants |> List.tryFind(fun variant -> variant.Id = variantId))

/// The day for a date inside the plan range; None before, after, or on rest.
let trySession
  (plan: Plan)
  (gender: Gender)
  (variantId: string)
  (anchor: DateOnly)
  (date: DateOnly)
  : Day option =
  let offset = weekOffset anchor date

  if offset < 0 then
    None
  else
    tryVariant plan gender variantId
    |> Option.bind(fun variant ->
      if offset < variant.Weeks.Length then
        let week = variant.Weeks.[offset]
        let dow = weekdayOf date

        week.Days |> List.tryFind(fun day -> tryDayOfWeek day.Id = Some dow)
      else
        None)

/// 1-based plan week number for a date; None outside the plan range.
let planWeek (plan: Plan) (anchor: DateOnly) (date: DateOnly) : int option =
  let offset = weekOffset anchor date

  if offset >= 0 && offset < plan.Weeks then
    Some(offset + 1)
  else
    None

/// The week for a date; None when the variant has no week there.
let tryWeek
  (plan: Plan)
  (gender: Gender)
  (variantId: string)
  (anchor: DateOnly)
  (date: DateOnly)
  : Week option =
  let offset = weekOffset anchor date

  if offset < 0 then
    None
  else
    tryVariant plan gender variantId
    |> Option.bind(fun variant ->
      if offset < variant.Weeks.Length then
        Some variant.Weeks.[offset]
      else
        None)

/// True when the variant has a session on the date.
let trainsOn
  (plan: Plan)
  (gender: Gender)
  (variantId: string)
  (anchor: DateOnly)
  (date: DateOnly)
  : bool =
  trySession plan gender variantId anchor date |> Option.isSome

/// Next session at or after the date; for the rest-day line.
let nextSession
  (plan: Plan)
  (gender: Gender)
  (variantId: string)
  (anchor: DateOnly)
  (date: DateOnly)
  : (DateOnly * Day) option =
  let weeks =
    max
      plan.Weeks
      (tryVariant plan gender variantId
       |> Option.map(fun variant -> variant.Weeks.Length)
       |> Option.defaultValue 0)

  let limit = weeks * 7 + 7

  let rec step candidate =
    if daysBetween date candidate > limit then
      None
    else
      match trySession plan gender variantId anchor candidate with
      | Some day -> Some(candidate, day)
      | None -> step(addDays candidate 1)

  step date

/// RIR target text for a 1-based plan week ("2-3"); None when absent or
/// malformed. Mesociclo line shape: `mesociclo | Semana N: <text> | RIR <a-b>`
/// (the stored Extras text starts at the key; a trailing note segment is fine).
let tryMesocycleRir (plan: Plan) (week: int) : string option =
  let prefix = $"Semana {week}:"

  plan.Extras
  |> List.tryPick(fun extra ->
    if not(extra.StartsWith "mesociclo") then
      None
    else
      let segments =
        extra.Split('|') |> Array.map(fun segment -> segment.Trim())

      match segments |> Array.tryItem 1 with
      | Some header when header.StartsWith prefix ->
        segments
        |> Array.tryItem 2
        |> Option.bind(fun segment ->
          if segment.StartsWith "RIR" then
            let target = segment.Substring(3).Trim()

            if target = "" then None else Some target
          else
            None)
      | _ -> None)

/// Default anchor: the Monday of the current week when the variant
/// trains today, else the next Monday. The candidate anchor drives the
/// "trains today" check, so 6/7-day variants and weekend sessions resolve.
let defaultAnchor
  (plan: Plan)
  (gender: Gender)
  (variantId: string)
  (today: DateOnly)
  : DateOnly =
  let monday = mondayOf today

  if trainsOn plan gender variantId monday today then
    monday
  else
    addDays monday 7

/// First free date after the plan's run: the anchor plus the plan's full
/// week count. planChipState reads it as the first Completed day.
let planFinish (plan: Plan) (anchor: DateOnly) : DateOnly =
  addDays anchor (7 * plan.Weeks)

/// Everything the staged anchor weighs: the plan, the variant it opens on,
/// the day of the decision, and the first free dates of the stored plans.
type AnchorDecision = {
  Plan: Plan
  View: Gender * string
  Today: DateOnly
  Finishes: DateOnly list
}

// Ordering goes through daysBetween, never DateOnly comparison.
let chainedAnchor(decision: AnchorDecision) : DateOnly =
  let gender, variantId = decision.View

  decision.Finishes
  |> List.fold
    (fun latest finish ->
      if daysBetween latest finish > 0 then finish else latest)
    (defaultAnchor decision.Plan gender variantId decision.Today)

// --- Week rows and the plan chip --------------------------------------------

/// One row of the visible week: the date, whether it is today, and the
/// session projected for it. `Session = None` renders as a rest row.
type WeekRow = {
  Date: DateOnly
  IsToday: bool
  Session: Day option
}

/// The seven dates of the week of `selected`, Monday first.
let weekRows
  (plan: Plan)
  (gender: Gender)
  (variantId: string)
  (anchor: DateOnly)
  (today: DateOnly)
  (selected: DateOnly)
  : WeekRow list =
  let monday = mondayOf selected

  [
    for offset in 0..6 ->
      let date = addDays monday offset

      {
        Date = date
        IsToday = sameDate date today
        Session = trySession plan gender variantId anchor date
      }
  ]

/// Plan progress for a date: the week counter inside the plan, the start
/// date before it, done after it. The UI renders all three quiet.
type PlanChip =
  | Inside of week: int * ofTotal: int
  | StartsOn of DateOnly
  | Completed

let planChipState (plan: Plan) (anchor: DateOnly) (date: DateOnly) : PlanChip =
  match planWeek plan anchor date with
  | Some week -> Inside(week, plan.Weeks)
  | None ->
    if daysBetween anchor date < 0 then
      StartsOn anchor
    else
      Completed
