module Briple.Trainer.Core.Tests.Prescription.Tests

// The typed prescription layer: pure doses (rep range or duration) and
// effort targets; qualifiers fail by design and "-" is ValueNone.

#nowarn "3535"

open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prescription

/// The tests' edge of the dose leaf: the advisory the assertion
/// inspects.
type DoseEdge =
  | BadDose of advisory: PrescriptionAdvisory

  interface IDoseError<DoseEdge> with
    static member NotPureRange raw = BadDose(NotPureRange raw)
    static member UnknownUnit raw = BadDose(UnknownUnit raw)

let dose(text: string) : Result<Dose, DoseEdge> = tryParseDose text

let tests =
  testList "Prescription" [
    testCase "a rep range parses" (fun () ->
      Expect.equal (dose "6-8") (Ok(Reps(6, 8))) "range")

    testCase "a single number parses as a fixed rep count" (fun () ->
      Expect.equal (dose "10") (Ok(Reps(10, 10))) "single")

    testCase "a duration in seconds parses" (fun () ->
      Expect.equal (dose "30-45 s") (Ok(Timed(30, 45))) "seconds")

    testCase "a duration in minutes parses to seconds" (fun () ->
      Expect.equal (dose "20 min") (Ok(Timed(1200, 1200))) "minutes")

    testCase "seg parses as seconds" (fun () ->
      Expect.equal (dose "45 seg") (Ok(Timed(45, 45))) "seg")

    testCase "a per-round qualifier fails with the raw input" (fun () ->
      Expect.equal
        (dose "8-12 reps por ronda")
        (Error(BadDose(NotPureRange "8-12 reps por ronda")))
        "the qualifier rejection carries the raw reps")

    testCase "a per-side qualifier fails with the raw input" (fun () ->
      Expect.equal
        (dose "30-45 s por lado")
        (Error(BadDose(NotPureRange "30-45 s por lado")))
        "the qualifier rejection carries the raw reps")

    testCase "an effort range parses as Rir" (fun () ->
      Expect.equal (tryParseEffort "1-2") (ValueSome(Rir(1, 2))) "rir range")

    testCase "a single effort number parses as Rir" (fun () ->
      Expect.equal (tryParseEffort "2") (ValueSome(Rir(2, 2))) "rir")

    testCase "a dash parses as ValueNone" (fun () ->
      Expect.equal (tryParseEffort "-") ValueNone "dash")

    testCase "empty text parses as ValueNone" (fun () ->
      Expect.equal (tryParseEffort "") ValueNone "empty")

    testCase "rpe parses as Rpe" (fun () ->
      Expect.equal (tryParseEffort "rpe 6-7") (ValueSome(Rpe(6, 7))) "rpe")

    testCase "free text parses as a zone" (fun () ->
      Expect.equal (tryParseEffort "zona 2") (ValueSome(Zone "zona 2")) "zone")
  ]
