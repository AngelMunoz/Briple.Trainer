module Memorias.Tests

// Una marca "muy leve" en un ejercicio se consolida en una
// memoria con delta paramétrico bien formado, y el ciclo
// propuesta/confirmada/descartada queda fijo.

open System
open Expecto
open Memorias

let private marca etiqueta objetivo = {
  Objetivo = objetivo
  Etiqueta = etiqueta
  Fecha = DateOnly(2026, 10, 13)
}

let tests =
  testList "Memorias" [
    testCase "una marca muy leve se consolida con delta paramétrico" (fun () ->
      let memoria = consolidar(marca Leve (Ejercicio "Press de banca"))

      Expect.equal memoria.Estado Propuesta "nace propuesta"

      Expect.equal
        memoria.Deltas
        [ Carga 5 ]
        "el ajuste automático sube la carga 5%"

      Expect.stringContains
        memoria.Resumen
        "muy leve"
        "el resumen narra el hecho"

      Expect.equal
        memoria.Origen.Objetivo
        (Ejercicio "Press de banca")
        "trae su origen")

    testCase "una marca pesado baja la carga" (fun () ->
      let memoria = consolidar(marca Pesado (Dia "Lunes"))

      Expect.equal
        memoria.Deltas
        [ Carga -5 ]
        "el ajuste automático baja la carga"

      Expect.stringContains memoria.Resumen "Lunes" "el resumen nombra el día")

    testCase "dolor y sin equipo no tienen ajuste automático" (fun () ->
      let conDolor = consolidar(marca Dolor (Ejercicio "Sentadilla"))
      let sinEquipo = consolidar(marca SinEquipo (Ejercicio "Dominadas"))

      Expect.isEmpty conDolor.Deltas "dolor va sin ajuste automático"

      Expect.isEmpty sinEquipo.Deltas "sin equipo va sin ajuste automático")

    testCase "confirmar fija los ajustes que el entrenador eligió" (fun () ->
      let memoria =
        marca Leve (Ejercicio "Press de banca")
        |> consolidar
        |> confirmar [ Rir -1; Repeticiones 2 ]

      Expect.equal memoria.Estado Confirmada "queda confirmada"

      Expect.equal
        memoria.Deltas
        [ Rir -1; Repeticiones 2 ]
        "los deltas son los elegidos")

    testCase "descartar saca la memoria del ciclo" (fun () ->
      let memoria = marca Pesado (Ejercicio "Remo") |> consolidar |> descartar

      Expect.equal memoria.Estado Descartada "queda descartada")
  ]
