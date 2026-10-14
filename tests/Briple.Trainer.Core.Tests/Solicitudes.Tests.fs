module Briple.Trainer.Core.Tests.Solicitudes.Tests

// "Mes 2" se expresa como ajuste sobre mes 1; los conteos
// 68/104/136 viajan en la solicitud como parámetros trazados.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Parametros
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Tests.TestSupport

let private parsearFixture nombre =
  match readFixture nombre |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}: {error.Message}"
  | Ok parsed -> parsed

let private memoriaConfirmada =
  {
    Objetivo = Ejercicio "Press de banca"
    Etiqueta = Leve
    Fecha = DateOnly(2026, 10, 6)
  }
  |> consolidar
  |> confirmar [ Carga 5 ]

let private pedidoHombre = {
  Ajustes = []
  Enfasis = []
  RirPorSemana = [ (1, "3"); (2, "2-3"); (3, "2"); (4, "1") ]
  Variantes = [ "3dias"; "5dias" ]
}

let tests =
  testList "Solicitudes" [
    testCase
      "mes 2 se expresa como ajuste sobre mes 1 con los conteos 68/104/136"
      (fun () ->
        let mes1 = parsearFixture "plan_entrenamiento_4sem.txt"
        let mes2 = parsearFixture "plan_mes2_4sem.txt"

        let hombre =
          atletasDelPlan mes1.Plan |> List.find(fun a -> a.Genero = Hombre)

        let mujer =
          atletasDelPlan mes1.Plan |> List.find(fun a -> a.Genero = Mujer)

        let conteosHombre = conteosDelPlan hombre mes2.Plan
        let conteosMujer = conteosDelPlan mujer mes2.Plan

        Expect.equal
          conteosHombre
          [ ("3dias", 68); ("5dias", 104) ]
          "conteos objetivo del hombre"

        Expect.equal
          conteosMujer
          [ ("5dias", 136) ]
          "conteos objetivo de la mujer"

        let solicitud =
          solicitudAjuste
            hombre
            [ memoriaConfirmada ]
            4
            pedidoHombre
            mes1.Plan
            conteosHombre

        let parametros = parametrosDeAjuste solicitud

        Expect.contains
          parametros
          (SemanasPlan(4, AjustePedido))
          "el período viaja"

        Expect.contains
          parametros
          (VariantesAtleta([ "3dias"; "5dias" ], AjustePedido))
          "las variantes del pedido"

        Expect.contains
          parametros
          (ConteoOpcion("3dias", 68, AjustePedido))
          "68 pedido: cambia respecto de mes 1 (72)"

        Expect.contains
          parametros
          (ConteoOpcion("5dias", 104, AjustePedido))
          "104 pedido: cambia respecto de mes 1 (72)"

        Expect.contains
          parametros
          (Ajuste(
            Ejercicio "Press de banca",
            [ Carga 5 ],
            MemoriaConfirmada memoriaConfirmada
          ))
          "la memoria confirmada viaja a la solicitud")

    testCase
      "un conteo igual al del plan anterior traza a PlanAnterior"
      (fun () ->
        let mes1 = parsearFixture "plan_entrenamiento_4sem.txt"

        let hombre =
          atletasDelPlan mes1.Plan |> List.find(fun a -> a.Genero = Hombre)

        let mismosConteos = conteosDelPlan hombre mes1.Plan

        let solicitud =
          solicitudAjuste hombre [] 4 pedidoHombre mes1.Plan mismosConteos

        Expect.contains
          (parametrosDeAjuste solicitud)
          (ConteoOpcion("3dias", 72, PlanAnterior))
          "sin cambio va del plan anterior")

    testCase "el plan nuevo junta perfil, memorias, pedido y conteos" (fun () ->
      let mes1 = parsearFixture "plan_entrenamiento_4sem.txt"

      let hombre =
        atletasDelPlan mes1.Plan |> List.find(fun a -> a.Genero = Hombre)

      let pedido = {
        pedidoHombre with
            Variantes = [ "3dias" ]
      }

      let parametros =
        solicitudNuevo hombre [ memoriaConfirmada ] 2 pedido [ ("3dias", 36) ]
        |> parametrosDeNuevo

      Expect.contains
        parametros
        (VariantesAtleta([ "3dias"; "5dias" ], PerfilDelAtleta))
        "el perfil fija todas sus variantes"

      Expect.contains
        parametros
        (VariantesAtleta([ "3dias" ], AjustePedido))
        "el pedido elige con cuáles generar"

      Expect.contains
        parametros
        (SemanasPlan(2, AjustePedido))
        "el período del pedido"

      Expect.contains
        parametros
        (RirSemana(1, "3", AjustePedido))
        "el RIR por semana del pedido"

      Expect.contains
        parametros
        (ConteoOpcion("3dias", 36, AjustePedido))
        "los conteos objetivo del pedido")
  ]
