module Briple.Trainer.Core.Prompts

open System
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Instructions
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Plan.Data
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests

// Instructions and the data contract (schema keys, labels, deltas) are
// written in English; the plan content (titles, day ids, exercise
// names, notes, extras, summaries) stays Spanish because the decoder
// and the athlete-facing app expect it. The trainer picks the content
// language in the desktop app.

let join (separator: string) (parts: string list) =
  String.concat separator parts

let targetText target =
  match target with
  | Exercise name -> name
  | Day id -> $"day {id}"

let deltaText delta =
  match delta with
  | Delta.Rir n -> $"rir {n}"
  | Delta.Reps n -> $"reps {n}"
  | Sets n -> $"sets {n}"
  | Load p -> $"load {p}"
  | PatternVariant name -> $"variant {name}"

let parameterText parameter =
  match parameter with
  | PlanWeeks(weeks, _) -> $"- the plan covers {weeks} weeks"
  | AthleteVariants(variants, _) ->
    let list = join ", " variants

    $"- the plan has one option per variant: {list}"
  | VariantCount(variant, total, _) ->
    $"- option {variant} has exactly {total} exercises in total"
  | WeekRir(week, target, _) ->
    $"- week {week} targets RIR {target} (added automatically, do not write it)"
  | Adjustment(target, deltas, ConfirmedMemory memory) ->
    let adjustments = deltas |> List.map deltaText |> join "; "

    match memory.Origin with
    | Exemption _ ->
      $"- on {targetText target}: {adjustments} — trainer exception: the trainer prescribed this technique. Follow it, and write in that exercise's notas that it is trainer-prescribed plus the evidence-based reason why it is not generally suggested."
    | Mark _ -> $"- on {targetText target}: {adjustments}"
  | Adjustment(target, deltas, _) ->
    let adjustments = deltas |> List.map deltaText |> join "; "

    $"- on {targetText target}: {adjustments}"

let prohibitionText prohibition =
  match prohibition with
  | ChangeCounts -> "- do not change the exercise counts per option"
  | OutsideVariants ->
    "- do not add options or genders other than the requested ones"
  | LeaveFormat -> "- do not invent fields outside the schema"

let fieldText field =
  match field with
  | Title -> "the plan title"
  | Weeks -> "the week count"
  | Generated -> "the generation date"
  | FileOrigin -> "the origin line"
  | Variants -> "the options"
  | VariantTitle -> "each option title"
  | VariantWeeks -> "how many weeks each option spans"
  | Days -> "which days of the week are training days"
  | DayTitle -> "each day title"
  | DayNotes -> "day notes"
  | Circuit -> "each exercise circuit"
  | Laps -> "each circuit rounds"
  | Order -> "the exercise order"
  | Name -> "each exercise name"
  | Reps -> "the rep ranges (text like 6-8 or 30-45 s)"
  | Field.Rir -> "the RIR (text like 1-2, or - when it does not apply)"
  | Rest -> "the rest periods (like 3 min)"
  | Notes -> "each exercise notes (or -)"
  | Extras ->
    "the extras: guidance lines (no mesociclo; that is added automatically)"

// the schema has no place for day notes: they are not requested
let requestedFields(instructions: Instructions) =
  instructions.Ask
  |> List.filter(fun field -> field <> DayNotes)
  |> List.map fieldText

let schemaText =
  """RESPONSE SCHEMA: respond only with valid JSON in this shape:
{
  "title": "text",
  "weeks": 4,
  "generated": "2026-10-14",
  "origin": "text",
  "gender": "male" or "female",
  "variants": [
    {
      "id": "3dias",
      "title": "text (optional)",
      "weeks": [
        { "number": 1, "days": [
          { "id": "lunes", "title": "text (optional)", "exercises": [
            { "circuit": "A", "laps": "4", "order": 1, "name": "text", "reps": "6-8", "rir": "1-2", "rest": "3 min", "notes": "-" }
          ] }
        ] }
      ]
    }
  ],
  "extras": ["free text"]
}
Day ids are Spanish: lunes, martes, miercoles, jueves, viernes, sabado, domingo.
The notes field uses "-" when it does not apply.
Do not write mesociclo RIR lines in extras: they are added automatically.
Write all content (titles, exercise names, reps, rest, notes, extras) in Spanish."""

let prescriptionText =
  """PRESCRIPTION RULES:
- reps is either a pure numeric range ("6-8") or a pure duration ("30-45 s", "20 min"). Never append words to it: rounds come from the circuit laps, and per-side work ("por lado") goes in notes.
- rir is a RIR range for rep-based exercises ("1-2", "2"); never "-". For timed exercises write the effort equivalent instead: "rpe 6-7" or a zone ("zona 2").
- The exercise name is exactly one exercise; alternatives or progressions go in that exercise's notes.
- LOAD DELTAS: load N means N percent of the athlete's habitual load for that exercise. The app tracks loads; reflect a load delta only in that exercise's notas when it adds clarity."""

let evidenceText =
  """EVIDENCE-BASED TRAINING:
- Every exercise must serve a clear purpose for this athlete, backed by current strength and conditioning evidence: specificity, progressive overload, fatigue management.
- Educated guesses are acceptable where evidence is thin. Uninformed choices are not.
- Do not force high-skill or high-risk techniques the athlete has not earned. Prefer joint-friendly, coachable progressions with a good risk/benefit ratio for this athlete.
- If you cannot state why an exercise helps this athlete, replace it with one you can justify.
TRAINER EXCEPTIONS: the trainer is the health expert for this athlete and does not need to justify a prescription. When a trainer exception prescribes a technique, follow it. In that exercise's notas, record that it is trainer-prescribed and state the evidence-based reason why it is not generally suggested."""

let goalEvidence goal =
  match goal with
  | Hypertrophy ->
    """GOAL: HYPERTROPHY
- Build muscle: 10-20 weekly sets per muscle group, most work in the 6-15 rep range, sets taken within 0-3 reps of failure.
- Rotate exercise variants weekly while the movement pattern of each day stays fixed.
- Progress volume and load across the weeks through the circuit laps and the rep ranges."""
  | Strength ->
    """GOAL: STRENGTH
- Build maximal force: 3-6 reps at a high relative load on the main lifts, 3-5 min rests there, most sets 1-3 reps in reserve.
- Keep technique crisp with heavy loads; treat frequent failure training as a tool, not a default.
- Progress load across the weeks through the circuit laps and the rep ranges, keeping the movement patterns fixed."""
  | WeightLoss ->
    """GOAL: WEIGHT LOSS
- Preserve muscle in a deficit: resistance training is the core of every session; do not replace it with cardio.
- Raise energy expenditure with circuit density and short rests, keeping joint-friendly modalities and moderate impact.
- Prescribe low-impact conditioning by time with an effort zone rather than by reps."""
  | CardioVascularHealth ->
    """GOAL: CARDIOVASCULAR HEALTH
- Build an aerobic base: zone-2 continuous work plus occasional intervals, prescribed by time.
- Use effort zones (rpe or heart-rate talk-test descriptions) instead of RIR on conditioning work.
- Keep the resistance training as support for joint and muscle health, joint-friendly and progressive."""
  | BasicBodyActivation ->
    """GOAL: BASIC BODY ACTIVATION
- Reactivate movement patterns: light loads, strict technique, high effort reserves (3+ reps in reserve or a comfortable zone).
- Keep sessions short, emphasize core control and joint mobility, and never program failure training.
- Progress gently across the weeks through the circuit laps before touching the rep ranges."""

// variant rotation avoids monotony: the pattern of each day holds and
// the variant changes week to week
let variabilityText =
  """VARIANT ROTATION:
- Within each option, every training day keeps the same movement patterns across the weeks.
- The exercise variant changes every week: rotate equipment, grip or angle, so an exercise name never repeats in the same day of the following week.
- Progress the work across the weeks through the circuit rounds and rep ranges, not by repeating identical exercises."""

let goalName goal =
  match goal with
  | Hypertrophy -> "hypertrophy"
  | Strength -> "strength"
  | WeightLoss -> "weight loss"
  | CardioVascularHealth -> "cardiovascular health"
  | BasicBodyActivation -> "basic body activation"

let athleteText(athlete: Athlete) =
  let gender = Gender.toProtocol athlete.Gender

  let goals = athlete.Goals |> List.map goalName |> join ", "
  let focus = join ", " athlete.Focus

  $"Athlete: {athlete.Name} ({gender}). Goals: {goals}. Focus: {focus}."

/// System message for the generation flows: role, schema, prescription
/// rules, evidence rules for the plan's goal, variant rotation and
/// output discipline.
let system(goal: PlanGoal) =
  "You are a strength and conditioning coach who writes training plans.\n\n"
  + schemaText
  + "\n\n"
  + prescriptionText
  + "\n\n"
  + evidenceText
  + "\n\n"
  + goalEvidence goal
  + "\n\n"
  + variabilityText
  + "\n\nRespond with the JSON only: no markdown, no extra text."

/// System message for feedback consolidation.
let consolidationSystem =
  """You are a strength and conditioning coach who summarizes athlete feedback into training memories.
Each memory is a persistent fact with a proposed parametric adjustment.
Valid deltas: rir N, reps N, sets N, load P (percent), variant NAME. Write numbers bare: load 5, not load +5% or load 5%.
Respond with one line per mark, in the same order, in this format:
target | label | summary | delta[;delta...]
Echo target and label exactly as given in the marks. Write the summary in Spanish, as one short sentence stating the fact.
The proposed delta corrects the mark: light raises load, heavy lowers it, pain and no-equipment propose a variant.
No explanations, no markdown."""

/// User message for the new plan flow.
let newPlanPrompt(request: NewPlanRequest) =
  let instructions = forNew request

  // profile and brief fix the same variants: say it once
  let fixedLines =
    instructions.Fix |> List.map parameterText |> List.distinct |> join "\n"

  let asked = requestedFields instructions |> join ", "

  let forbidden = instructions.Forbid |> List.map prohibitionText |> join "\n"

  $"ATHLETE\n{athleteText request.Athlete}\n\nMANDATORY IN THE RESPONSE:\n{fixedLines}\n\nWHAT YOU DECIDE:\n{asked}\n\nFORBIDDEN:\n{forbidden}"

/// User message for the adjustment flow: the common part plus the
/// previous plan as reference in the same schema.
let adjustmentPrompt(request: AdjustmentRequest) =
  let instructions = forAdjustment request

  let fixedLines =
    instructions.Fix |> List.map parameterText |> List.distinct |> join "\n"

  let asked = requestedFields instructions |> join ", "

  let forbidden = instructions.Forbid |> List.map prohibitionText |> join "\n"

  $"ATHLETE\n{athleteText request.Athlete}\n\nMANDATORY IN THE RESPONSE:\n{fixedLines}\n\nWHAT YOU DECIDE:\n{asked}\n\nFORBIDDEN:\n{forbidden}\n\nPREVIOUS PLAN (adjustment reference, same schema):\n{toJson request.PreviousPlan}"

/// User message for consolidation: the recorded marks.
let consolidationPrompt(marks: FeedbackMark list) =
  let labelText label =
    match label with
    | Light -> "light"
    | Heavy -> "heavy"
    | Pain -> "pain"
    | NoEquipment -> "no-equipment"

  let markText(mark: FeedbackMark) =
    let date = mark.Date.ToString("yyyy-MM-dd")

    $"- {targetText mark.Target} | {labelText mark.Label} | {date}"

  let marksText = marks |> List.map markText |> join "\n"

  $"RECORDED MARKS:\n{marksText}"
