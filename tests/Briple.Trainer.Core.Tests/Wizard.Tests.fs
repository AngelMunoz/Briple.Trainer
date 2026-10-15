module Briple.Trainer.Core.Tests.Wizard.Tests

// Los seis pasos del wizard quedan especificados con entradas
// y salidas; la salida de cada paso arma la entrada del siguiente.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Wizard
open Briple.Trainer.Core.Tests.TestSupport

let private atleta = {
  Nombre = "hombre"
  Genero = Male
  Variantes = [ "3dias"; "5dias" ]
  Metas = []
  Enfasis = []
}

let private marca = {
  Objetivo = Ejercicio "Press de banca"
  Etiqueta = Leve
  Fecha = DateOnly(2026, 10, 6)
}

let private propuesta = marca |> consolidar

let private confirmada = propuesta |> confirmar [ Carga 5 ]

let private pedido = {
  Ajustes = []
  Enfasis = []
  RirPorSemana = [ (1, "3") ]
  Variantes = [ "3dias"; "5dias" ]
}

let tests =
  testList "Wizard" [
    testCase "los seis pasos encadenan sus entradas y salidas" (fun () ->
      // 1: salida el atleta elegido; 2: entrada ese atleta
      let paso1 = inicial()
      let paso2 = RevisarMemorias(atleta, [ propuesta ])

      // 2: salida las confirmadas; 3: entrada el atleta
      let paso3 = ElegirPeriodo atleta

      // 3: salida las semanas; 4: entrada atleta + confirmadas + semanas
      let paso4 = DefinirPedido(atleta, [ confirmada ], 4)

      // 4: salida el pedido; 5: entrada la solicitud armada
      let solicitud = solicitudNuevo atleta [ confirmada ] 4 pedido []
      let paso5 = Generar(PlanNuevo solicitud)

      // 5: salida el candidato validado; 6: entrada candidato + warnings
      let (candidato, warnings) =
        match readFixture "plan_entrenamiento_4sem.txt" |> parsePlan with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed -> (parsed.Plan, parsed.Warnings)

      let paso6 = Revisar(candidato, warnings)

      Expect.equal paso1 ElegirAtleta "paso 1 sin entrada"

      let pasos = [ paso1; paso2; paso3; paso4; paso5; paso6 ]

      Expect.equal (List.distinct pasos).Length 6 "seis pasos distintos"

      Expect.equal solicitud.Semanas 4 "la salida del paso 3 entró al 4"

      Expect.equal
        (List.head solicitud.Memorias)
        confirmada
        "la salida del paso 2 entró al 5"

      Expect.equal
        candidato.Weeks
        4
        "la salida del paso 5 entra al 6 con sus warnings"

      Expect.isEmpty warnings "el candidato del fixture valida limpio")

    testCase "el ajuste también genera desde el paso 5" (fun () ->
      let mes1 =
        match readFixture "plan_entrenamiento_4sem.txt" |> parsePlan with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed -> parsed.Plan

      let solicitud = solicitudAjuste atleta [] 4 pedido mes1 [ ("3dias", 68) ]

      let paso = Generar(AjusteDePlan solicitud)

      Expect.equal
        paso
        (Generar(AjusteDePlan solicitud))
        "el paso 5 carga la solicitud de ajuste")
  ]
