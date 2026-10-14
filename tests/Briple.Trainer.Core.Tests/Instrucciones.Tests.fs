module Instrucciones.Tests

// El mapeo parámetros → instrucciones: lo fijado viene de la
// solicitud, lo pedido son los campos de origen agente del inventario,
// lo prohibido depende del flujo.

open System
open Expecto
open Plan.Types
open Atletas
open Memorias
open Parametros
open Solicitudes
open Instrucciones

let private atletaHombre = {
  Nombre = "hombre"
  Genero = Hombre
  Variantes = [ "3dias"; "5dias" ]
  Metas = []
  Enfasis = []
}

let private pedido = {
  Ajustes = []
  Enfasis = []
  RirPorSemana = []
  Variantes = [ "3dias"; "5dias" ]
}

let private camposDelAgente = [
  Titulo
  Generado
  OrigenArchivo
  TituloOpcion
  TituloDia
  NotasDia
  Circuito
  Vueltas
  Orden
  Nombre
  Reps
  Rir
  Descanso
  Notas
  Extras
]

let tests =
  testList "Instrucciones" [
    testCase
      "el ajuste fija los parámetros, pide los campos del agente y prohíbe tres cosas"
      (fun () ->
        let candidato: Plan = {
          Version = 1
          Titulo = "anterior"
          Semanas = 4
          Generado = ""
          Origen = ""
          Bloques = []
          Extras = []
        }

        let solicitud =
          solicitudAjuste atletaHombre [] 4 pedido candidato [ ("3dias", 68) ]

        let instrucciones = delAjuste solicitud

        Expect.contains
          instrucciones.Fijar
          (ConteoOpcion("3dias", 68, AjustePedido))
          "los conteos se fijan"

        Expect.equal
          instrucciones.Pedir
          camposDelAgente
          "el agente produce solo los campos de su origen"

        Expect.equal
          instrucciones.Prohibir
          [ CambiarConteos; OpcionesFueraDeVariantes; SalirseDelFormato ]
          "el ajuste no toca conteos, opciones ni formato")

    testCase "el plan nuevo no prohíbe conteos porque no lleva" (fun () ->
      let instrucciones = delNuevo(solicitudNuevo atletaHombre [] 4 pedido)

      Expect.equal
        instrucciones.Prohibir
        [ OpcionesFueraDeVariantes; SalirseDelFormato ]
        "sin conteos no hay nada que prohibir")

    testCase "las semanas y las variantes se fijan en ambos flujos" (fun () ->
      let nuevo = delNuevo(solicitudNuevo atletaHombre [] 4 pedido)

      Expect.contains
        nuevo.Fijar
        (SemanasPlan(4, AjustePedido))
        "semanas fijas"

      Expect.contains
        nuevo.Fijar
        (VariantesAtleta([ "3dias"; "5dias" ], AjustePedido))
        "variantes fijas")
  ]
