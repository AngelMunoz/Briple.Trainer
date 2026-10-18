module Briple.Trainer.Core.Plan.Sanitize

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.PromptText

type ContentField =
  | PlanTitle
  | PlanOrigin
  | ExerciseName of name: string
  | ExerciseNotes of name: string
  | ExtraLine of key: string

type ContentWarning = { Field: ContentField; Pattern: string }

let normalizeExercise(exercise: Exercise) : Exercise = {
  exercise with
      Circuit = line 8 exercise.Circuit
      Laps = line 40 exercise.Laps
      Name = line 120 exercise.Name
      Reps = line 40 exercise.Reps
      Rir = line 40 exercise.Rir
      Rest = line 40 exercise.Rest
      Notes = line 500 exercise.Notes
}

let normalizeDay(day: Day) : Day = {
  day with
      Id = line 40 day.Id
      Title = day.Title |> Option.map(flat 200)
      Exercises = day.Exercises |> List.map normalizeExercise
}

let normalizeWeek(week: Week) : Week = {
  week with
      Days = week.Days |> List.map normalizeDay
}

let normalizeVariant(variant: Variant) : Variant = {
  variant with
      Id = line 40 variant.Id
      Title = variant.Title |> Option.map(flat 200)
      Weeks = variant.Weeks |> List.map normalizeWeek
}

let normalizeBlock(block: Block) : Block = {
  block with
      Variants = block.Variants |> List.map normalizeVariant
}

let normalize(plan: Plan) : Plan = {
  plan with
      Title = flat 200 plan.Title
      Generated = flat 40 plan.Generated
      Origin = flat 500 plan.Origin
      Blocks = plan.Blocks |> List.map normalizeBlock
      Extras = plan.Extras |> List.map(flat 500)
}

let patterns = [
  "instructions"
  "instrucciones"
  "assistant"
  "asistente"
  "system"
  "sistema"
  "ignore"
  "ignora"
  "you are"
  "eres un"
]

let warning(field: ContentField, text: string) : ContentWarning option =
  let normalized = text.ToLowerInvariant()

  patterns
  |> List.tryFind(fun pattern -> normalized.Contains pattern)
  |> Option.map(fun pattern -> { Field = field; Pattern = pattern })

let extraKey(text: string) =
  match text.IndexOf '|' with
  | -1 -> ""
  | index -> text.Substring(0, index).Trim()

let warningsOfExercise(exercise: Exercise) : ContentWarning list =
  [
    warning(ExerciseName exercise.Name, exercise.Name)
    warning(ExerciseNotes exercise.Name, exercise.Notes)
  ]
  |> List.choose id

let contentWarnings(plan: Plan) : ContentWarning list =
  let planWarnings =
    [ warning(PlanTitle, plan.Title); warning(PlanOrigin, plan.Origin) ]
    |> List.choose id

  let exerciseWarnings =
    plan |> Tree.exercisesOf |> Seq.collect warningsOfExercise |> List.ofSeq

  let extraWarnings =
    plan.Extras
    |> List.map(fun extra -> warning(ExtraLine(extraKey extra), extra))
    |> List.choose id

  planWarnings @ exerciseWarnings @ extraWarnings |> List.distinct
