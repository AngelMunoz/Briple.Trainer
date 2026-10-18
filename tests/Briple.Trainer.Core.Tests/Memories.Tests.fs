module Briple.Trainer.Core.Tests.Memories.Tests

// A "very light" mark on an exercise consolidates into a memory with a
// well-formed parametric delta, and the proposed/confirmed/discarded
// cycle stays fixed.

open System
open Expecto
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories

let private mark label target = {
  Target = target
  Label = label
  Date = DateOnly(2026, 10, 13)
}

let tests =
  testList "Memories" [
    testCase "a very light mark consolidates with a parametric delta" (fun () ->
      let memory = consolidate(mark Light (Exercise "Press de banca"))

      Expect.equal memory.Status Proposed "born proposed"

      Expect.equal
        memory.Deltas
        [ Load 5 ]
        "the automatic adjustment raises load by 5%"

      Expect.equal memory.Summary "" "the summary is not fixed in the domain"

      match memory.Origin with
      | Mark mark ->
        Expect.equal
          mark.Target
          (Exercise "Press de banca")
          "carries its origin"
      | Exemption _ -> failtest "it was born from a mark")

    testCase "a heavy mark lowers the load" (fun () ->
      let memory = consolidate(mark Heavy (Day "Lunes"))

      Expect.equal
        memory.Deltas
        [ Load -5 ]
        "the automatic adjustment lowers the load"

      Expect.equal memory.Summary "" "the summary is not fixed in the domain")

    testCase "pain and no equipment have no automatic adjustment" (fun () ->
      let withPain = consolidate(mark Pain (Exercise "Sentadilla"))
      let noEquipment = consolidate(mark NoEquipment (Exercise "Dominadas"))

      Expect.isEmpty withPain.Deltas "pain goes without automatic adjustment"

      Expect.isEmpty
        noEquipment.Deltas
        "no equipment goes without automatic adjustment")

    testCase "confirm sets the adjustments the trainer picked" (fun () ->
      let memory =
        mark Light (Exercise "Press de banca")
        |> consolidate
        |> confirm [ Rir -1; Reps 2 ]

      Expect.equal memory.Status Confirmed "stays confirmed"

      Expect.equal
        memory.Deltas
        [ Rir -1; Reps 2 ]
        "the deltas are the picked ones")

    testCase "discard takes the memory out of the cycle" (fun () ->
      let memory = mark Heavy (Exercise "Remo") |> consolidate |> discard

      Expect.equal memory.Status Discarded "stays discarded")

    testCase
      "a trainer exemption is born confirmed with the technique"
      (fun () ->
        let memory =
          memoryOfExemption {
            Target = Day "Miércoles"
            Technique = "Sentadilla con salto"
          }

        Expect.equal memory.Status Confirmed "never passes through proposed"

        Expect.equal
          memory.Deltas
          [ PatternVariant "Sentadilla con salto" ]
          "the technique is the variant"

        Expect.equal memory.Summary "" "the summary is not fixed in the domain"

        (match memory.Origin with
         | Exemption exemption ->
           Expect.equal
             exemption.Technique
             "Sentadilla con salto"
             "keeps the technique"
         | Mark _ -> failtest "it was born from an exemption"))

    testCase "the goal picks the automatic adjustment" (fun () ->
      let light = mark Light (Exercise "Caminata")

      Expect.equal
        (consolidateFor CardioVascularHealth light).Deltas
        [ Duration 5 ]
        "cardio raises the duration"

      Expect.equal
        (consolidateFor Hypertrophy light).Deltas
        [ Load 5 ]
        "hypertrophy raises the load"

      let heavy = mark Heavy (Exercise "Caminata")

      Expect.equal
        (consolidateFor WeightLoss heavy).Deltas
        [ Duration -5 ]
        "weight loss lowers the duration")
  ]
