module Briple.Trainer.Core.Tests.TestSupport

open System
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types

let readFixture(name: string) =
  IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name)
  |> IO.File.ReadAllText

/// Parses with the display edge: PlanFailure is the fixed 'e every
/// test asserts on.
let parse(text: string) : Result<ParsedPlan, PlanFailure> = parsePlan text
