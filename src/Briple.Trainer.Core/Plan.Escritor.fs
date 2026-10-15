module Briple.Trainer.Core.Plan.Escritor

open Briple.Trainer.Core.Plan.Types

let metaLine key value = $"#meta {key} {value}"

let exerciseLine(exercise: Exercise) =
  $"#ej {exercise.Circuit}|{exercise.Laps}|{exercise.Order}|{exercise.Name}|{exercise.Reps}|{exercise.Rir}|{exercise.Rest}|{exercise.Notes}"

let dayLines(day: Day) = [
  yield $"#start dia {day.Id}"

  yield! day.Title |> Option.toList |> List.map(metaLine "titulo")

  for exercise in day.Exercises do
    yield exerciseLine exercise

  yield "#end dia"
]

let variantLines(variant: Variant) = [
  yield $"#start opcion {variant.Id}"

  yield! variant.Title |> Option.toList |> List.map(metaLine "titulo")

  for week in variant.Weeks do
    yield $"#start semana {week.Number}"

    for day in week.Days do
      yield! dayLines day

    yield "#end semana"

  yield "#end opcion"
]

let blockLines(block: Block) = [
  yield $"#start genero {Gender.toFileToken block.Gender}"

  for variant in block.Variants do
    yield! variantLines variant

  yield "#end genero"
]

/// Writes the full plan as format text, one block per line with fixed
/// indentation.
let write(plan: Plan) : string =
  let lines = [
    yield $"#start plan v{plan.Version}"
    yield metaLine "titulo" plan.Title
    yield metaLine "semanas" plan.Weeks
    yield metaLine "generado" plan.Generated
    yield metaLine "origen" plan.Origin

    for block in plan.Blocks do
      yield! blockLines block

    for extra in plan.Extras do
      yield $"#x {extra}"

    yield "#end plan"
  ]

  String.concat "\n" lines
