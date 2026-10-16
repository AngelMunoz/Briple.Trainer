module Briple.Trainer.Core.Tests.Parameters.Tests

// A confirmed memory reflects into the parameters of the next request,
// and the inventory declares origins for every plan field.

open System
open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Parameters

let private mark label target = {
  Target = target
  Label = label
  Date = DateOnly(2026, 10, 13)
}

let private memoryOf label target deltas =
  mark label target |> consolidate |> confirm deltas

let tests =
  testList "Parameters" [
    testCase "the inventory declares origins for every field" (fun () ->
      let allFields = [
        Title
        Weeks
        Generated
        FileOrigin
        Variants
        VariantTitle
        VariantWeeks
        Days
        DayTitle
        DayNotes
        Circuit
        Laps
        Order
        Name
        Reps
        Rir
        Rest
        Notes
        Extras
      ]

      for field in allFields do
        Expect.isNonEmpty (originsFor field) $"field %A{field} without origins")

    testCase "a confirmed memory reflects into the next request" (fun () ->
      let memory = memoryOf Light (Exercise "Press de banca") [ Load 10 ]

      Expect.equal
        (ofConfirmedMemories [ memory ])
        [
          Adjustment(
            Exercise "Press de banca",
            [ Load 10 ],
            ConfirmedMemory memory
          )
        ]
        "the confirmed delta travels as a traced adjustment")

    testCase "the traced memory maps to its origin class" (fun () ->
      let memory = memoryOf Light (Exercise "Press de banca") [ Load 10 ]

      Expect.equal
        (classOf(ConfirmedMemory memory))
        FromConfirmedMemory
        "memory"

      Expect.equal (classOf AthleteProfile) FromProfile "profile"

      Expect.equal (classOf PreviousPlan) FromPreviousPlan "previous plan"

      Expect.equal (classOf TrainerBrief) FromBrief "brief"

      Expect.equal (classOf Agent) FromAgent "agent"

      Expect.equal (classOf Derived) FromDerived "derived")

    testCase "proposed and discarded contribute no parameters" (fun () ->
      let proposal = mark Light (Exercise "Press de banca") |> consolidate
      let discarded = proposal |> discard

      Expect.isEmpty
        (ofConfirmedMemories [ proposal; discarded ])
        "nothing arrives unconfirmed")

    testCase
      "the trainer exemption contributes its target as an adjustment"
      (fun () ->
        let memory =
          memoryOfExemption {
            Target = Day "Miércoles"
            Technique = "Sentadilla con salto"
          }

        Expect.equal
          (ofConfirmedMemories [ memory ])
          [
            Adjustment(
              Day "Miércoles",
              [ PatternVariant "Sentadilla con salto" ],
              ConfirmedMemory memory
            )
          ]
          "the confirmed exemption travels as an adjustment")
  ]
