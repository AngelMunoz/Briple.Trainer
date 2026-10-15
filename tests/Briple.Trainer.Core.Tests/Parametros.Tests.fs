module Briple.Trainer.Core.Tests.Parametros.Tests

// Una memoria confirmada se refleja en los parámetros de la
// solicitud siguiente, y el inventario declara orígenes para cada
// campo del plan.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters

let private marca etiqueta objetivo = {
  Target = objetivo
  Label = etiqueta
  Date = DateOnly(2026, 10, 13)
}

let private memoriaDe etiqueta objetivo deltas =
  marca etiqueta objetivo |> consolidate |> confirm deltas

let tests =
  testList "Parametros" [
    testCase "el inventario declara orígenes para cada campo" (fun () ->
      let campos = [
        Title
        Weeks
        Generated
        FileOrigin
        Variants
        VariantTitle
        VariantWeeks
        Days
        DayTitle
        DayNotes
        Circuit
        Laps
        Order
        Name
        Reps
        Rir
        Rest
        Notes
        Extras
      ]

      for campo in campos do
        Expect.isNonEmpty (originsFor campo) $"campo %A{campo} sin orígenes")

    testCase "los ajustes del atleta salen del perfil" (fun () ->
      let atleta = {
        Name = "Pepe"
        Gender = Male
        Variants = [ "3dias"; "5dias" ]
        Goals = []
        Focus = []
      }

      Expect.equal
        (ofProfile atleta)
        [ AthleteVariants([ "3dias"; "5dias" ], AthleteProfile) ]
        "el perfil fija las variantes")

    testCase
      "una memoria confirmada se refleja en la solicitud siguiente"
      (fun () ->
        let memoria = memoriaDe Light (Exercise "Press de banca") [ Load 10 ]

        Expect.equal
          (ofConfirmedMemories [ memoria ])
          [
            Adjustment(
              Exercise "Press de banca",
              [ Load 10 ],
              ConfirmedMemory memoria
            )
          ]
          "el delta confirmado viaja como ajuste trazado")

    testCase "la memoria trazada mapea a su clase de origen" (fun () ->
      let memoria = memoriaDe Light (Exercise "Press de banca") [ Load 10 ]

      Expect.equal
        (classOf(ConfirmedMemory memoria))
        FromConfirmedMemory
        "memoria"

      Expect.equal (classOf AthleteProfile) FromProfile "perfil"

      Expect.equal (classOf PreviousPlan) FromPreviousPlan "plan anterior"

      Expect.equal (classOf TrainerBrief) FromBrief "ajuste pedido"

      Expect.equal (classOf Agent) FromAgent "agente")

    testCase "propuestas y descartadas no aportan parámetros" (fun () ->
      let propuesta = marca Light (Exercise "Press de banca") |> consolidate
      let descartada = propuesta |> discard

      Expect.isEmpty
        (ofConfirmedMemories [ propuesta; descartada ])
        "nada llega sin confirmar")

    testCase
      "la excepcion del entrenador aporta su objetivo como ajuste"
      (fun () ->
        let memoria =
          memoryOfExemption {
            Target = Day "Miércoles"
            Technique = "Sentadilla con salto"
          }

        Expect.equal
          (ofConfirmedMemories [ memoria ])
          [
            Adjustment(
              Day "Miércoles",
              [ PatternVariant "Sentadilla con salto" ],
              ConfirmedMemory memoria
            )
          ]
          "la excepcion confirmada viaja como ajuste")
  ]
