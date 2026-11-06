module Briple.Trainer.UI.Tests.Program

open Expecto

open Briple.Trainer.UI.Tests

[<EntryPoint>]
let main argv =
  let tests =
    testList "briple trainer ui" [
      Persistence.tests
      Stores.tests
      App.tests
      PlanImport.Tests.tests
      Components.tests
      Components.noteTests
      Components.reviewTests
      Components.wizardTests
      Leaks.tests
      Snapshots.captureTests
    ]

  runTestsWithCLIArgs [] argv tests
