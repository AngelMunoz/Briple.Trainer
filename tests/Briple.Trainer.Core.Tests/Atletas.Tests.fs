module Briple.Trainer.Core.Tests.Atletas.Tests

// Los planes existentes se descomponen en dos atletas reales
// (el hombre con dos variantes, la mujer con una) y un atleta nuevo se
// define solo con parámetros, sin tocar tipos.

open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Tests.TestSupport

let private dosAtletasDe plan =
  let athletes = athletesOfPlan plan

  Expect.equal athletes.Length 2 "un bloque por atleta"

  (List.find (fun a -> a.Gender = Male) athletes,
   List.find (fun a -> a.Gender = Female) athletes)

let tests =
  testList "Atletas" [
    testCase "mes 1: dos atletas, el hombre con dos variantes" (fun () ->
      match readFixture "plan_entrenamiento_4sem.txt" |> parsePlan with
      | Error error -> failtest $"parse error at line {error.Line}"
      | Ok parsed ->
        let hombre, mujer = dosAtletasDe parsed.Plan

        Expect.equal
          hombre.Variants
          [ "3dias"; "5dias" ]
          "las opciones del hombre son sus variantes, no atletas distintos"

        Expect.equal mujer.Variants [ "5dias" ] "la mujer entrena una variante")

    testCase "mes 2: los mismos dos atletas" (fun () ->
      match readFixture "plan_mes2_4sem.txt" |> parsePlan with
      | Error error -> failtest $"parse error at line {error.Line}"
      | Ok parsed ->
        let hombre, mujer = dosAtletasDe parsed.Plan

        Expect.equal
          hombre.Variants
          [ "3dias"; "5dias" ]
          "mismas variantes que mes 1"

        Expect.equal mujer.Variants [ "5dias" ] "una variante")

    testCase
      "un tercer atleta de prueba se define solo con parámetros"
      (fun () ->
        let tercero = {
          Name = "Prueba"
          Gender = Female
          Variants = [ "4dias" ]
          Goals = [ "fuerza" ]
          Focus = [ "glúteo" ]
        }

        Expect.equal
          tercero.Variants
          [ "4dias" ]
          "variantes nuevas sin tocar tipos"

        Expect.equal tercero.Gender Female "el perfil es del propio atleta")
  ]
