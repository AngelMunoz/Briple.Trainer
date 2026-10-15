module Briple.Trainer.Core.Validador

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Solicitudes

type Violation = { Rule: string; Detail: string }

type Verdict =
  | Valid of plan: Plan
  | Rejected of Violation list

let violation rule detail = { Rule = rule; Detail = detail }

/// Checks a plan already converted to the domain against the parameters
/// of the request: gender, variants, weeks, counts and RIR.
let checkParameters
  (athlete: Athlete)
  (weeks: int)
  (brief: Brief)
  (counts: (string * int) list)
  (plan: Plan)
  : Violation list =
  let violations = ResizeArray<Violation>()

  match plan.Blocks with
  | [ block ] when block.Gender = athlete.Gender -> ()
  | _ ->
    violations.Add(
      violation
        "genero"
        $"se esperaba solo el bloque {Gender.toFileToken athlete.Gender}, hay {plan.Blocks.Length} bloques"
    )

  if plan.Weeks <> weeks then
    violations.Add(
      violation "semanas" $"se esperaban {weeks}, el plan declara {plan.Weeks}"
    )

  let variants =
    plan.Blocks
    |> List.filter(fun block -> block.Gender = athlete.Gender)
    |> List.collect(fun block -> block.Variants)

  let expected = List.sort brief.Variants
  let actual = variants |> List.map(fun variant -> variant.Id) |> List.sort

  if expected <> actual then
    violations.Add(
      violation "opciones" $"se esperaban {expected}, el plan trae {actual}"
    )

  for variant in variants do
    let numbers = variant.Weeks |> List.map(fun week -> week.Number)

    if numbers <> [ 1..weeks ] then
      violations.Add(
        violation
          "semanas-opcion"
          $"la opcion {variant.Id} trae las semanas {numbers}, se esperaba 1..{weeks}"
      )

  let actualCounts = planCounts athlete plan |> Map.ofList

  for variant, total in counts do
    match Map.tryFind variant actualCounts with
    | Some actual when actual = total -> ()
    | Some actual ->
      violations.Add(
        violation
          "conteo"
          $"la opcion {variant} debe tener {total} #ej, tiene {actual}"
      )
    | None ->
      violations.Add(
        violation "conteo" $"la opcion {variant} no existe en la salida"
      )

  for week, target in brief.RirPerWeek do
    match tryMesocycleRir plan week with
    | Some actual when actual = target -> ()
    | Some actual ->
      violations.Add(
        violation
          "rir"
          $"la semana {week} debe declarar RIR {target}, declara {actual}"
      )
    | None ->
      violations.Add(
        violation "rir" $"la semana {week} no declara RIR {target}"
      )

  List.ofSeq violations

let validateCommon athlete weeks brief counts (output: string) =
  match parsePlan output with
  | Error error ->
    Rejected [ violation "formato" $"linea {error.Line}: {error.Message}" ]
  | Ok parsed ->
    let formatViolations =
      parsed.Warnings
      |> List.map(fun warning ->
        violation "formato" $"linea {warning.Line}: {warning.Message}")

    let parameterViolations =
      checkParameters athlete weeks brief counts parsed.Plan

    match formatViolations @ parameterViolations with
    | [] -> Valid parsed.Plan
    | violations -> Rejected violations

/// Validates the output of a new plan: clean format, one block of the
/// athlete's gender, their variants, the weeks, the counts and the
/// brief's RIR.
let validateNew (request: NewPlanRequest) (output: string) =
  validateCommon
    request.Athlete
    request.Weeks
    request.Brief
    request.Counts
    output

/// Validates the output of an adjustment: the same over the adjustment
/// request, whose previous plan travels as reference.
let validateAdjustment (request: AdjustmentRequest) (output: string) =
  validateCommon
    request.Athlete
    request.Weeks
    request.Brief
    request.Counts
    output
