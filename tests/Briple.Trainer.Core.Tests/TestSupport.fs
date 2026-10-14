module Briple.Trainer.Core.Tests.TestSupport

open System

let readFixture(name: string) =
  IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name)
  |> IO.File.ReadAllText
