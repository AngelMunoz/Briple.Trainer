module Briple.Trainer.Core.Tests.Parser.Tests

// Expecto port of the browser-lane parser tests. The positive lane runs
// against the real 4-week plan files (month 1, month 2); negative fixtures
// are inline strings with known line numbers.

open Expecto
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Tests.TestSupport

let parse(text: string) = parsePlan text

let hasWarning (kind: WarningKind) (line: int) (warnings: ParseWarning list) =
  warnings
  |> List.exists(fun warning -> warning.Kind = kind && warning.Line = line)

let block (plan: Plan) (gender: Gender) =
  plan.Blocks |> List.find(fun b -> b.Gender = gender)

let variant (block: Block) (id: string) =
  block.Variants |> List.find(fun o -> o.Id = id)

let totalExercises(plan: Plan) =
  plan.Blocks
  |> List.sumBy(fun block ->
    block.Variants
    |> List.sumBy(fun variant ->
      variant.Weeks
      |> List.sumBy(fun week ->
        week.Days |> List.sumBy(fun d -> d.Exercises.Length))))

let assertSamplePlan(text: string) =
  match parse text with
  | Error error ->
    failtest $"unexpected parse error at line {error.Line}: {error.Message}"
  | Ok parsed ->
    let plan = parsed.Plan

    Expect.equal
      parsed.Warnings
      []
      $"expected no warnings, got %A{parsed.Warnings}"

    Expect.equal plan.Version 1 "version"

    Expect.equal
      plan.Title
      "Plan de Entrenamiento en Circuito - 4 Semanas"
      "title"

    Expect.equal plan.Weeks 4 "weeks"
    Expect.equal plan.Generated "2026-09-19" "generated"

    Expect.equal
      plan.Origin
      "Plan_Entrenamiento_Circuito_4_Semanas (2).xlsx"
      "origin"

    Expect.equal plan.Blocks.Length 2 "genders in file"

    let man = block plan Male
    let woman = block plan Female

    Expect.equal
      (man.Variants |> List.map(fun o -> o.Id))
      [ "3dias"; "5dias" ]
      "man variants"

    Expect.equal
      (woman.Variants |> List.map(fun o -> o.Id))
      [ "5dias" ]
      "woman variants"

    let threeDays = variant man "3dias"

    Expect.equal
      threeDays.Title
      (Some "FULL BODY A / B / C (LUNES · MIÉRCOLES · VIERNES)")
      "variant title"

    Expect.equal
      (threeDays.Weeks |> List.map(fun s -> s.Number))
      [ 1; 2; 3; 4 ]
      "3dias weeks"

    let week1 = List.head threeDays.Weeks

    Expect.equal
      (week1.Days |> List.map(fun d -> d.Id))
      [ "Lunes"; "Miércoles"; "Viernes" ]
      "3dias days in week 1"

    let monday = List.head week1.Days
    Expect.equal monday.Title (Some "Full Body A") "day title"
    Expect.equal monday.Exercises.Length 6 "monday exercise count"

    let first = List.head monday.Exercises
    Expect.equal first.Circuit "A" "circuit"
    Expect.equal first.Laps "4" "laps (verbatim string)"
    Expect.equal first.Order 1 "order"
    Expect.equal first.Name "Sentadilla con barra" "name"
    Expect.equal first.Reps "6-8" "reps verbatim"
    Expect.equal first.Rir "1-2" "rir verbatim"
    Expect.equal first.Rest "3 min" "rest verbatim"
    Expect.equal first.Notes "-" "notes verbatim"

    let friday = week1.Days |> List.find(fun d -> d.Id = "Viernes")

    let plank =
      friday.Exercises
      |> List.find(fun e -> e.Name = "Plancha / rueda abdominal")

    Expect.equal plank.Reps "30-45 s" "time-based reps verbatim"
    Expect.equal plank.Rir "-" "absent rir stays '-'"

    let fiveDays = variant man "5dias"

    Expect.isTrue
      (fiveDays.Weeks |> List.forall(fun s -> s.Days.Length = 5))
      "5dias trains five days per week"

    let womanFirst = List.head woman.Variants

    Expect.isTrue
      (womanFirst.Weeks |> List.forall(fun s -> s.Days.Length = 5))
      "woman plan trains five days per week"

    Expect.equal plan.Extras.Length 32 "extras count"

    Expect.isTrue
      (plan.Extras.[0].StartsWith "decision | ")
      "first extra keeps its key prefix"

    Expect.equal
      (plan.Extras
       |> List.filter(fun extra -> extra.StartsWith "mesociclo")
       |> List.length)
      5
      "mesociclo entries"

    Expect.equal (totalExercises plan) 252 "total #ej rows in the model"

let tests =
  testList "Plan parser" [
    testCase "the sample plan parses with zero warnings" (fun () ->
      assertSamplePlan(readFixture "training_plan_4w.txt"))

    testCase "the month 2 sample plan parses with zero warnings" (fun () ->
      match readFixture "month2_4w.txt" |> parse with
      | Error error ->
        failtest
          $"unexpected parse error at line {error.Line}: {error.Message}"
      | Ok parsed ->
        let plan = parsed.Plan

        Expect.equal
          parsed.Warnings
          []
          $"expected no warnings, got %A{parsed.Warnings}"

        Expect.equal plan.Version 1 "version"

        Expect.equal
          plan.Title
          "Plan de Entrenamiento - Mes 2 en Circuito (4 Semanas)"
          "title"

        Expect.equal plan.Weeks 4 "weeks"
        Expect.equal plan.Generated "2026-10-05" "generated"
        Expect.equal plan.Blocks.Length 2 "genders in file"

        let man = block plan Male
        let woman = block plan Female

        Expect.equal
          (man.Variants |> List.map(fun o -> o.Id))
          [ "3dias"; "5dias" ]
          "man variants"

        Expect.equal
          (woman.Variants |> List.map(fun o -> o.Id))
          [ "5dias" ]
          "woman variants"

        Expect.equal (totalExercises plan) 308 "total #ej rows in the model"
        Expect.equal plan.Extras.Length 19 "extras count")

    testCase "a mismatched #end is a hard error" (fun () ->
      let fixture =
        "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start genero Hombre\n#end opcion\n#end plan\n"

      match parse fixture with
      | Error error ->
        Expect.equal error.Line 5 "error points at the mismatched line"
      | Ok _ -> failtest "expected a parse error")

    testCase "an unterminated block is a hard error" (fun () ->
      match
        parse
          "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start genero Hombre\n"
      with
      | Error error ->
        Expect.isTrue
          (error.Message.StartsWith "unterminated")
          "reports the unterminated block"
      | Ok _ -> failtest "expected a parse error")

    testCase "a malformed #ej row warns instead of failing" (fun () ->
      let fixture =
        "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start genero Hombre\n#start opcion 3dias\n#start semana 1\n#start dia Lunes\n#ej A|4|Sentadilla|6-8|1-2|3 min\n#end dia\n#end semana\n#end opcion\n#end genero\n#end plan\n"

      match parse fixture with
      | Ok parsed ->
        Expect.isTrue
          (hasWarning BadEjField 8 parsed.Warnings)
          "bad row reported at its line"

        Expect.isTrue
          (hasWarning EmptyDay 9 parsed.Warnings)
          "the day ends up empty"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "an #ej row without a name warns" (fun () ->
      let fixture =
        "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start genero Hombre\n#start opcion 3dias\n#start semana 1\n#start dia Lunes\n#ej A|4|1||6-8|1-2|3 min|-\n#end dia\n#end semana\n#end opcion\n#end genero\n#end plan\n"

      match parse fixture with
      | Ok parsed ->
        Expect.isTrue
          (hasWarning BadEjField 8 parsed.Warnings)
          "empty name reported at its line"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "an #ej row with a pipe inside the name warns" (fun () ->
      let fixture =
        "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start genero Hombre\n#start opcion 3dias\n#start semana 1\n#start dia Lunes\n#ej A|4|1|Na|me|6-8|1-2|3 min|-\n#end dia\n#end semana\n#end opcion\n#end genero\n#end plan\n"

      match parse fixture with
      | Ok parsed ->
        Expect.isTrue
          (hasWarning BadEjField 8 parsed.Warnings)
          "pipe in name reported at its line"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "an unrecognized day name warns" (fun () ->
      let fixture =
        "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start genero Hombre\n#start opcion 3dias\n#start semana 1\n#start dia Samedi\n#ej A|4|1|X|6-8|1-2|3 min|-\n#end dia\n#end semana\n#end opcion\n#end genero\n#end plan\n"

      match parse fixture with
      | Ok parsed ->
        Expect.isTrue
          (hasWarning UnknownDayName 7 parsed.Warnings)
          "unmapped day name reported"

        Expect.equal
          parsed.Plan.Blocks.[0].Variants.[0].Weeks.[0].Days.Length
          1
          "day kept in the model"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "an unknown block is skipped in a balanced way" (fun () ->
      let fixture =
        "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start semanax 1\n#start dia Lunes\n#end dia\n#end semanax\n#end plan\n"

      match parse fixture with
      | Ok parsed ->
        Expect.equal parsed.Warnings.Length 1 "exactly one warning"

        Expect.isTrue
          (hasWarning UnknownBlockSkipped 4 parsed.Warnings)
          "skip warning at line 4"

        Expect.equal
          parsed.Plan.Blocks.Length
          0
          "no gender parsed from the unknown block"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "a duplicate week warns" (fun () ->
      let fixture =
        "#start plan v1\n#meta titulo T\n#meta semanas 1\n#start genero Hombre\n#start opcion 3dias\n#start semana 1\n#end semana\n#start semana 1\n#end semana\n#end opcion\n#end genero\n#end plan\n"

      match parse fixture with
      | Ok parsed ->
        Expect.isTrue
          (hasWarning DuplicateEntry 8 parsed.Warnings)
          "duplicate reported at second start"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "an unsupported plan version warns but parses" (fun () ->
      match
        parse "#start plan v2\n#meta titulo T\n#meta semanas 1\n#end plan\n"
      with
      | Ok parsed ->
        Expect.isTrue
          (hasWarning UnsupportedVersion 1 parsed.Warnings)
          "version warning at line 1"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "missing required meta warns at #end plan" (fun () ->
      match parse "#start plan v1\n#end plan\n" with
      | Ok parsed ->
        Expect.equal
          (parsed.Warnings
           |> List.filter(fun w -> w.Kind = MissingMeta)
           |> List.length)
          2
          "title and weeks both reported"

        Expect.equal parsed.Plan.Title "" "title defaulted"
        Expect.equal parsed.Plan.Weeks 0 "weeks defaulted"
      | Error error ->
        failtest $"expected warnings, got error: {error.Message}")

    testCase "the smallest valid plan parses cleanly" (fun () ->
      match
        parse "#start plan v1\n#meta titulo T\n#meta semanas 1\n#end plan\n"
      with
      | Ok parsed ->
        Expect.equal parsed.Warnings [] $"no warnings, got %A{parsed.Warnings}"
        Expect.equal parsed.Plan.Blocks.Length 0 "no blocks"
      | Error error ->
        failtest $"expected a clean parse, got error: {error.Message}")
  ]
