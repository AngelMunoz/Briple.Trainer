module Atletas.Tests

// Gate F2: los planes existentes se descomponen en dos atletas reales
// (el hombre con dos variantes, la mujer con una) y un atleta nuevo se
// define solo con parámetros, sin tocar tipos.

open Expecto
open Plan.Types
open Plan.Parser
open Atletas
open Briple.Trainer.Core.Tests.TestSupport

let private dosAtletasDe plan =
  let atletas = atletasDelPlan plan

  Expect.equal atletas.Length 2 "un bloque por atleta"

  (List.find (fun a -> a.Genero = Hombre) atletas,
   List.find (fun a -> a.Genero = Mujer) atletas)

let tests =
  testList "Atletas" [
    testCase "mes 1: dos atletas, el hombre con dos variantes" (fun () ->
      match readFixture "plan_entrenamiento_4sem.txt" |> parsePlan with
      | Error error -> failtest $"parse error at line {error.Line}"
      | Ok parsed ->
        let hombre, mujer = dosAtletasDe parsed.Plan

        Expect.equal
          hombre.Variantes
          [ "3dias"; "5dias" ]
          "las opciones del hombre son sus variantes, no atletas distintos"

        Expect.equal
          mujer.Variantes
          [ "5dias" ]
          "la mujer entrena una variante")

    testCase "mes 2: los mismos dos atletas" (fun () ->
      match readFixture "plan_mes2_4sem.txt" |> parsePlan with
      | Error error -> failtest $"parse error at line {error.Line}"
      | Ok parsed ->
        let hombre, mujer = dosAtletasDe parsed.Plan

        Expect.equal
          hombre.Variantes
          [ "3dias"; "5dias" ]
          "mismas variantes que mes 1"

        Expect.equal mujer.Variantes [ "5dias" ] "una variante")

    testCase
      "un tercer atleta de prueba se define solo con parámetros"
      (fun () ->
        let tercero = {
          Nombre = "Prueba"
          Genero = Mujer
          Variantes = [ "4dias" ]
          Metas = [ "fuerza" ]
          Enfasis = [ "glúteo" ]
        }

        Expect.equal
          tercero.Variantes
          [ "4dias" ]
          "variantes nuevas sin tocar tipos"

        Expect.equal tercero.Genero Mujer "el perfil es del propio atleta")
  ]
