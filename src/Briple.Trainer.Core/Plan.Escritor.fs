module Briple.Trainer.Core.Plan.Escritor

open Briple.Trainer.Core.Plan.Types

let metaLine clave valor = $"#meta {clave} {valor}"

let ejLine(ejercicio: Ejercicio) =
  $"#ej {ejercicio.Circuito}|{ejercicio.Vueltas}|{ejercicio.Orden}|{ejercicio.Nombre}|{ejercicio.Reps}|{ejercicio.Rir}|{ejercicio.Descanso}|{ejercicio.Notas}"

let lineasDia(dia: Dia) = [
  yield $"#start dia {dia.Id}"

  yield! dia.Titulo |> Option.toList |> List.map(metaLine "titulo")

  for ejercicio in dia.Ejercicios do
    yield ejLine ejercicio

  yield "#end dia"
]

let lineasOpcion(opcion: Opcion) = [
  yield $"#start opcion {opcion.Id}"

  yield! opcion.Titulo |> Option.toList |> List.map(metaLine "titulo")

  for semana in opcion.Semanas do
    yield $"#start semana {semana.Numero}"

    for dia in semana.Dias do
      yield! lineasDia dia

    yield "#end semana"

  yield "#end opcion"
]

let lineasBloque(bloque: Bloque) = [
  yield $"#start genero {Genero.toString bloque.Genero}"

  for opcion in bloque.Opciones do
    yield! lineasOpcion opcion

  yield "#end genero"
]

/// Escribe el plan completo como texto del formato, un bloque por
/// linea con indentacion fija.
let escribir(plan: Plan) : string =
  let lineas = [
    yield $"#start plan v{plan.Version}"
    yield metaLine "titulo" plan.Titulo
    yield metaLine "semanas" plan.Semanas
    yield metaLine "generado" plan.Generado
    yield metaLine "origen" plan.Origen

    for bloque in plan.Bloques do
      yield! lineasBloque bloque

    for extra in plan.Extras do
      yield $"#x {extra}"

    yield "#end plan"
  ]

  String.concat "\n" lineas
