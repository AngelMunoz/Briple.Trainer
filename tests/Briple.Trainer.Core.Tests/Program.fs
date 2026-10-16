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
      Prescription.Tests.tests
      Athletes.Tests.tests
      Memories.Tests.tests
      Parameters.Tests.tests
      Requests.Tests.tests
      Instructions.Tests.tests
      Wizard.Tests.tests
      Writer.Tests.tests
      Data.Tests.tests
      Validator.Tests.tests
      Prompts.Tests.tests
      Agent.Tests.tests
      Agent.Tests.interpretationTests
    ])
