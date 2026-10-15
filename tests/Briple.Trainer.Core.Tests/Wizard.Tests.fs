module Briple.Trainer.Core.Tests.Wizard.Tests

// Los seis pasos del wizard quedan especificados con entradas
// y salidas; la salida de cada paso arma la entrada del siguiente.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Wizard
open Briple.Trainer.Core.Tests.TestSupport

let private atleta = {
  Name = "hombre"
  Gender = Male
  Variants = [ "3dias"; "5dias" ]
  Goals = []
  Focus = []
}

let private marca = {
  Target = Exercise "Press de banca"
  Label = Light
  Date = DateOnly(2026, 10, 6)
}

let private propuesta = marca |> consolidate

let private confirmada = propuesta |> confirm [ Load 5 ]

let private pedido = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3") ]
  Variants = [ "3dias"; "5dias" ]
}

let tests =
  testList "Wizard" [
    testCase "los seis pasos encadenan sus entradas y salidas" (fun () ->
      // 1: salida el atleta elegido; 2: entrada ese atleta
      let paso1 = initial()
      let paso2 = ReviewMemories(atleta, [ propuesta ])

      // 2: salida las confirmadas; 3: entrada el atleta
      let paso3 = ChoosePeriod atleta

      // 3: salida las semanas; 4: entrada atleta + confirmadas + semanas
      let paso4 = DefineBrief(atleta, [ confirmada ], 4)

      // 4: salida el pedido; 5: entrada la solicitud armada
      let solicitud = newRequest atleta [ confirmada ] 4 pedido []
      let paso5 = Generate(NewPlan solicitud)

      // 5: salida el candidato validado; 6: entrada candidato + warnings
      let (candidato, warnings) =
        match readFixture "plan_entrenamiento_4sem.txt" |> parsePlan with
        | Error error -> failtest $"parse error at line {error.Line}"
        | Ok parsed -> (parsed.Plan, parsed.Warnings)

      let paso6 = Review(candidato, warnings)

      Expect.equal paso1 ChooseAthlete "paso 1 sin entrada"

      let pasos = [ paso1; paso2; paso3; paso4; paso5; paso6 ]

      Expect.equal (List.distinct pasos).Length 6 "seis pasos distintos"

      Expect.equal solicitud.Weeks 4 "la salida del paso 3 entró al 4"

      Expect.equal
        (List.head solicitud.Memories)
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

      let solicitud =
        adjustmentRequest atleta [] 4 pedido mes1 [ ("3dias", 68) ]

      let paso = Generate(PlanAdjustment solicitud)

      Expect.equal
        paso
        (Generate(PlanAdjustment solicitud))
        "el paso 5 carga la solicitud de ajuste")
  ]
