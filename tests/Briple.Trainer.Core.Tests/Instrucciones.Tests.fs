module Briple.Trainer.Core.Tests.Instrucciones.Tests

// El mapeo parámetros → instrucciones: lo fijado viene de la
// solicitud, lo pedido son los campos de origen agente del inventario,
// lo prohibido depende del flujo.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Instructions

let private atletaHombre = {
  Name = "hombre"
  Gender = Male
  Variants = [ "3dias"; "5dias" ]
  Goals = []
  Focus = []
}

let private pedido = {
  Adjustments = []
  Focus = []
  RirPerWeek = []
  Variants = [ "3dias"; "5dias" ]
}

let private camposDelAgente = [
  Title
  Generated
  FileOrigin
  VariantTitle
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

let tests =
  testList "Instrucciones" [
    testCase
      "el ajuste fija los parámetros, pide los campos del agente y prohíbe tres cosas"
      (fun () ->
        let candidato: Plan = {
          Version = 1
          Title = "anterior"
          Weeks = 4
          Generated = ""
          Origin = ""
          Blocks = []
          Extras = []
        }

        let solicitud =
          adjustmentRequest atletaHombre [] 4 pedido candidato [
            ("3dias", 68)
          ]

        let instrucciones = forAdjustment solicitud

        Expect.contains
          instrucciones.Fix
          (VariantCount("3dias", 68, TrainerBrief))
          "los conteos se fijan"

        Expect.equal
          instrucciones.Ask
          camposDelAgente
          "el agente produce solo los campos de su origen"

        Expect.equal
          instrucciones.Forbid
          [ ChangeCounts; OutsideVariants; LeaveFormat ]
          "el ajuste no toca conteos, opciones ni formato")

    testCase "el plan nuevo fija y prohíbe sus conteos" (fun () ->
      let instrucciones =
        forNew(newRequest atletaHombre [] 4 pedido [ ("3dias", 68) ])

      Expect.contains
        instrucciones.Fix
        (VariantCount("3dias", 68, TrainerBrief))
        "los conteos del pedido se fijan"

      Expect.equal
        instrucciones.Forbid
        [ ChangeCounts; OutsideVariants; LeaveFormat ]
        "el nuevo no toca conteos, opciones ni formato")

    testCase "las semanas y las variantes se fijan en ambos flujos" (fun () ->
      let nuevo = forNew(newRequest atletaHombre [] 4 pedido [])

      Expect.contains nuevo.Fix (PlanWeeks(4, TrainerBrief)) "semanas fijas"

      Expect.contains
        nuevo.Fix
        (AthleteVariants([ "3dias"; "5dias" ], TrainerBrief))
        "variantes fijas")
  ]
