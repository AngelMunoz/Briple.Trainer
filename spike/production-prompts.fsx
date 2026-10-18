// Final production prompts for the five goals, no model call.
// Run from the repo root: dotnet fsi spike/production-prompts.fsx

#I @"..\src\Briple.Trainer.Core\bin\Debug\net10.0"
#r "Briple.Trainer.Core.dll"
#r "JDeck.dll"
#r "XParsec.dll"
#r "FsToolkit.ErrorHandling.dll"
#r "IcedTasks.dll"

open System
open System.IO
open System.Text
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Requests

let goals = [
  Hypertrophy
  Strength
  WeightLoss
  CardioVascularHealth
  BasicBodyActivation
]

let athlete = {
  Name = "Ramiro"
  Gender = Male
  Goals = [ CardioVascularHealth; BasicBodyActivation ]
}

let nameOf goal =
  match goal with
  | Hypertrophy -> "hypertrophy"
  | Strength -> "strength"
  | WeightLoss -> "weight-loss"
  | CardioVascularHealth -> "cardiovascular-health"
  | BasicBodyActivation -> "basic-body-activation"

let dir =
  Path.Combine(__SOURCE_DIRECTORY__, "production-smoke", "prompts-final")

Directory.CreateDirectory dir |> ignore

for goal in goals do
  let brief = {
    Goal = goal
    Frequency = TresDias
    Emphasis = []
    Adjustments = []
  }

  let request = {
    Athlete = athlete
    Memories = []
    Weeks = 2
    Brief = brief
  }

  let prefix = Path.Combine(dir, nameOf goal)

  File.WriteAllText(prefix + ".system.txt", system goal, Encoding.UTF8)
  File.WriteAllText(prefix + ".user.txt", newPlanPrompt request, Encoding.UTF8)
  printfn "wrote %s" prefix

printfn "dir: %s" dir
