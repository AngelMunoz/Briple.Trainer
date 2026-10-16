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
      Components.tests
      Components.noteTests
    ]

  runTestsWithCLIArgs [] argv tests
