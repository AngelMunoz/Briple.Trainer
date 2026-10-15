module Briple.Trainer.Core.Tests.Memorias.Tests

// Una marca "muy leve" en un ejercicio se consolida en una
// memoria con delta paramétrico bien formado, y el ciclo
// propuesta/confirmada/descartada queda fijo.

open System
open Expecto
open Briple.Trainer.Core.Memories

let private marca etiqueta objetivo = {
  Target = objetivo
  Label = etiqueta
  Date = DateOnly(2026, 10, 13)
}

let tests =
  testList "Memorias" [
    testCase "una marca muy leve se consolida con delta paramétrico" (fun () ->
      let memoria = consolidate(marca Light (Exercise "Press de banca"))

      Expect.equal memoria.Status Proposed "nace propuesta"

      Expect.equal
        memoria.Deltas
        [ Load 5 ]
        "el ajuste automático sube la carga 5%"

      Expect.equal memoria.Summary "" "el resumen no se fija en el dominio"

      match memoria.Origin with
      | Mark marca ->
        Expect.equal marca.Target (Exercise "Press de banca") "trae su origen"
      | Exemption _ -> failtest "nacio de una marca")

    testCase "una marca pesado baja la carga" (fun () ->
      let memoria = consolidate(marca Heavy (Day "Lunes"))

      Expect.equal
        memoria.Deltas
        [ Load -5 ]
        "el ajuste automático baja la carga"

      Expect.equal memoria.Summary "" "el resumen no se fija en el dominio")

    testCase "dolor y sin equipo no tienen ajuste automático" (fun () ->
      let conDolor = consolidate(marca Pain (Exercise "Sentadilla"))
      let sinEquipo = consolidate(marca NoEquipment (Exercise "Dominadas"))

      Expect.isEmpty conDolor.Deltas "dolor va sin ajuste automático"

      Expect.isEmpty sinEquipo.Deltas "sin equipo va sin ajuste automático")

    testCase "confirmar fija los ajustes que el entrenador eligió" (fun () ->
      let memoria =
        marca Light (Exercise "Press de banca")
        |> consolidate
        |> confirm [ Rir -1; Reps 2 ]

      Expect.equal memoria.Status Confirmed "queda confirmada"

      Expect.equal
        memoria.Deltas
        [ Rir -1; Reps 2 ]
        "los deltas son los elegidos")

    testCase "descartar saca la memoria del ciclo" (fun () ->
      let memoria = marca Heavy (Exercise "Remo") |> consolidate |> discard

      Expect.equal memoria.Status Discarded "queda descartada")

    testCase
      "una excepcion del entrenador nace confirmada con la tecnica"
      (fun () ->
        let memoria =
          memoryOfExemption {
            Target = Day "Miércoles"
            Technique = "Sentadilla con salto"
          }

        Expect.equal memoria.Status Confirmed "no pasa por propuesta"

        Expect.equal
          memoria.Deltas
          [ PatternVariant "Sentadilla con salto" ]
          "la tecnica es la variante"

        Expect.equal memoria.Summary "" "el resumen no se fija en el dominio"

        (match memoria.Origin with
         | Exemption exemption ->
           Expect.equal
             exemption.Technique
             "Sentadilla con salto"
             "guarda la tecnica"
         | Mark _ -> failtest "nacio de una excepcion"))
  ]
