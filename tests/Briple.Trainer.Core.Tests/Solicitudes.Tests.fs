module Briple.Trainer.Core.Tests.Solicitudes.Tests

// "Mes 2" se expresa como ajuste sobre mes 1; los conteos
// 68/104/136 viajan en la solicitud como parámetros trazados.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Tests.TestSupport

let private parsearFixture nombre =
  match readFixture nombre |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}: {error.Message}"
  | Ok parsed -> parsed

let private memoriaConfirmada =
  {
    Target = Exercise "Press de banca"
    Label = Light
    Date = DateOnly(2026, 10, 6)
  }
  |> consolidate
  |> confirm [ Load 5 ]

let private pedidoHombre = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3"); (2, "2-3"); (3, "2"); (4, "1") ]
  Variants = [ "3dias"; "5dias" ]
}

let tests =
  testList "Solicitudes" [
    testCase
      "mes 2 se expresa como ajuste sobre mes 1 con los conteos 68/104/136"
      (fun () ->
        let mes1 = parsearFixture "plan_entrenamiento_4sem.txt"
        let mes2 = parsearFixture "plan_mes2_4sem.txt"

        let hombre =
          athletesOfPlan mes1.Plan |> List.find(fun a -> a.Gender = Male)

        let mujer =
          athletesOfPlan mes1.Plan |> List.find(fun a -> a.Gender = Female)

        let conteosHombre = planCounts hombre mes2.Plan
        let conteosMujer = planCounts mujer mes2.Plan

        Expect.equal
          conteosHombre
          [ ("3dias", 68); ("5dias", 104) ]
          "conteos objetivo del hombre"

        Expect.equal
          conteosMujer
          [ ("5dias", 136) ]
          "conteos objetivo de la mujer"

        let solicitud =
          adjustmentRequest
            hombre
            [ memoriaConfirmada ]
            4
            pedidoHombre
            mes1.Plan
            conteosHombre

        let parametros = parametersForAdjustment solicitud

        Expect.contains
          parametros
          (PlanWeeks(4, TrainerBrief))
          "el período viaja"

        Expect.contains
          parametros
          (AthleteVariants([ "3dias"; "5dias" ], TrainerBrief))
          "las variantes del pedido"

        Expect.contains
          parametros
          (VariantCount("3dias", 68, TrainerBrief))
          "68 pedido: cambia respecto de mes 1 (72)"

        Expect.contains
          parametros
          (VariantCount("5dias", 104, TrainerBrief))
          "104 pedido: cambia respecto de mes 1 (72)"

        Expect.contains
          parametros
          (Adjustment(
            Exercise "Press de banca",
            [ Load 5 ],
            ConfirmedMemory memoriaConfirmada
          ))
          "la memoria confirmada viaja a la solicitud")

    testCase
      "un conteo igual al del plan anterior traza a PlanAnterior"
      (fun () ->
        let mes1 = parsearFixture "plan_entrenamiento_4sem.txt"

        let hombre =
          athletesOfPlan mes1.Plan |> List.find(fun a -> a.Gender = Male)

        let mismosConteos = planCounts hombre mes1.Plan

        let solicitud =
          adjustmentRequest hombre [] 4 pedidoHombre mes1.Plan mismosConteos

        Expect.contains
          (parametersForAdjustment solicitud)
          (VariantCount("3dias", 72, PreviousPlan))
          "sin cambio va del plan anterior")

    testCase "el plan nuevo junta perfil, memorias, pedido y conteos" (fun () ->
      let mes1 = parsearFixture "plan_entrenamiento_4sem.txt"

      let hombre =
        athletesOfPlan mes1.Plan |> List.find(fun a -> a.Gender = Male)

      let pedido = {
        pedidoHombre with
            Variants = [ "3dias" ]
      }

      let parametros =
        newRequest hombre [ memoriaConfirmada ] 2 pedido [ ("3dias", 36) ]
        |> parametersForNew

      Expect.contains
        parametros
        (AthleteVariants([ "3dias"; "5dias" ], AthleteProfile))
        "el perfil fija todas sus variantes"

      Expect.contains
        parametros
        (AthleteVariants([ "3dias" ], TrainerBrief))
        "el pedido elige con cuáles generar"

      Expect.contains
        parametros
        (PlanWeeks(2, TrainerBrief))
        "el período del pedido"

      Expect.contains
        parametros
        (WeekRir(1, "3", TrainerBrief))
        "el RIR por semana del pedido"

      Expect.contains
        parametros
        (VariantCount("3dias", 36, TrainerBrief))
        "los conteos objetivo del pedido")
  ]
