module Briple.Trainer.Core.Tests.Prompts.Tests

// El render del prompt: el formato, lo fijado (semanas, variantes,
// conteos, RIR), lo prohibido, y el plan anterior en el ajuste.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Tests.TestSupport

let private parsear nombre =
  match readFixture nombre |> parsePlan with
  | Error error -> failtest $"parse error at line {error.Line}"
  | Ok parsed -> parsed

let private hombre = {
  Name = "Pepe"
  Gender = Male
  Variants = [ "3dias"; "5dias" ]
  Goals = [ "fuerza" ]
  Focus = [ "gluteo" ]
}

let private pedido = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3"); (2, "2-3"); (4, "1") ]
  Variants = [ "3dias"; "5dias" ]
}

let tests =
  testList "Prompts" [
    testCase
      "el prompt del plan nuevo trae esquema, fijado y prohibido"
      (fun () ->
        let solicitud =
          newRequest hombre [] 4 pedido [ ("3dias", 72); ("5dias", 72) ]

        let prompt = newPlanPrompt solicitud

        Expect.stringContains
          prompt
          "- the plan covers 4 weeks"
          "las semanas se fijan"

        Expect.stringContains
          prompt
          "- the plan has one option per variant: 3dias, 5dias"
          "las variantes se fijan"

        Expect.stringContains
          prompt
          "- week 2 targets RIR 2-3 (added automatically, do not write it)"
          "el RIR se fija"

        Expect.stringContains
          prompt
          "- option 3dias has exactly 72 exercises in total"
          "los conteos se fijan"

        Expect.stringContains prompt "FORBIDDEN" "las prohibiciones van"

        Expect.stringContains
          system
          "RESPONSE SCHEMA"
          "el sistema trae el esquema json"

        Expect.stringContains
          system
          "\"ejercicios\": ["
          "el esquema declara los ejercicios"

        Expect.stringContains
          system
          "EVIDENCE-BASED TRAINING"
          "el sistema exige evidencia"

        Expect.stringContains
          system
          "VARIANT ROTATION"
          "el sistema exige rotacion de variantes"

        Expect.stringContains
          system
          "an exercise name never repeats in the same day of the following week"
          "la rotacion es entre semanas consecutivas"

        Expect.stringContains
          system
          "Respond with the JSON only"
          "la instruccion de salida va en el sistema")

    testCase
      "el prompt del ajuste trae los conteos y el plan anterior"
      (fun () ->
        let mes1 = parsear "plan_entrenamiento_4sem.txt"

        let solicitud =
          adjustmentRequest hombre [] 4 pedido mes1.Plan [
            ("3dias", 68)
            ("5dias", 104)
          ]

        let prompt = adjustmentPrompt solicitud

        Expect.stringContains
          prompt
          "- option 3dias has exactly 68 exercises in total"
          "conteo 68"

        Expect.stringContains
          prompt
          "- option 5dias has exactly 104 exercises in total"
          "conteo 104"

        Expect.stringContains
          prompt
          "PREVIOUS PLAN"
          "la referencia del ajuste va"

        Expect.stringContains
          prompt
          "Sentadilla con barra"
          "el plan anterior viaja completo en el mismo esquema")

    testCase "la memoria confirmada entra al prompt como ajuste" (fun () ->
      let memoria =
        {
          Target = Exercise "Press de banca"
          Label = Light
          Date = DateOnly(2026, 10, 6)
        }
        |> consolidate
        |> confirm [ Load 5 ]

      let prompt = newPlanPrompt(newRequest hombre [ memoria ] 4 pedido [])

      Expect.stringContains
        prompt
        "- on Press de banca: carga 5"
        "el delta de la memoria se fija")

    testCase
      "la excepcion del entrenador exige el indicativo en notas"
      (fun () ->
        let memoria =
          memoryOfExemption {
            Target = Day "Miércoles"
            Technique = "Sentadilla con salto"
          }

        let prompt = newPlanPrompt(newRequest hombre [ memoria ] 4 pedido [])

        Expect.stringContains
          prompt
          "- on day Miércoles: variante Sentadilla con salto — trainer exception"
          "la excepcion viaja con su tecnica"

        Expect.stringContains
          prompt
          "trainer-prescribed plus the evidence-based reason why it is not generally suggested"
          "exige el indicativo en notas"

        Expect.stringContains
          system
          "TRAINER EXCEPTIONS: the trainer is the health expert"
          "el sistema cede ante la prescripcion")

    testCase
      "el prompt de consolidacion trae las marcas y el protocolo"
      (fun () ->
        let marcas = [
          {
            Target = Exercise "Press de banca"
            Label = Light
            Date = DateOnly(2026, 10, 6)
          }
          {
            Target = Day "Lunes"
            Label = NoEquipment
            Date = DateOnly(2026, 10, 7)
          }
        ]

        let prompt = consolidationPrompt marcas

        Expect.stringContains
          prompt
          "- Press de banca | leve | 2026-10-06"
          "la marca del ejercicio va"

        Expect.stringContains
          prompt
          "- day Lunes | sin-equipo | 2026-10-07"
          "la marca del dia va"

        Expect.stringContains
          consolidationSystem
          "objetivo | etiqueta | resumen | delta[;delta...]"
          "el protocolo de respuesta va en el sistema")
  ]
