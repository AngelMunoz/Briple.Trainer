module Briple.Trainer.Core.Tests.Datos.Tests

// La frontera de datos: el JSON del modelo se convierte al dominio o
// acumula fallos con su ruta. El wrapper {"data": "..."} se
// desenvuelve antes de convertir.

open System
open System.Text.Json
open Expecto
open Briple.Trainer.Core.Plan.Data
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Tests.TestSupport

let jsonValido =
  """
{
  "title": "Plan de prueba",
  "weeks": 2,
  "generated": "2026-10-14",
  "origin": "spike",
  "gender": "male",
  "variants": [
    {
      "id": "3dias",
      "title": "Full body",
      "weeks": [
        {
          "number": 1,
          "days": [
            {
              "id": "lunes",
              "title": "A",
              "exercises": [
                {
                  "circuit": "A",
                  "laps": "4",
                  "order": 1,
                  "name": "Sentadilla",
                  "reps": "6-8",
                  "rir": "1-2",
                  "rest": "3 min",
                  "notes": "-"
                }
              ]
            }
          ]
        }
      ]
    }
  ],
  "extras": [ "guia | calienta 10 min" ]
}
"""

let tests =
  testList "Datos" [
    testCase "el json valido se convierte al dominio" (fun () ->
      match fromJson jsonValido with
      | Ok plan ->
        Expect.equal plan.Title "Plan de prueba" "titulo"
        Expect.equal plan.Weeks 2 "semanas"
        Expect.equal plan.Blocks.Length 1 "un bloque"

        let bloque = List.head plan.Blocks

        Expect.equal bloque.Gender Male "genero convertido"

        let opcion = List.head bloque.Variants

        Expect.equal opcion.Id "3dias" "opcion"

        let semana = List.head opcion.Weeks
        let dia = List.head semana.Days

        Expect.equal dia.Id "lunes" "dia en minusculas"

        let ejercicio = List.head dia.Exercises

        Expect.equal ejercicio.Name "Sentadilla" "ejercicio"
        Expect.equal ejercicio.Order 1 "orden entero"

        Expect.equal plan.Extras [ "guia | calienta 10 min" ] "extras"
      | Error fallos -> failtest $"se esperaba Ok: %A{fallos}")

    testCase "un json envuelto en data se desenvuelve" (fun () ->
      let envuelto = $"{{ \"data\": {JsonSerializer.Serialize jsonValido} }}"

      match fromJson envuelto with
      | Ok plan -> Expect.equal plan.Title "Plan de prueba" "mismo plan"
      | Error fallos -> failtest $"se esperaba Ok: %A{fallos}")

    testCase "un genero desconocido acumula fallo" (fun () ->
      let json = jsonValido.Replace("\"male\"", "\"alien\"")

      match fromJson json with
      | Ok _ -> failtest "se esperaba Error"
      | Error fallos ->
        Expect.exists
          fallos
          (fun fallo -> fallo.Contains "gender")
          "el fallo nombra el genero")

    testCase "un dia no reconocido acumula fallo con su ruta" (fun () ->
      let json = jsonValido.Replace("\"lunes\"", "\"samedi\"")

      match fromJson json with
      | Ok _ -> failtest "se esperaba Error"
      | Error fallos ->
        Expect.exists
          fallos
          (fun fallo -> fallo.Contains "days[0]" && fallo.Contains "samedi")
          "el fallo trae la ruta")

    testCase "un ejercicio sin nombre rechaza con fallo y ruta" (fun () ->
      let json =
        jsonValido.Replace("\"name\": \"Sentadilla\"", "\"name\": \" \"")

      match fromJson json with
      | Ok _ -> failtest "se esperaba Error"
      | Error fallos ->
        Expect.exists
          fallos
          (fun fallo ->
            fallo.Contains "exercises[0]" && fallo.Contains "empty name")
          "el fallo trae la ruta")

    testCase "un json roto da fallo ilegible" (fun () ->
      match fromJson "{ esto no es json" with
      | Ok _ -> failtest "se esperaba Error"
      | Error fallos ->
        Expect.exists
          fallos
          (fun fallo -> fallo.Contains "unreadable")
          "fallos ilegibles")

    testCase
      "toJson y fromJson round-trip del plan de un solo bloque"
      (fun () ->
        match readFixture "plan_entrenamiento_4sem.txt" |> parsePlan with
        | Error error -> failtest $"parse error: {error.Message}"
        | Ok parsed ->
          let soloHombre = {
            parsed.Plan with
                Blocks =
                  parsed.Plan.Blocks
                  |> List.filter(fun bloque -> bloque.Gender = Male)
          }

          match toJson soloHombre |> fromJson with
          | Ok convertido ->
            Expect.equal
              convertido
              soloHombre
              "el plan sobrevive el round-trip"
          | Error fallos -> failtest $"se esperaba Ok: %A{fallos}")
  ]
