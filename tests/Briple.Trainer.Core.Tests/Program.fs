module Briple.Trainer.Core.Tests.Program

open Expecto

[<EntryPoint>]
let main args =
  runTestsWithCLIArgs
    []
    args
    (testList "Briple.Trainer.Core" [
      Parser.Tests.tests
      Projection.Tests.tests
    ])
