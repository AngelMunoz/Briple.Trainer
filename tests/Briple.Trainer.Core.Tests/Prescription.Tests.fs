module Briple.Trainer.Core.Tests.Prescription.Tests

// The typed prescription layer: pure doses (rep range or duration) and
// effort targets; qualifiers fail by design and "-" is ValueNone.

open Expecto
open Briple.Trainer.Core.Prescription

let tests =
  testList "Prescription" [
    testCase "a rep range parses" (fun () ->
      Expect.equal (tryParseDose "6-8") (Ok(Reps(6, 8))) "range")

    testCase "a single number parses as a fixed rep count" (fun () ->
      Expect.equal (tryParseDose "10") (Ok(Reps(10, 10))) "single")

    testCase "a duration in seconds parses" (fun () ->
      Expect.equal (tryParseDose "30-45 s") (Ok(Timed(30, 45))) "seconds")

    testCase "a duration in minutes parses to seconds" (fun () ->
      Expect.equal (tryParseDose "20 min") (Ok(Timed(1200, 1200))) "minutes")

    testCase "seg parses as seconds" (fun () ->
      Expect.equal (tryParseDose "45 seg") (Ok(Timed(45, 45))) "seg")

    testCase "a per-round qualifier fails" (fun () ->
      match tryParseDose "8-12 reps por ronda" with
      | Error _ -> ()
      | Ok dose -> failtest $"expected an error, got %A{dose}")

    testCase "a per-side qualifier fails" (fun () ->
      match tryParseDose "30-45 s por lado" with
      | Error _ -> ()
      | Ok dose -> failtest $"expected an error, got %A{dose}")

    testCase "an effort range parses as Rir" (fun () ->
      Expect.equal
        (tryParseEffort "1-2")
        (Ok(ValueSome(Rir(1, 2))))
        "rir range")

    testCase "a single effort number parses as Rir" (fun () ->
      Expect.equal (tryParseEffort "2") (Ok(ValueSome(Rir(2, 2)))) "rir")

    testCase "a dash parses as ValueNone" (fun () ->
      Expect.equal (tryParseEffort "-") (Ok ValueNone) "dash")

    testCase "empty text parses as ValueNone" (fun () ->
      Expect.equal (tryParseEffort "") (Ok ValueNone) "empty")

    testCase "rpe parses as Rpe" (fun () ->
      Expect.equal (tryParseEffort "rpe 6-7") (Ok(ValueSome(Rpe(6, 7)))) "rpe")

    testCase "free text parses as a zone" (fun () ->
      Expect.equal
        (tryParseEffort "zona 2")
        (Ok(ValueSome(Zone "zona 2")))
        "zone")
  ]
