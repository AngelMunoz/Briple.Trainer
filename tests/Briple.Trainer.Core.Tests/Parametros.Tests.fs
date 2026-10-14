module Briple.Trainer.Core.Tests.Parametros.Tests

// Una memoria confirmada se refleja en los parámetros de la
// solicitud siguiente, y el inventario declara orígenes para cada
// campo del plan.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Parametros

let private marca etiqueta objetivo = {
  Objetivo = objetivo
  Etiqueta = etiqueta
  Fecha = DateOnly(2026, 10, 13)
}

let private memoriaDe etiqueta objetivo deltas =
  marca etiqueta objetivo |> consolidar |> confirmar deltas

let tests =
  testList "Parametros" [
    testCase "el inventario declara orígenes para cada campo" (fun () ->
      let campos = [
        Titulo
        Semanas
        Generado
        OrigenArchivo
        Opciones
        TituloOpcion
        SemanasDeOpcion
        Dias
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

      for campo in campos do
        Expect.isNonEmpty (origenesDe campo) $"campo %A{campo} sin orígenes")

    testCase "los ajustes del atleta salen del perfil" (fun () ->
      let atleta = {
        Nombre = "Pepe"
        Genero = Hombre
        Variantes = [ "3dias"; "5dias" ]
        Metas = []
        Enfasis = []
      }

      Expect.equal
        (delPerfil atleta)
        [ VariantesAtleta([ "3dias"; "5dias" ], PerfilDelAtleta) ]
        "el perfil fija las variantes")

    testCase
      "una memoria confirmada se refleja en la solicitud siguiente"
      (fun () ->
        let memoria = memoriaDe Leve (Ejercicio "Press de banca") [ Carga 10 ]

        Expect.equal
          (deMemoriasConfirmadas [ memoria ])
          [
            Ajuste(
              Ejercicio "Press de banca",
              [ Carga 10 ],
              MemoriaConfirmada memoria
            )
          ]
          "el delta confirmado viaja como ajuste trazado")

    testCase "la memoria trazada mapea a su clase de origen" (fun () ->
      let memoria = memoriaDe Leve (Ejercicio "Press de banca") [ Carga 10 ]

      Expect.equal
        (claseDe(MemoriaConfirmada memoria))
        DeMemoriaConfirmada
        "memoria"

      Expect.equal (claseDe PerfilDelAtleta) DelPerfil "perfil"

      Expect.equal (claseDe PlanAnterior) DelPlanAnterior "plan anterior"

      Expect.equal (claseDe AjustePedido) DelAjustePedido "ajuste pedido"

      Expect.equal (claseDe Agente) DelAgente "agente")

    testCase "propuestas y descartadas no aportan parámetros" (fun () ->
      let propuesta = marca Leve (Ejercicio "Press de banca") |> consolidar
      let descartada = propuesta |> descartar

      Expect.isEmpty
        (deMemoriasConfirmadas [ propuesta; descartada ])
        "nada llega sin confirmar")

    testCase
      "la excepcion del entrenador aporta su objetivo como ajuste"
      (fun () ->
        let memoria =
          memoriaDeExcepcion {
            Objetivo = Dia "Miércoles"
            Tecnica = "Sentadilla con salto"
          }

        Expect.equal
          (deMemoriasConfirmadas [ memoria ])
          [
            Ajuste(
              Dia "Miércoles",
              [ VariantePatron "Sentadilla con salto" ],
              MemoriaConfirmada memoria
            )
          ]
          "la excepcion confirmada viaja como ajuste")
  ]
