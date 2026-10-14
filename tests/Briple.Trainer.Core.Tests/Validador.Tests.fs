module Briple.Trainer.Core.Tests.Validador.Tests

// El validador de salidas del agente: formato limpio y parámetros de
// la solicitud (genero, opciones, semanas, conteos, RIR). La salida
// de prueba es el fixture del hombre, que cumple todo lo pedido.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Escritor
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Validador
open Briple.Trainer.Core.Tests.TestSupport

let private parsear nombre =
  match readFixture nombre |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}"
  | Ok parsed -> parsed

let private hombre = {
  Nombre = "hombre"
  Genero = Hombre
  Variantes = [ "3dias"; "5dias" ]
  Metas = []
  Enfasis = []
}

let private pedido = {
  Ajustes = []
  Enfasis = []
  RirPorSemana = [ (1, "3"); (2, "2-3"); (4, "1") ]
  Variantes = [ "3dias"; "5dias" ]
}

/// El fixture del hombre como salida de prueba: solo su bloque.
let private salidaHombre() =
  let mes1 = parsear "plan_entrenamiento_4sem.txt"

  let soloHombre = {
    mes1.Plan with
        Bloques = mes1.Plan.Bloques |> List.filter(fun b -> b.Genero = Hombre)
  }

  escribir soloHombre

let private esperarRegla regla veredicto =
  match veredicto with
  | Valido _ -> failtest $"se esperaba Rechazado por {regla}"
  | Rechazado fallos ->
    Expect.exists
      fallos
      (fun fallo -> fallo.Regla = regla)
      $"fallo de {regla} presente"

let tests =
  testList "Validador" [
    testCase "la salida del fixture valida como plan nuevo" (fun () ->
      let solicitud = solicitudNuevo hombre [] 4 pedido

      match validarNuevo solicitud (salidaHombre()) with
      | Valido plan -> Expect.equal plan.Semanas 4 "el veredicto trae el plan"
      | Rechazado fallos -> failtest $"se esperaba Valido: %A{fallos}")

    testCase "una salida con dos bloques se rechaza por genero" (fun () ->
      let solicitud = solicitudNuevo hombre [] 4 pedido
      let mes1 = parsear "plan_entrenamiento_4sem.txt"

      // el archivo completo trae el bloque de la mujer tambien
      esperarRegla "genero" (validarNuevo solicitud (escribir mes1.Plan)))

    testCase "las semanas distintas a las pedidas se rechazan" (fun () ->
      let solicitud = solicitudNuevo hombre [] 5 pedido

      esperarRegla "semanas" (validarNuevo solicitud (salidaHombre())))

    testCase "una opcion que no se pidio se rechaza" (fun () ->
      let pedidoUnVariante = { pedido with Variantes = [ "3dias" ] }

      let solicitud = solicitudNuevo hombre [] 4 pedidoUnVariante

      esperarRegla "opciones" (validarNuevo solicitud (salidaHombre())))

    testCase "un conteo que no se cumple se rechaza" (fun () ->
      let mes1 = parsear "plan_entrenamiento_4sem.txt"

      let solicitud =
        solicitudAjuste hombre [] 4 pedido mes1.Plan [ ("3dias", 68) ]

      esperarRegla "conteo" (validarAjuste solicitud (salidaHombre())))

    testCase "los conteos cumplidos validan el ajuste" (fun () ->
      let mes1 = parsear "plan_entrenamiento_4sem.txt"

      let solicitud =
        solicitudAjuste hombre [] 4 pedido mes1.Plan [
          ("3dias", 72)
          ("5dias", 72)
        ]

      match validarAjuste solicitud (salidaHombre()) with
      | Valido _ -> ()
      | Rechazado fallos -> failtest $"se esperaba Valido: %A{fallos}")

    testCase "un RIR que no coincide se rechaza" (fun () ->
      let pedidoRir = {
        pedido with
            RirPorSemana = [ (1, "0-0") ]
      }

      let solicitud = solicitudNuevo hombre [] 4 pedidoRir

      esperarRegla "rir" (validarNuevo solicitud (salidaHombre())))

    testCase "una salida que no es del formato se rechaza" (fun () ->
      let solicitud = solicitudNuevo hombre [] 4 pedido

      esperarRegla "formato" (validarNuevo solicitud "esto no es un plan"))

    testCase "una salida con warnings de formato se rechaza" (fun () ->
      let solicitud = solicitudNuevo hombre [] 4 pedido

      // meta desconocida dentro del plan: parsea con warning
      let conWarning =
        (salidaHombre()).Replace("#meta titulo ", "#meta titulox ")

      esperarRegla "formato" (validarNuevo solicitud conWarning))
  ]
