module Briple.Trainer.Core.Prompts

open System
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Instrucciones
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Plan.Datos
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Solicitudes

// Instructions are written in English; the data contract (schema
// keys, day ids, labels, deltas) and the plan content stay in
// Spanish because the decoder and the athlete-facing app expect them.

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
  | Sets n -> $"series {n}"
  | Load p -> $"carga {p}"
  | PatternVariant name -> $"variante {name}"

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
  "titulo": "texto",
  "semanas": 4,
  "generado": "2026-10-14",
  "origen": "texto",
  "genero": "hombre" o "mujer",
  "opciones": [
    {
      "id": "3dias",
      "titulo": "texto (opcional)",
      "semanas": [
        { "numero": 1, "dias": [
          { "id": "lunes", "titulo": "texto (opcional)", "ejercicios": [
            { "circuito": "A", "vueltas": "4", "orden": 1, "nombre": "texto", "reps": "6-8", "rir": "1-2", "descanso": "3 min", "notas": "-" }
          ] }
        ] }
      ]
    }
  ],
  "extras": ["texto libre"]
}
Day ids are Spanish: lunes, martes, miercoles, jueves, viernes, sabado, domingo.
The notas field uses "-" when it does not apply.
Do not write mesociclo RIR lines in extras: they are added automatically.
Write all content (titulo, titles, exercise names, reps, descanso, notas, extras) in Spanish."""

let evidenceText =
  """EVIDENCE-BASED TRAINING:
- Every exercise must serve a clear purpose for this athlete, backed by current strength and conditioning evidence: specificity, progressive overload, volume landmarks, fatigue management.
- Educated guesses are acceptable where evidence is thin. Uninformed choices are not.
- Do not force high-skill or high-risk techniques the athlete has not earned. Prefer joint-friendly, coachable progressions with a good risk/benefit ratio for this athlete.
- If you cannot state why an exercise helps this athlete, replace it with one you can justify.
TRAINER EXCEPTIONS: the trainer is the health expert for this athlete and does not need to justify a prescription. When a trainer exception prescribes a technique, follow it. In that exercise's notas, record that it is trainer-prescribed and state the evidence-based reason why it is not generally suggested."""

// variant rotation avoids monotony: the pattern of each day holds and
// the variant changes week to week
let variabilityText =
  """VARIANT ROTATION:
- Within each option, every training day keeps the same movement patterns across the weeks.
- The exercise variant changes every week: rotate equipment, grip or angle, so an exercise name never repeats in the same day of the following week.
- Progress the work across the weeks through the circuit rounds and rep ranges, not by repeating identical exercises."""

let athleteText(athlete: Athlete) =
  let gender = Gender.toProtocol athlete.Gender

  let goals = join ", " athlete.Goals
  let focus = join ", " athlete.Focus

  $"Athlete: {athlete.Name} ({gender}). Goals: {goals}. Focus: {focus}."

/// System message for the generation flows: role, schema, evidence
/// rules, variant rotation and output discipline.
let system =
  "You are a strength and conditioning coach who writes training plans.\n\n"
  + schemaText
  + "\n\n"
  + evidenceText
  + "\n\n"
  + variabilityText
  + "\n\nRespond with the JSON only: no markdown, no extra text."

/// System message for feedback consolidation.
let consolidationSystem =
  """You are a strength and conditioning coach who summarizes athlete feedback into training memories.
Each memory is a persistent fact with a proposed parametric adjustment.
Valid deltas: rir N, reps N, series N, carga P (percent), variante NOMBRE. Write numbers bare: carga 5, not carga +5% or carga 5%.
Respond with one line per mark, in the same order, in this format:
objetivo | etiqueta | resumen | delta[;delta...]
Echo objetivo and etiqueta exactly as given in the marks. Write the resumen in Spanish, as one short sentence stating the fact.
The proposed delta corrects the mark: leve raises load, pesado lowers it, dolor and sin-equipo propose a variante.
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
    | Light -> "leve"
    | Heavy -> "pesado"
    | Pain -> "dolor"
    | NoEquipment -> "sin-equipo"

  let markText(mark: FeedbackMark) =
    let date = mark.Date.ToString("yyyy-MM-dd")

    $"- {targetText mark.Target} | {labelText mark.Label} | {date}"

  let marksText = marks |> List.map markText |> join "\n"

  $"RECORDED MARKS:\n{marksText}"
