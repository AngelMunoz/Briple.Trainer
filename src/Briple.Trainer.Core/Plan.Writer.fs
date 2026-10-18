module Briple.Trainer.Core.Plan.Writer

open Briple.Trainer.Core.Plan.Sanitize
open Briple.Trainer.Core.Plan.Types

let metaLine key value = $"#meta {key} {value}"

let exerciseLine(exercise: Exercise) =
  $"#ej {exercise.Circuit}|{exercise.Laps}|{exercise.Order}|{exercise.Name}|{exercise.Reps}|{exercise.Rir}|{exercise.Rest}|{exercise.Notes}"

let dayLines(day: Day) = [
  $"#start dia {day.Id}"

  yield! day.Title |> Option.toList |> List.map(metaLine "titulo")

  for exercise in day.Exercises do
    exerciseLine exercise

  "#end dia"
]

let variantLines(variant: Variant) = [
  $"#start opcion {variant.Id}"

  yield! variant.Title |> Option.toList |> List.map(metaLine "titulo")

  for week in variant.Weeks do
    $"#start semana {week.Number}"

    for day in week.Days do
      yield! dayLines day

    "#end semana"

  "#end opcion"
]

let blockLines(block: Block) = [
  $"#start genero {Gender.toFileToken block.Gender}"

  for variant in block.Variants do
    yield! variantLines variant

  "#end genero"
]

/// Writes the full plan as format text, one block per line with fixed
/// indentation. Every string passes the write sanitizer first.
let write(plan: Plan) : string =
  let plan = normalize plan

  let lines = [
    $"#start plan v{plan.Version}"
    metaLine "titulo" plan.Title
    metaLine "semanas" plan.Weeks
    metaLine "generado" plan.Generated
    metaLine "origen" plan.Origin

    for block in plan.Blocks do
      yield! blockLines block

    for extra in plan.Extras do
      $"#x {extra}"

    "#end plan"
  ]

  String.concat "\n" lines
