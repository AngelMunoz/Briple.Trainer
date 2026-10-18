module Briple.Trainer.Core.Goals

open Briple.Trainer.Core.Athletes

type Structure =
  | StraightSets
  | Circuit
  | SteadyAndIntervals
  | ShortCircuit

type Rotation =
  | WeeklyVariant
  | MainLiftFixed
  | EveryTwoWeeks
  | Minimal

type GoalProfile = {
  Goal: PlanGoal
  WeeklyVolume: string
  RepRange: string
  Effort: string
  RirWeeks: int list
  CardioMinutes: int
  MaxRowsPerDay: int
  Rest: string
  Structure: Structure
  Rotation: Rotation
}

let profileOf(goal: PlanGoal) : GoalProfile =
  match goal with
  | Hypertrophy -> {
      Goal = goal
      WeeklyVolume = "10 to 20 hard sets per muscle group per week"
      RepRange = "6-15 reps on most work"
      Effort = "RIR 0-3 on rep-based rows"
      RirWeeks = [ 3; 2; 1; 0 ]
      CardioMinutes = 0
      MaxRowsPerDay = 8
      Rest = "2-3 min between straight sets"
      Structure = StraightSets
      Rotation = WeeklyVariant
    }
  | Strength -> {
      Goal = goal
      WeeklyVolume =
        "3 to 6 hard sets per main lift per week, plus 2-4 support exercises per session"
      RepRange = "3-6 reps on the main lifts, 6-12 on accessories"
      Effort = "RIR 1-3 on rep-based rows"
      RirWeeks = [ 3; 2; 1; 1 ]
      CardioMinutes = 0
      MaxRowsPerDay = 6
      Rest = "3-5 min after a main lift, 2 min on accessories"
      Structure = StraightSets
      Rotation = MainLiftFixed
    }
  | WeightLoss -> {
      Goal = goal
      WeeklyVolume = "8 to 14 hard sets per muscle group per week, in circuits"
      RepRange = "8-15 reps"
      Effort = "RIR 1-3 on rep-based rows; zone 2 or rpe on cardio rows"
      RirWeeks = [ 3; 2; 2; 1 ]
      CardioMinutes = 10
      MaxRowsPerDay = 7
      Rest = "30-60 s between exercises, 60-90 s between rounds"
      Structure = Circuit
      Rotation = EveryTwoWeeks
    }
  | CardioVascularHealth -> {
      Goal = goal
      WeeklyVolume = "3 to 4 support exercises per session; cardio is the base"
      RepRange = "8-12 reps on support work; cardio by time"
      Effort = "rpe or zone on every timed row; RIR 2-4 on support rows"
      RirWeeks = [ 3; 3; 2; 2 ]
      CardioMinutes = 20
      MaxRowsPerDay = 6
      Rest = "60-90 s on support work"
      Structure = SteadyAndIntervals
      Rotation = WeeklyVariant
    }
  | BasicBodyActivation -> {
      Goal = goal
      WeeklyVolume = "4 to 8 sets per muscle group per week; no failure work"
      RepRange = "10-15 reps; light loads"
      Effort = "RIR 3-5 on rep-based rows; rpe 3-5 on timed holds"
      RirWeeks = [ 4; 4; 3; 3 ]
      CardioMinutes = 10
      MaxRowsPerDay = 6
      Rest = "60-90 s"
      Structure = ShortCircuit
      Rotation = Minimal
    }

let rirFor (profile: GoalProfile) (weeks: int) : (int * string) list = [
  for week in 1..weeks ->
    week, string profile.RirWeeks.[min (week - 1) (profile.RirWeeks.Length - 1)]
]

let rirWeeks (profile: GoalProfile) (weeks: int) : int list = [
  for week in 1..weeks ->
    profile.RirWeeks.[min (week - 1) (profile.RirWeeks.Length - 1)]
]
