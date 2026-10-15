module Briple.Trainer.Core.Tests.Validador.Tests

// El validador de salidas del agente: formato limpio y parámetros de
// la solicitud (genero, opciones, semanas, conteos, RIR). La salida
// de prueba es el fixture del hombre, que cumple todo lo pedido.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Writer
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator
open Briple.Trainer.Core.Tests.TestSupport

let private parsear nombre =
  match readFixture nombre |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}"
  | Ok parsed -> parsed

let private hombre = {
  Name = "hombre"
  Gender = Male
  Variants = [ "3dias"; "5dias" ]
  Goals = []
  Focus = []
}

let private pedido = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3"); (2, "2-3"); (4, "1") ]
  Variants = [ "3dias"; "5dias" ]
}

/// El fixture del hombre como salida de prueba: solo su bloque.
let private salidaHombre() =
  let mes1 = parsear "plan_entrenamiento_4sem.txt"

  let soloHombre = {
    mes1.Plan with
        Blocks = mes1.Plan.Blocks |> List.filter(fun b -> b.Gender = Male)
  }

  write soloHombre

let private esperarRegla regla veredicto =
  match veredicto with
  | Valid _ -> failtest $"se esperaba Rechazado por {regla}"
  | Rejected fallos ->
    Expect.exists
      fallos
      (fun fallo -> fallo.Rule = regla)
      $"fallo de {regla} presente"

let tests =
  testList "Validador" [
    testCase "la salida del fixture valida como plan nuevo" (fun () ->
      let solicitud =
        newRequest hombre [] 4 pedido [ ("3dias", 72); ("5dias", 72) ]

      match validateNew solicitud (salidaHombre()) with
      | Valid plan -> Expect.equal plan.Weeks 4 "el veredicto trae el plan"
      | Rejected fallos -> failtest $"se esperaba Valido: %A{fallos}")

    testCase "una salida con dos bloques se rechaza por genero" (fun () ->
      let solicitud = newRequest hombre [] 4 pedido []
      let mes1 = parsear "plan_entrenamiento_4sem.txt"

      // el archivo completo trae el bloque de la mujer tambien
      esperarRegla "gender" (validateNew solicitud (write mes1.Plan)))

    testCase "las semanas distintas a las pedidas se rechazan" (fun () ->
      let solicitud = newRequest hombre [] 5 pedido []

      esperarRegla "weeks" (validateNew solicitud (salidaHombre())))

    testCase "una opcion que no se pidio se rechaza" (fun () ->
      let pedidoUnVariante = { pedido with Variants = [ "3dias" ] }

      let solicitud = newRequest hombre [] 4 pedidoUnVariante []

      esperarRegla "variants" (validateNew solicitud (salidaHombre())))

    testCase "un conteo que no se cumple se rechaza" (fun () ->
      let mes1 = parsear "plan_entrenamiento_4sem.txt"

      let solicitud =
        adjustmentRequest hombre [] 4 pedido mes1.Plan [ ("3dias", 68) ]

      esperarRegla "count" (validateAdjustment solicitud (salidaHombre())))

    testCase "un conteo incumplido se rechaza tambien en plan nuevo" (fun () ->
      let solicitud = newRequest hombre [] 4 pedido [ ("3dias", 68) ]

      esperarRegla "count" (validateNew solicitud (salidaHombre())))

    testCase "los conteos cumplidos validan el ajuste" (fun () ->
      let mes1 = parsear "plan_entrenamiento_4sem.txt"

      let solicitud =
        adjustmentRequest hombre [] 4 pedido mes1.Plan [
          ("3dias", 72)
          ("5dias", 72)
        ]

      match validateAdjustment solicitud (salidaHombre()) with
      | Valid _ -> ()
      | Rejected fallos -> failtest $"se esperaba Valido: %A{fallos}")

    testCase "un RIR que no coincide se rechaza" (fun () ->
      let pedidoRir = {
        pedido with
            RirPerWeek = [ (1, "0-0") ]
      }

      let solicitud = newRequest hombre [] 4 pedidoRir []

      esperarRegla "rir" (validateNew solicitud (salidaHombre())))

    testCase "una salida que no es del formato se rechaza" (fun () ->
      let solicitud = newRequest hombre [] 4 pedido []

      esperarRegla "format" (validateNew solicitud "esto no es un plan"))

    testCase "una salida con warnings de formato se rechaza" (fun () ->
      let solicitud = newRequest hombre [] 4 pedido []

      // meta desconocida dentro del plan: parsea con warning
      let conWarning =
        (salidaHombre()).Replace("#meta titulo ", "#meta titulox ")

      esperarRegla "format" (validateNew solicitud conWarning))
  ]
