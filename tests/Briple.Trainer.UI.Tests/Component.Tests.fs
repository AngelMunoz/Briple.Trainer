module Briple.Trainer.UI.Tests.Components

// The wizard, review and note components through their public surface:
// props in, events out, driven with synthetic clicks and property
// writes. The note flow runs against a fake agent and a real store over
// a throwaway database.

open System
open System.Globalization
open System.IO
open System.Reactive.Subjects
open System.Threading.Tasks

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Headless
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Media
open Avalonia.VisualTree
open NXUI.Extensions

open Expecto

open Briple.Trainer.Components.Fields
open Briple.Trainer.Strings

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.UI.Tests.Fixtures
open Briple.Trainer.UI.Tests.Harness

let private memoryReviewEnv
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : Briple.Trainer.Components.MemoryReview.MemoryReviewEnv = {
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

let private planReviewEnv
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : Briple.Trainer.Components.PlanReview.PlanReviewEnv = {
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

let private scrollerIn(root: Control) : ScrollViewer =
  root.GetVisualDescendants()
  |> Seq.pick(fun visual ->
    match visual with
    | :? ScrollViewer as scroller -> Some scroller
    | _ -> None)

let private textBoxes(root: Control) : TextBox list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? TextBox as textBox -> Some textBox
    | _ -> None)
  |> List.ofSeq

let private clickText(root: Control, text: string) : unit =
  buttonWithText(root, text) |> Option.iter click

/// Finds a radio button by its visible label.
let private radioWithText(root: Control, text: string) : RadioButton option =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? RadioButton as radio when string radio.Content = text -> Some radio
    | _ -> None)
  |> Seq.tryHead



let tests =
  testList "components" [
    uiTest "memory review confirms with the accepted deltas"
    <| fun () -> task {
      let events = ResizeArray<Briple.Trainer.Components.MemoryReview.Event>()
      let _, culture = liveCulture es

      let memory =
        consolidate {
          Target = Exercise "Press de banca"
          Label = Light
          Date = DateOnly(2026, 10, 6)
        }

      let! card =
        probeUi(fun () ->
          Briple.Trainer.Components.MemoryReview.MemoryReview(
            memoryReviewEnv culture,
            { Id = Guid.NewGuid(); Memory = memory },
            events.Add
          ))

      do! mount card

      do! runUi(fun () -> clickText(card, get(es, Keys.memoriesConfirm)))

      let! reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq events),
          (fun current ->
            current
            |> List.exists(function
              | Briple.Trainer.Components.MemoryReview.Event.Confirmed _ ->
                true
              | Briple.Trainer.Components.MemoryReview.Event.Discarded _ ->
                false))
        )

      Expect.isTrue reported "the confirm event reached the parent"

      match List.ofSeq events with
      | Briple.Trainer.Components.MemoryReview.Event.Confirmed(_, confirmed) :: _ ->
        Expect.equal confirmed.Deltas memory.Deltas "the proposal deltas travel"
      | _ -> failtest "no confirmed memory"
    }

    uiTest "memory review adds a delta and keeps the chosen scope"
    <| fun () -> task {
      let events = ResizeArray<Briple.Trainer.Components.MemoryReview.Event>()
      let _, culture = liveCulture es

      let memory =
        consolidate {
          Target = Exercise "Press de banca"
          Label = Pain
          Date = DateOnly(2026, 10, 6)
        }

      let! card =
        probeUi(fun () ->
          Briple.Trainer.Components.MemoryReview.MemoryReview(
            memoryReviewEnv culture,
            { Id = Guid.NewGuid(); Memory = memory },
            events.Add
          ))

      do! mount card

      let! athleteScope =
        probeUi(fun () ->
          card.GetVisualDescendants()
          |> Seq.choose(function
            | :? RadioButton as radio when
              hasText(radio, get(es, Keys.memoryScopeAthlete))
              ->
              Some radio
            | _ -> None)
          |> Seq.tryHead)

      let athleteScope =
        athleteScope
        |> Option.defaultWith(fun () ->
          failtest "the athlete scope did not render")

      do! runUi(fun () -> click athleteScope)

      let! value =
        probeUi(fun () ->
          textBoxes card |> List.tryFind(fun box -> box.Width = 140.0))

      let value =
        value
        |> Option.defaultWith(fun () ->
          failtest "the adjustment value did not render")

      do! runUi(fun () -> value.Text <- "2")

      let! addButton =
        probeUi(fun () ->
          buttonWithText(card, get(es, Keys.memoryAddAdjustment)))

      let addButton =
        addButton
        |> Option.defaultWith(fun () ->
          failtest "the add adjustment button did not render")

      let! canAdd = waitFor(2000, (fun () -> addButton.IsEnabled), id)
      Expect.isTrue canAdd "the adjustment value reached the model"

      do! runUi(fun () -> click addButton)

      let! added =
        waitFor(2000, (fun () -> card), fun root -> hasText(root, "RIR +2"))

      Expect.isTrue added "the added delta rendered"

      do! runUi(fun () -> clickText(card, get(es, Keys.memoriesConfirm)))

      let! reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq events),
          fun current ->
            current
            |> List.exists(function
              | Briple.Trainer.Components.MemoryReview.Event.Confirmed _ -> true
              | Briple.Trainer.Components.MemoryReview.Event.Discarded _ ->
                false)
        )

      Expect.isTrue reported "the confirm event reached the parent"

      match List.ofSeq events with
      | Briple.Trainer.Components.MemoryReview.Event.Confirmed(_, confirmed) :: _ ->
        Expect.contains confirmed.Deltas (Rir 2) "the added delta travels"

        Expect.equal
          confirmed.Scope
          MemoryScope.Athlete
          "the chosen scope travels"
      | _ -> failtest "no confirmed memory"
    }

    uiTest "memory review discards"
    <| fun () -> task {
      let events = ResizeArray<Briple.Trainer.Components.MemoryReview.Event>()
      let _, culture = liveCulture es

      let memory =
        consolidate {
          Target = Exercise "Press de banca"
          Label = Light
          Date = DateOnly(2026, 10, 6)
        }

      let id = Guid.NewGuid()

      let! card =
        probeUi(fun () ->
          Briple.Trainer.Components.MemoryReview.MemoryReview(
            memoryReviewEnv culture,
            { Id = id; Memory = memory },
            events.Add
          ))

      do! mount card

      do! runUi(fun () -> clickText(card, get(es, Keys.memoriesDiscard)))

      let! reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq events),
          (fun current ->
            List.contains
              (Briple.Trainer.Components.MemoryReview.Event.Discarded id)
              current)
        )

      Expect.isTrue reported "the discard event reached the parent"
    }

    uiTest "the added rows rebuild for a delta, not for another field"
    <| fun () -> task {
      let _, culture = liveCulture es
      let id = Guid.NewGuid()

      let memory =
        consolidate {
          Target = Exercise "Press de banca"
          Label = Light
          Date = DateOnly(2026, 10, 6)
        }

      let! card =
        probeUi(fun () ->
          Briple.Trainer.Components.MemoryReview.MemoryReview(
            memoryReviewEnv culture,
            { Id = id; Memory = memory },
            ignore
          ))

      do! mount card

      let addedHost() =
        probeUi(fun () ->
          controlByName(card, $"memory-added-{id}")
          |> Option.map(fun control -> control :?> ContentControl))

      let! value =
        probeUi(fun () ->
          textBoxes card |> List.tryFind(fun box -> box.Width = 140.0))

      let value =
        value
        |> Option.defaultWith(fun () ->
          failtest "the adjustment value did not render")

      let! addButton =
        probeUi(fun () ->
          buttonWithText(card, get(es, Keys.memoryAddAdjustment)))

      let addButton =
        addButton
        |> Option.defaultWith(fun () ->
          failtest "the add adjustment button did not render")

      let! hostOption = addedHost()

      let host =
        hostOption
        |> Option.defaultWith(fun () ->
          failtest "the added rows host did not render")

      let emptyContent = host.Content

      do! runUi(fun () -> value.Text <- "2")

      let! canAdd =
        waitFor(2000, (fun () -> addButton.IsEnabled), fun value -> value)

      Expect.isTrue canAdd "the value reached the model"

      do! runUi(fun () -> click addButton)

      let! added =
        waitFor(
          2000,
          (fun () -> host.Content),
          fun content -> not(obj.ReferenceEquals(content, emptyContent))
        )

      Expect.isTrue added "the added delta rebuilt the rows"

      let addedContent = host.Content

      let! kindPicker =
        probeUi(fun () ->
          card.GetVisualDescendants()
          |> Seq.choose(function
            | :? ComboBox as picker -> Some picker
            | _ -> None)
          |> Seq.tryHead)

      let kindPicker =
        kindPicker
        |> Option.defaultWith(fun () ->
          failtest "the kind picker did not render")

      do! runUi(fun () -> kindPicker.SelectedIndex <- 1)

      let! cleared = waitFor(2000, (fun () -> value.Text), String.IsNullOrEmpty)

      Expect.isTrue cleared "the kind change reached the model"

      let! unchanged =
        waitFor(
          500,
          (fun () -> obj.ReferenceEquals(host.Content, addedContent)),
          fun value -> value
        )

      Expect.isTrue unchanged "another field does not rebuild the added rows"
    }

    uiTest "the plan reads first and edits by row"
    <| fun () -> task {
      match Briple.Trainer.Core.Plan.Parser.Parse.parse sampleRaw with
      | Error e -> failtest $"the sample plan does not parse: {e}"
      | Ok parsed ->
        let events = ResizeArray<Briple.Trainer.Components.PlanReview.Event>()
        let _, culture = liveCulture es

        let! review =
          probeUi(fun () ->
            Briple.Trainer.Components.PlanReview.PlanReview(
              planReviewEnv culture,
              {
                Parsed = parsed
                Week = ValueNone
                Editing = false
              },
              events.Add
            ))

        do! mount review

        Expect.isEmpty (List.ofSeq events) "an untouched plan reports no change"

        // reading first: the exercise renders as prose lines, no entries
        let! readable =
          waitFor(
            2000,
            (fun () -> review),
            fun root ->
              hasText(root, "A1  Press de banca")
              && hasText(root, "8-10 · 2 · 120s")
          )

        Expect.isTrue readable "the exercise lines did not render"

        let! circuitHeading =
          probeUi(fun () -> hasText(review, "Circuito A · 3 vueltas"))

        Expect.isTrue circuitHeading "the circuit heading did not render"

        let! dayBadge =
          probeUi(fun () -> hasText(review, "1 ejercicio · 1 circuito"))

        Expect.isTrue dayBadge "the day badge did not render"

        let! weekBadge = probeUi(fun () -> hasText(review, "Semana 1 de 1"))

        Expect.isTrue weekBadge "the week badge did not render"

        let! boxes = probeUi(fun () -> textBoxes review)
        Expect.isEmpty boxes "no entries before the explicit edit"

        // the explicit edit opens the row's entries
        let! editButton =
          probeUi(fun () -> buttonWithText(review, get(es, Keys.planEdit)))

        let editButton =
          editButton
          |> Option.defaultWith(fun () ->
            failtest "the edit button did not render")

        do! runUi(fun () -> click editButton)

        let! editing =
          waitFor(
            2000,
            (fun () -> buttonWithText(review, get(es, Keys.planDone))),
            Option.isSome
          )

        Expect.isTrue editing "the edit mode opened"

        let! rowEditButton =
          probeUi(fun () -> buttonWithText(review, get(es, Keys.planEdit)))

        let rowEditButton =
          rowEditButton
          |> Option.defaultWith(fun () ->
            failtest "the row edit button did not render")

        do! runUi(fun () -> click rowEditButton)

        let! opened =
          waitFor(
            2000,
            (fun () -> textBoxes review),
            fun entries ->
              entries |> List.exists(fun entry -> entry.Width = 48.0)
          )

        Expect.isTrue opened "the row entries did not open"

        let! repsEntries =
          probeUi(fun () ->
            textBoxes review
            |> List.filter(fun textBox -> textBox.Width = 48.0))

        match repsEntries with
        | [ lo; hi ] ->
          do!
            runUi(fun () ->
              lo.Text <- "6"
              hi.Text <- "8")
        | _ -> failtest "the two rep fields did not open"

        let! changed =
          waitFor(
            2000,
            (fun () ->
              events
              |> Seq.choose(function
                | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
                  Some plan
                | _ -> None)
              |> Seq.tryLast),
            Option.isSome
          )

        Expect.isTrue changed "the rep edit reported a plan"

        let lastPlan =
          events
          |> Seq.choose(function
            | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
              Some plan
            | Briple.Trainer.Components.PlanReview.Event.ViewChanged _ -> None)
          |> Seq.last

        let repsOf(plan: Plan) =
          plan.Blocks
          |> List.collect _.Variants
          |> List.collect _.Weeks
          |> List.collect _.Days
          |> List.collect _.Exercises
          |> List.find(fun exercise -> exercise.Name = "Press de banca")
          |> fun exercise -> exercise.Reps

        Expect.equal (repsOf lastPlan) "6-8" "the edit lands in the tree"

        // the revert restores the last snapshot and closes the row
        let! revertButton =
          probeUi(fun () -> buttonWithText(review, get(es, Keys.planRevert)))

        let revertButton =
          revertButton
          |> Option.defaultWith(fun () ->
            failtest "the revert button did not render")

        do! runUi(fun () -> click revertButton)

        let! reverted =
          waitFor(2000, (fun () -> textBoxes review), List.isEmpty)

        Expect.isTrue reverted "the row closed after the revert"

        let restored =
          events
          |> Seq.choose(function
            | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
              Some plan
            | Briple.Trainer.Components.PlanReview.Event.ViewChanged _ -> None)
          |> Seq.last

        Expect.equal (repsOf restored) "8-10" "the snapshot comes back"

        let! boxesAfter = probeUi(fun () -> textBoxes review)
        Expect.isEmpty boxesAfter "the row closes after the revert"
    }

    uiTest "the row key follows the position, not the file order"
    <| fun () -> task {
      let plan: Plan = {
        Version = 1
        Title = "dos circuitos"
        Weeks = 1
        Generated = ""
        Origin = "test"
        Blocks = [
          {
            Gender = Male
            Variants = [
              {
                Id = "3dias"
                Title = None
                Weeks = [
                  {
                    Number = 1
                    Days = [
                      {
                        Id = "Lunes"
                        Title = None
                        Notes = []
                        Exercises = [
                          {
                            Circuit = "A"
                            Laps = "2"
                            Order = 1
                            Name = "Press de banca"
                            Reps = "8-10"
                            Rir = "2"
                            Rest = "2 min"
                            Notes = "-"
                          }
                          {
                            Circuit = "A"
                            Laps = "2"
                            Order = 2
                            Name = "Remo con barra"
                            Reps = "8-10"
                            Rir = "2"
                            Rest = "2 min"
                            Notes = "-"
                          }
                          {
                            Circuit = "B"
                            Laps = "2"
                            Order = 1
                            Name = "Plancha frontal"
                            Reps = "30-45 s"
                            Rir = "rpe 6-7"
                            Rest = "1 min"
                            Notes = "-"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            ]
          }
        ]
        Extras = [
          "descansos | 60-90 s"
          "regla-circuito | vueltas iguales"
          "progresion | sube 2.5 kg"
          "mesociclo | Semana 1: objetivo | RIR 3"
        ]
      }

      let _, culture = liveCulture es

      let! review =
        probeUi(fun () ->
          Briple.Trainer.Components.PlanReview.PlanReview(
            planReviewEnv culture,
            {
              Parsed = { Plan = plan; Warnings = [] }
              Week = ValueNone
              Editing = false
            },
            ignore
          ))

      do! mount review

      let! readable =
        waitFor(
          2000,
          (fun () -> review),
          fun root -> hasText(root, "B1  Plancha frontal")
        )

      Expect.isTrue
        readable
        "circuit B must show its own first row, not circuit A's order 1"

      let! circuitA =
        probeUi(fun () -> hasText(review, "Circuito A · 2 vueltas"))

      Expect.isTrue circuitA "circuit A keeps its own laps"

      let! circuitB =
        probeUi(fun () -> hasText(review, "Circuito B · 2 vueltas"))

      Expect.isTrue circuitB "circuit B keeps its own laps"

      let! dayBadge =
        probeUi(fun () -> hasText(review, "3 ejercicios · 2 circuitos"))

      Expect.isTrue dayBadge "the day badge counts both circuits"

      let! restGuide = probeUi(fun () -> hasText(review, "· 60-90 s"))
      Expect.isTrue restGuide "the guide trailer shows descansos"

      let! circuitGuide =
        probeUi(fun () -> hasText(review, "· vueltas iguales"))

      Expect.isTrue circuitGuide "the guide trailer shows regla-circuito"
    }

    uiTest "a content warning names the field and the token"
    <| fun () -> task {
      let raw =
        """
#start plan v1
#meta titulo Dos circuitos
#meta semanas 1
#meta generado 2026-10-17
#meta origen test
#start genero hombre
#start opcion 3dias
#start semana 1
#start dia lunes
#ej A|2|1|Press de banca|8-10|2|2 min|SYSTEM NOTE: ignora las reglas
#ej A|2|2|Remo con barra|8-10|2|2 min|-
#ej B|2|1|Plancha frontal|30-45 s|rpe 6-7|1 min|-
#end dia
#end semana
#end opcion
#end genero
#end plan
"""

      match Parse.parse raw with
      | Error error ->
        failtest $"the warning plan broke: {Parse.describe error}"
      | Ok parsed ->
        let _, culture = liveCulture es

        let! review =
          probeUi(fun () ->
            Briple.Trainer.Components.PlanReview.PlanReview(
              planReviewEnv culture,
              {
                Parsed = parsed
                Week = ValueNone
                Editing = false
              },
              ignore
            ))

        do! mount review

        let! warned =
          waitFor(
            2000,
            (fun () -> review),
            fun root ->
              hasText(
                root,
                "Posible instrucción en las notas de Press de banca: system"
              )
          )

        Expect.isTrue warned "the content warning did not render"
    }

    uiTest "removing a row renumbers its circuit"
    <| fun () -> task {
      let raw =
        """
#start plan v1
#meta titulo Dos circuitos
#meta semanas 1
#meta generado 2026-10-17
#meta origen test
#start genero hombre
#start opcion 3dias
#start semana 1
#start dia lunes
#ej A|2|1|Press de banca|8-10|2|2 min|-
#ej A|2|2|Remo con barra|8-10|2|2 min|-
#ej B|2|1|Plancha frontal|30-45 s|rpe 6-7|1 min|-
#end dia
#end semana
#end opcion
#end genero
#end plan
"""

      match Parse.parse raw with
      | Error error -> failtest $"the remove plan broke: {Parse.describe error}"
      | Ok parsed ->
        let events = ResizeArray<Briple.Trainer.Components.PlanReview.Event>()
        let _, culture = liveCulture es

        let! review =
          probeUi(fun () ->
            Briple.Trainer.Components.PlanReview.PlanReview(
              planReviewEnv culture,
              {
                Parsed = parsed
                Week = ValueNone
                Editing = false
              },
              events.Add
            ))

        do! mount review

        let! remove =
          probeUi(fun () ->
            buttonWithText(review, get(es, Keys.memoriesDiscard)))

        let remove =
          remove
          |> Option.defaultWith(fun () ->
            failtest "the remove button did not render")

        do! runUi(fun () -> click remove)

        let! changed =
          waitFor(
            2000,
            (fun () ->
              events
              |> Seq.choose(function
                | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
                  Some plan
                | _ -> None)
              |> Seq.tryLast),
            Option.isSome
          )

        Expect.isTrue changed "the removal reported a plan"

        match
          events
          |> Seq.choose(function
            | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
              Some plan
            | _ -> None)
          |> Seq.tryLast
        with
        | Some plan ->
          let day = plan.Blocks.[0].Variants.[0].Weeks.[0].Days.[0]

          Expect.equal
            (day.Exercises |> List.map _.Name)
            [ "Remo con barra"; "Plancha frontal" ]
            "the row left the day"

          Expect.equal
            (day.Exercises |> List.map _.Order)
            [ 1; 1 ]
            "each circuit renumbers from one"

          Expect.equal
            (day.Exercises |> List.map _.Laps)
            [ "2"; "2" ]
            "the laps do not change"
        | None -> failtest "no plan event"
    }

    uiTest "the circuit buttons add one lap only to that circuit"
    <| fun () -> task {
      let raw =
        """
#start plan v1
#meta titulo Dos circuitos
#meta semanas 1
#meta generado 2026-10-17
#meta origen test
#start genero hombre
#start opcion 3dias
#start semana 1
#start dia lunes
#ej A|2|1|Press de banca|8-10|2|2 min|-
#ej A|2|2|Remo con barra|8-10|2|2 min|-
#ej B|2|1|Plancha frontal|30-45 s|rpe 6-7|1 min|-
#end dia
#end semana
#end opcion
#end genero
#end plan
"""

      match Parse.parse raw with
      | Error error -> failtest $"the laps plan broke: {Parse.describe error}"
      | Ok parsed ->
        let events = ResizeArray<Briple.Trainer.Components.PlanReview.Event>()
        let _, culture = liveCulture es

        let! review =
          probeUi(fun () ->
            Briple.Trainer.Components.PlanReview.PlanReview(
              planReviewEnv culture,
              {
                Parsed = parsed
                Week = ValueNone
                Editing = false
              },
              events.Add
            ))

        do! mount review

        let! plus = probeUi(fun () -> buttonWithText(review, "+"))

        let plus =
          plus
          |> Option.defaultWith(fun () ->
            failtest "the lap button did not render")

        do! runUi(fun () -> click plus)

        let! changed =
          waitFor(
            2000,
            (fun () ->
              events
              |> Seq.choose(function
                | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
                  Some plan
                | _ -> None)
              |> Seq.tryLast),
            Option.isSome
          )

        Expect.isTrue changed "the lap change reported a plan"

        match
          events
          |> Seq.choose(function
            | Briple.Trainer.Components.PlanReview.Event.PlanChanged plan ->
              Some plan
            | _ -> None)
          |> Seq.tryLast
        with
        | Some plan ->
          let exercises =
            plan.Blocks.[0].Variants.[0].Weeks.[0].Days.[0].Exercises

          Expect.equal
            (exercises |> List.map _.Laps)
            [ "3"; "3"; "2" ]
            "only circuit A gains a lap"
        | None -> failtest "no plan event"
    }

    uiTest "wizard brief reports every draft change"
    <| fun () -> task {
      let updates =
        ResizeArray<Briple.Trainer.Components.WizardBrief.BriefDraft>()

      let draft =
        Briple.Trainer.Components.WizardBrief.defaultDraft(
          sampleAthlete,
          false,
          ContentLanguage.Spanish
        )

      let! brief =
        probeUi(fun () ->
          Briple.Trainer.Components.WizardBrief.WizardBrief {
            Athlete = sampleAthlete
            HasPrevious = false
            Draft = draft
            OnChanged = updates.Add
          })

      do! mount brief

      // the default draft is the seeded one
      Expect.equal draft.Goal Hypertrophy "the athlete's first goal"

      Expect.equal draft.Frequency TresDias "the default frequency"

      Expect.equal draft.Emphasis [] "no emphasis by default"

      Expect.equal
        draft.Mode
        Briple.Trainer.Components.WizardBrief.Mode.Fresh
        "a first plan starts fresh"

      let! weekTwo = probeUi(fun () -> radioWithText(brief, "2"))

      let weekTwo =
        weekTwo
        |> Option.defaultWith(fun () ->
          failtest "the week radio did not render")

      do! runUi(fun () -> click weekTwo)

      let! reported =
        waitFor(
          2000,
          (fun () -> List.ofSeq updates),
          fun current -> not current.IsEmpty
        )

      Expect.isTrue reported "the change reached the page"

      let last = Seq.last updates

      Expect.equal last.Weeks 2 "the picked period"

      Expect.equal last.Goal Hypertrophy "the goal stays"

      Expect.equal last.Emphasis [] "the emphasis stays"
    }

    uiTest "the radio row follows a model change"
    <| fun () -> task {
      let selected = new BehaviorSubject<int>(1)

      let row =
        Briple.Trainer.Components.Fields.radioRow {
          Options = [ 1, "uno"; 2, "dos" ]
          Group = "radio-row-test"
          Selected = selected :> IObservable<int>
          OnPick = ignore
        }

      do! mount row

      let! first =
        waitFor(
          2000,
          (fun () -> radioWithText(row, "uno")),
          fun radio ->
            radio
            |> Option.exists(fun button ->
              button.IsChecked.GetValueOrDefault false)
        )

      Expect.isTrue first "the first option starts selected"

      selected.OnNext 2

      let! second =
        waitFor(
          2000,
          (fun () -> radioWithText(row, "dos")),
          fun radio ->
            radio
            |> Option.exists(fun button ->
              button.IsChecked.GetValueOrDefault false)
        )

      Expect.isTrue second "the selected value moved the check"
    }

    uiTest "the hub wheel pans and releases at the ends"
    <| fun () -> task {
      let step = Briple.Trainer.Components.Hub.wheelStep
      let down = Vector(0.0, -120.0)
      let up = Vector(0.0, 120.0)

      Expect.equal
        (step(100.0, 500.0, down))
        (Briple.Trainer.Components.Hub.WheelStep.Pan 148.0)
        "a forward turn pans one notch"

      Expect.equal
        (step(480.0, 500.0, down))
        (Briple.Trainer.Components.Hub.WheelStep.Pan 500.0)
        "the turn before the end stops at the end"

      Expect.equal
        (step(500.0, 500.0, down))
        Briple.Trainer.Components.Hub.WheelStep.Pass
        "a forward turn at the end goes to the page"

      Expect.equal
        (step(0.0, 500.0, up))
        Briple.Trainer.Components.Hub.WheelStep.Pass
        "a backward turn at the start goes to the page"

      Expect.equal
        (step(0.0, 500.0, down))
        (Briple.Trainer.Components.Hub.WheelStep.Pan 48.0)
        "a hub at the start takes the forward turn"

      Expect.equal
        (step(0.0, 0.0, down))
        Briple.Trainer.Components.Hub.WheelStep.Pass
        "a hub that fits takes nothing"
    }

    uiTest "the hub starts at its initial column and reports a click"
    <| fun () -> task {
      let picked = ref None

      let! hubControl =
        probeUi(fun () ->
          Briple.Trainer.Components.Hub.hub {
            Title = ""
            Slots = [
              Briple.Trainer.Components.Hub.slot(
                "Día 1",
                TextBlock(Text = "contenido 1")
              )
              Briple.Trainer.Components.Hub.slot(
                "Día 2",
                TextBlock(Text = "contenido 2")
              )
            ]
            InitialActive = ValueSome 1
            Active = ValueNone
            OnSelect = ValueSome(fun index -> picked.Value <- Some index)
          })

      let! _ = mountWindowSized(hubControl, 900.0, 400.0)

      let! started =
        probeUi(fun () ->
          match
            buttonWithText(hubControl, "Día 1"),
            buttonWithText(hubControl, "Día 2")
          with
          | Some first, Some second ->
            first.FontWeight = FontWeight.Normal
            && second.FontWeight = FontWeight.SemiBold
          | _ -> false)

      Expect.isTrue started "the hub used InitialActive for the active header"

      let! header = probeUi(fun () -> buttonWithText(hubControl, "Día 1"))

      let header =
        header
        |> Option.defaultWith(fun () ->
          failtest "the first header did not render")

      do! runUi(fun () -> click header)

      let! reported = waitFor(2000, (fun () -> picked.Value), (=)(Some 0))

      Expect.isTrue reported "the header click reached OnSelect"
    }
    uiTest "the hub overflows when the columns exceed the viewport"
    <| fun () -> task {
      let! hubControl =
        probeUi(fun () ->
          Briple.Trainer.Components.Hub.hub {
            Title = ""
            Slots = [
              for index in 1..5 ->
                Briple.Trainer.Components.Hub.slot(
                  $"Día {index}",
                  TextBlock(Text = $"contenido {index}")
                )
            ]
            InitialActive = ValueNone
            Active = ValueNone
            OnSelect = ValueNone
          })

      // the hub owns one scroller: five 400px columns in a 600px
      // window must overflow, and the slot headers must render
      let! _ = mountWindowSized(hubControl, 600.0, 400.0)

      let! overflow =
        waitFor(
          2000,
          (fun () -> scrollerIn hubControl),
          fun scroller ->
            scroller.Extent.Width > scroller.Viewport.Width + 100.0
        )

      Expect.isTrue overflow "the columns overflow the viewport"

      let! headers =
        waitFor(
          2000,
          (fun () -> hubControl),
          fun root -> hasText(root, "Día 1") && hasText(root, "Día 5")
        )

      Expect.isTrue headers "the slot headers rendered"
    }
    uiTest "the hub shares the viewport when its columns fit"
    <| fun () -> task {
      let! hubControl =
        probeUi(fun () ->
          Briple.Trainer.Components.Hub.hub {
            Title = ""
            Slots = [
              for index in 1..2 ->
                Briple.Trainer.Components.Hub.slot(
                  $"Día {index}",
                  TextBlock(Text = $"contenido {index}")
                )
            ]
            InitialActive = ValueNone
            Active = ValueNone
            OnSelect = ValueNone
          })

      let! _ = mountWindowSized(hubControl, 900.0, 400.0)

      let! fitted =
        waitFor(
          2000,
          (fun () -> scrollerIn hubControl),
          fun scroller -> scroller.Extent.Width <= scroller.Viewport.Width + 1.0
        )

      Expect.isTrue
        fitted
        "the two columns share the viewport instead of overflowing"

      let! headers =
        waitFor(
          2000,
          (fun () -> hubControl),
          fun root -> hasText(root, "Día 1") && hasText(root, "Día 2")
        )

      Expect.isTrue headers "both columns stay whole in a wide window"
    }
    uiTest "the hub pans on a vertical wheel and releases at its ends"
    <| fun () -> task {
      let! hubControl =
        probeUi(fun () ->
          Briple.Trainer.Components.Hub.hub {
            Title = ""
            Slots = [
              for index in 1..5 ->
                Briple.Trainer.Components.Hub.slot(
                  $"Día {index}",
                  TextBlock(Text = $"contenido {index}")
                )
            ]
            InitialActive = ValueNone
            Active = ValueNone
            OnSelect = ValueNone
          })

      let! outer =
        probeUi(fun () ->
          ScrollViewer(
            Content =
              StackPanel().Children(hubControl, Border().Height(1200.0))
          ))

      let! window = mountWindowSized(outer, 600.0, 400.0)

      let! point =
        probeUi(fun () ->
          match hubControl.TranslatePoint(Point(20.0, 20.0), window) with
          | point when point.HasValue -> point.Value
          | _ -> Point(20.0, 20.0))

      let wheel(delta: Vector) =
        runUi(fun () ->
          HeadlessWindowExtensions.MouseWheel(
            window,
            point,
            delta,
            RawInputModifiers.None
          ))

      let! hubScroller = probeUi(fun () -> scrollerIn hubControl)

      // a forward turn over the hub pans it; the page holds
      do! wheel(Vector(0.0, -120.0))

      let! hubOffset = probeUi(fun () -> hubScroller.Offset.X)

      Expect.isGreaterThan hubOffset 0.0 "the vertical wheel pans the hub"

      let! pageOffset = probeUi(fun () -> outer.Offset.Y)

      Expect.equal pageOffset 0.0 "the page holds while the hub can still pan"

      // at the end the same turn belongs to the page
      do!
        runUi(fun () ->
          hubScroller.Offset <-
            Vector(hubScroller.Extent.Width - hubScroller.Viewport.Width, 0.0))

      do! wheel(Vector(0.0, -120.0))

      let! scrolledDown = probeUi(fun () -> outer.Offset.Y)

      Expect.isGreaterThan
        scrolledDown
        0.0
        "the page scrolls once the hub reaches its end"

      // at the start a backward turn belongs to the page
      do! runUi(fun () -> hubScroller.Offset <- Vector(0.0, 0.0))
      do! wheel(Vector(0.0, 120.0))

      let! scrolledBack = probeUi(fun () -> outer.Offset.Y)

      Expect.isLessThan
        scrolledBack
        scrolledDown
        "the page scrolls back once the hub reaches its start"
    }

    uiTest "an action row keeps its actions out of the open button"
    <| fun () -> task {
      let opened = ref false
      let acted = ref false

      let row =
        actionRow {
          Title = TextBlock(Text = "Ana").Classes("body-strong")
          Detail = System.Reactive.Linq.Observable.Return "Strength"
          MoreLabel =
            System.Reactive.Linq.Observable.Return(get(es, Keys.commonMore))
          OnOpen = fun () -> opened.Value <- true
          Actions = [
            {
              Glyph = Briple.Trainer.Icons.archive
              Label =
                System.Reactive.Linq.Observable.Return(
                  get(es, Keys.athleteArchive)
                )
              Danger = false
              OnClick = fun () -> acted.Value <- true
            }
          ]
        }

      do! mount row

      let! more =
        probeUi(fun () -> buttonWithTooltip(row, get(es, Keys.commonMore)))

      let more =
        more
        |> Option.defaultWith(fun () ->
          failtest "the overflow button did not render")

      let! action =
        probeUi(fun () -> menuItemWithText(more, get(es, Keys.athleteArchive)))

      let action =
        action
        |> Option.defaultWith(fun () ->
          failtest "the action menu item did not render")

      do! runUi(fun () -> clickMenuItem action)

      Expect.isTrue acted.Value "the action did not run"
      Expect.isFalse opened.Value "the action opened the row"

      let! openButton = probeUi(fun () -> buttonWithText(row, "Ana"))

      let openButton =
        openButton
        |> Option.defaultWith(fun () ->
          failtest "the open button did not render")

      do! runUi(fun () -> click openButton)

      Expect.isTrue opened.Value "the open button did not open the row"
    }
  ]

// --- the note section --------------------------------------------------------

let private wait(task: Task<Result<'a, exn>>) : Result<'a, exn> =
  task.GetAwaiter().GetResult()

/// Answers the reading and records every note ask, so a test can assert
/// the live plan vocabulary the agent receives.
type private RecordingAgent(reading: NoteInterpretation) =
  let asks = ResizeArray<NoteAsk>()

  member _.Asks = asks

  interface Briple.Trainer.Env.IProvideAgent with
    member _.AgentAvailable = true

    member _.GenerateNew(_, _) = failwith "not used in this flow"

    member _.GenerateAdjustment(_, _) = failwith "not used in this flow"

    member _.ConsolidateMarks(_, _) = failwith "not used in this flow"

    member _.InterpretNote(ask, _) =
      asks.Add ask
      Task.FromResult(Ok reading)

/// Holds one generation open until the test releases it.
type private PendingAgent() =
  let completion =
    TaskCompletionSource<Result<Run, exn>>(
      TaskCreationOptions.RunContinuationsAsynchronously
    )

  member _.Finish(result: Result<Run, exn>) = completion.SetResult result

  interface Briple.Trainer.Env.IProvideAgent with
    member _.AgentAvailable = true

    member _.GenerateNew(_, _) = completion.Task

    member _.GenerateAdjustment(_, _) = completion.Task

    member _.ConsolidateMarks(_, _) = failwith "not used in this flow"

    member _.InterpretNote(_, _) = failwith "not used in this flow"

let noteTests =
  testList "note section" [
    uiTest "a note becomes a proposal and the confirmation persists it"
    <| fun () -> task {
      let db =
        Path.Combine(Path.GetTempPath(), $"briple-note-{Guid.NewGuid():N}.db")

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athleteId =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let plan =
        match Parse.parse sampleRaw with
        | Ok parsed -> parsed.Plan
        | Error error ->
          failtest $"the sample plan broke: {Parse.describe error}"

      let planId = Guid.NewGuid()
      let _, culture = liveCulture es

      let reading: NoteInterpretation = {
        Memories = [
          {
            Summary = ""
            Deltas = [ Load 5 ]
            Origin =
              Mark {
                Target = Exercise "Press de banca"
                Label = Light
                Date = DateOnly(2026, 10, 15)
              }
            Status = Proposed
            Scope = Plan
          }
        ]
        Rejections = []
      }

      let noteEnv: Briple.Trainer.Components.NoteSection.NoteEnv = {
        Agent = FakeAgent(reading)
        Memories = stores.Memories
        Logger = testLogger
        Culture = culture.Current()
        CultureChanged = culture.Changed
      }

      let! section =
        probeUi(fun () ->
          Briple.Trainer.Components.NoteSection.NoteSection(
            noteEnv,
            {
              AthleteId = athleteId
              Plan = fun () -> plan
              PlanId = planId
              Cancel = System.Threading.CancellationToken.None
              Initial = {
                Note = ""
                Busy = false
                Saving = false
                Proposals = []
                Status = Briple.Trainer.Components.NoteSection.Status.Idle
                Culture = culture.Current()
              }
              OnState = ignore
            },
            ignore
          ))

      do! mount section

      // the note entry is the section's only TextBox
      do!
        runUi(fun () ->
          match textBoxes section with
          | note :: _ -> note.Text <- "el press quedó liviano"
          | [] -> failtest "the note entry did not render")

      let! interpretButton =
        probeUi(fun () ->
          buttonWithText(section, get(es, Keys.reviewNoteInterpret)))

      let interpretButton =
        interpretButton
        |> Option.defaultWith(fun () ->
          failtest "the interpret button did not render")

      let! canInterpret =
        waitFor(2000, (fun () -> interpretButton.IsEnabled), id)

      Expect.isTrue canInterpret "the note reached the model"

      do! runUi(fun () -> click interpretButton)

      // the reading lands as a confirm/discard card
      let! card =
        waitFor(
          2000,
          (fun () -> section),
          fun root ->
            buttonWithText(root, get(es, Keys.memoriesConfirm)) |> Option.isSome
        )

      Expect.isTrue card "the proposal card did not render"

      let! confirm =
        probeUi(fun () ->
          buttonWithText(section, get(es, Keys.memoriesConfirm)))

      let confirm =
        confirm
        |> Option.defaultWith(fun () -> failtest "the confirm button vanished")

      // a double click: the in-flight save ignores the second decision
      do!
        runUi(fun () ->
          click confirm
          click confirm)

      let! persisted =
        waitFor(
          2000,
          (fun () -> stores.Memories.Snapshot athleteId),
          fun entities ->
            entities
            |> List.exists(fun entity -> entity.Memory.Status = Confirmed)
        )

      Expect.isTrue persisted "the confirmation reached the store"

      // give a second save, if the guard failed, time to land
      do! Task.Delay 100

      let confirmed =
        stores.Memories.Snapshot athleteId
        |> List.filter(fun entity -> entity.Memory.Status = Confirmed)

      Expect.equal (List.length confirmed) 1 "one decision writes one memory"

      let stored = List.head confirmed

      Expect.equal stored.PlanId (ValueSome planId) "the plan id rides"

      Expect.equal stored.Memory.Scope Plan "the situational scope rides"
    }

    uiTest "a note ask reads the plan at ask time, not at first render"
    <| fun () -> task {
      let db =
        Path.Combine(
          Path.GetTempPath(),
          $"briple-note-live-{Guid.NewGuid():N}.db"
        )

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete

      let athleteId =
        match athleteResult with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let plan =
        match Parse.parse sampleRaw with
        | Ok parsed -> parsed.Plan
        | Error error ->
          failtest $"the sample plan broke: {Parse.describe error}"

      let latestPlan = new BehaviorSubject<Plan>(plan)

      let reading: NoteInterpretation = { Memories = []; Rejections = [] }

      let agent = RecordingAgent(reading)
      let _, culture = liveCulture es

      let noteEnv: Briple.Trainer.Components.NoteSection.NoteEnv = {
        Agent = agent
        Memories = stores.Memories
        Logger = testLogger
        Culture = culture.Current()
        CultureChanged = culture.Changed
      }

      let! section =
        probeUi(fun () ->
          Briple.Trainer.Components.NoteSection.NoteSection(
            noteEnv,
            {
              AthleteId = athleteId
              Plan = fun () -> latestPlan.Value
              PlanId = Guid.NewGuid()
              Cancel = System.Threading.CancellationToken.None
              Initial = {
                Note = ""
                Busy = false
                Saving = false
                Proposals = []
                Status = Briple.Trainer.Components.NoteSection.Status.Idle
                Culture = culture.Current()
              }
              OnState = ignore
            },
            ignore
          ))

      do! mount section

      let! note =
        probeUi(fun () ->
          match textBoxes section with
          | note :: _ -> note
          | [] -> failtest "the note entry did not render")

      let! interpretButton =
        probeUi(fun () ->
          buttonWithText(section, get(es, Keys.reviewNoteInterpret)))

      let interpretButton =
        interpretButton
        |> Option.defaultWith(fun () ->
          failtest "the interpret button did not render")

      do! runUi(fun () -> note.Text <- "el press quedó liviano")

      let! firstReady = waitFor(2000, (fun () -> interpretButton.IsEnabled), id)

      Expect.isTrue firstReady "the first note reached the model"

      do! runUi(fun () -> click interpretButton)

      let! firstAsk =
        waitFor(2000, (fun () -> agent.Asks.Count), fun count -> count >= 1)

      Expect.isTrue firstAsk "the first ask reached the agent"

      Expect.contains
        agent.Asks.[0].Targets
        (Exercise "Press de banca")
        "the first ask carries the first plan vocabulary"

      let renameExercise(exercise: Exercise) = {
        exercise with
            Name = "Press renombrado"
      }

      let renameDay(day: Day) = {
        day with
            Exercises = day.Exercises |> List.map renameExercise
      }

      let renameWeek(week: Week) = {
        week with
            Days = week.Days |> List.map renameDay
      }

      let renameVariant(variant: Variant) = {
        variant with
            Weeks = variant.Weeks |> List.map renameWeek
      }

      let renameBlock(block: Block) = {
        block with
            Variants = block.Variants |> List.map renameVariant
      }

      let renamed = {
        plan with
            Blocks = plan.Blocks |> List.map renameBlock
      }

      do! runUi(fun () -> latestPlan.OnNext renamed)

      do! runUi(fun () -> note.Text <- "segunda lectura")

      let! secondReady =
        waitFor(2000, (fun () -> interpretButton.IsEnabled), id)

      Expect.isTrue secondReady "the second note reached the model"

      do! runUi(fun () -> click interpretButton)

      let! secondAsk =
        waitFor(2000, (fun () -> agent.Asks.Count), fun count -> count >= 2)

      Expect.isTrue secondAsk "the second ask reached the agent"

      Expect.contains
        agent.Asks.[1].Targets
        (Exercise "Press renombrado")
        "the second ask carries the edited vocabulary"

      Expect.isFalse
        (List.contains (Exercise "Press de banca") agent.Asks.[1].Targets)
        "the stale vocabulary is gone"
    }
  ]

// --- the review page --------------------------------------------------------

/// A real store with one athlete and one plan, plus the page's gate.
let private reviewSetup() =
  let db =
    Path.Combine(Path.GetTempPath(), $"briple-review-{Guid.NewGuid():N}.db")

  migrate(testLogger, db) |> ignore

  let stores = createStores(sqliteStore db, ignore)

  let athleteId =
    match wait(stores.Athletes.Add sampleAthlete) with
    | Ok entity -> entity.Id
    | Error e -> failtest $"add failed: {e}"

  let plan =
    match Parse.parse sampleRaw with
    | Ok parsed -> parsed.Plan
    | Error error -> failtest $"the sample plan broke: {Parse.describe error}"

  let entity =
    match
      wait(
        stores.Plans.Add(
          {
            AthleteId = athleteId
            Kind = "new"
            Counts = planCounts sampleAthlete plan
            CreatedAt = ValueNone
          },
          plan
        )
      )
    with
    | Ok entity -> entity
    | Error e -> failtest $"plan add failed: {e}"

  stores, athleteId, entity

let reviewTests =
  testList "review page" [
    uiTest "a clean review is Ready, an edit is Unsaved, the save clears it"
    <| fun () -> task {
      let stores, _, entity = reviewSetup()

      let source, culture = liveCulture es

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/review/test",
          fun _ _ -> async { return true }
        )

      let! page =
        probeUi(fun () ->
          Briple.Trainer.Views.Review.ReviewPage(
            reviewEnv stores (FakeAgent(noReading)) culture,
            {
              Lead = ValueNone
              Entity = entity
              Gate = gate
              Lifetime = testLifetime()
            }
          ))

      do! mount page

      Expect.equal
        gate.PhaseValue
        Briple.Trainer.Navigation.PagePhase.Ready
        "an untouched review is ready"

      let! rendered =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> hasText(root, get(es, Keys.reviewTitle))
        )

      Expect.isTrue rendered "the review rendered"

      do! runUi(fun () -> clickText(page, get(es, Keys.planEdit)))

      let! editing =
        waitFor(
          2000,
          (fun () -> buttonWithText(page, get(es, Keys.planDone))),
          Option.isSome
        )

      Expect.isTrue editing "the edit mode opened"

      do! runUi(fun () -> clickText(page, get(es, Keys.planEdit)))

      let! opened =
        waitFor(
          2000,
          (fun () -> page),
          fun root ->
            textBoxes(root) |> List.exists(fun box -> box.Width = 48.0)
        )

      Expect.isTrue opened "the edit mode opened"

      let! repsEntries =
        probeUi(fun () ->
          textBoxes page |> List.filter(fun box -> box.Width = 48.0))

      match repsEntries with
      | [ lo; hi ] ->
        do!
          runUi(fun () ->
            lo.Text <- "6"
            hi.Text <- "8")
      | _ -> failtest "the two rep fields did not open"

      let! dirty =
        waitFor(
          2000,
          (fun () -> gate.PhaseValue),
          fun value ->
            value = Briple.Trainer.Navigation.PagePhase.UnsavedChanges
        )

      Expect.isTrue dirty "the edit marks the review unsaved"

      do! runUi(fun () -> clickText(page, get(es, Keys.reviewSave)))

      let! saved =
        waitFor(
          2000,
          (fun () -> gate.PhaseValue),
          fun value -> value = Briple.Trainer.Navigation.PagePhase.Ready
        )

      Expect.isTrue saved "the save clears the phase"

      let! persisted =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot entity.AthleteId),
          fun plans ->
            plans
            |> List.exists(fun plan ->
              plan.Id = entity.Id && plan.Raw.Contains "6-8")
        )

      Expect.isTrue persisted "the edited raw reached the store"

      // a culture switch updates the composed plan text in place; the
      // static title stays in the app language and the draft survives
      do! runUi(fun () -> source.OnNext(Language.toCulture En))

      let! retitled =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> hasText(root, "Circuit A · 3 rounds")
        )

      Expect.isTrue retitled "the composed plan text changed language"

      let! repsAfter =
        probeUi(fun () ->
          textBoxes page
          |> List.filter(fun box -> box.Width = 48.0)
          |> List.map(fun box ->
            box.Text |> Option.ofObj |> Option.defaultValue "")
          |> String.concat "-")

      Expect.equal repsAfter "6-8" "the draft survived the rebuild"
    }
  ]

// --- the wizard hub ---------------------------------------------------------

let wizardTests =
  testList "wizard hub" [
    testCase "the app stamps the generation date" (fun () ->
      match Parse.parse sampleRaw with
      | Error error ->
        failtest $"the sample plan broke: {Parse.describe error}"
      | Ok parsed ->
        let dated =
          Briple.Trainer.Views.Generate.withGenerationDate
            (DateOnly(2026, 10, 17))
            parsed.Plan

        Expect.equal
          dated.Generated
          "2026-10-17"
          "the app writes the local date")

    uiTest "the wizard shows both sections whole at the default window"
    <| fun () -> task {
      let db =
        Path.Combine(
          Path.GetTempPath(),
          $"briple-wizard-fit-{Guid.NewGuid():N}.db"
        )

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athlete =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity
        | Error e -> failtest $"add failed: {e}"

      let _, culture = liveCulture es

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/athletes/test/generate",
          fun _ _ -> async { return true }
        )

      let! page =
        probeUi(fun () ->
          Briple.Trainer.Views.Generate.WizardPage(
            generateEnv stores (FakeAgent(noReading)) culture,
            {
              Lead = ValueSome(fun () -> TextBlock(Text = "back") :> Control)
              Athlete = athlete
              Confirmed = []
              Proposals = []
              Previous = ValueNone
              Gate = gate
              Lifetime = testLifetime()
            },
            ignore
          ))

      let! _ = mountWindowSized(page, 1100.0, 800.0)

      let! fitted =
        waitFor(
          2000,
          (fun () ->
            page.GetVisualDescendants()
            |> Seq.choose(fun visual ->
              match visual with
              | :? ScrollViewer as scroller when
                scroller.HorizontalScrollBarVisibility =
                  ScrollBarVisibility.Auto
                ->
                Some scroller
              | _ -> None)
            |> Seq.tryHead
            |> Option.map(fun scroller ->
              scroller.Extent.Width, scroller.Viewport.Width)),
          function
          | Some(extent, viewport) -> extent <= viewport + 1.0
          | None -> false
        )

      Expect.isTrue fitted "the two sections share the page, with no clipping"
    }

    uiTest "a decided proposal leaves the wizard and reaches the applies list"
    <| fun () -> task {
      let db =
        Path.Combine(
          Path.GetTempPath(),
          $"briple-wizard-proposal-{Guid.NewGuid():N}.db"
        )

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete

      let athlete =
        match athleteResult with
        | Ok entity -> entity
        | Error e -> failtest $"add failed: {e}"

      let _, culture = liveCulture es

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/athletes/test/generate",
          fun _ _ -> async { return true }
        )

      let proposalMemory: Memory = {
        Summary = "Ajuste de prueba"
        Deltas = [ Load 5 ]
        Origin =
          Mark {
            Target = Exercise "Press de banca"
            Label = Light
            Date = DateOnly(2026, 10, 15)
          }
        Status = Proposed
        Scope = Plan
      }

      let proposal: Briple.Trainer.Persistence.MemoryEntity = {
        Id = Guid.NewGuid()
        AthleteId = athlete.Id
        Memory = proposalMemory
        PlanId = ValueSome(Guid.NewGuid())
        CreatedAt = DateTime.UtcNow
        ArchivedAt = ValueNone
      }

      let! page =
        probeUi(fun () ->
          Briple.Trainer.Views.Generate.WizardPage(
            generateEnv stores (FakeAgent(noReading)) culture,
            {
              Lead = ValueNone
              Athlete = athlete
              Confirmed = []
              Proposals = [ proposal ]
              Previous = ValueNone
              Gate = gate
              Lifetime = testLifetime()
            },
            ignore
          ))

      let! _ = mountWindowSized(page, 1100.0, 800.0)

      let! confirm =
        probeUi(fun () -> buttonWithText(page, get(es, Keys.memoriesConfirm)))

      let confirm =
        confirm
        |> Option.defaultWith(fun () ->
          failtest "the proposal card did not render")

      // a double click: the first decision wins, the second is ignored
      do!
        runUi(fun () ->
          click confirm
          click confirm)

      let! gone =
        waitFor(
          2000,
          (fun () -> buttonWithText(page, get(es, Keys.memoriesConfirm))),
          Option.isNone
        )

      Expect.isTrue gone "the decided card left the list"

      let! applies =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> hasText(root, get(es, Keys.wizardApplies))
        )

      Expect.isTrue applies "the applies list updated"

      let! summary =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> hasText(root, "Ajuste de prueba")
        )

      Expect.isTrue summary "the confirmed summary is listed"
    }

    uiTest "the hub moves between sections and resets on activation"
    <| fun () -> task {
      let db =
        Path.Combine(Path.GetTempPath(), $"briple-wizard-{Guid.NewGuid():N}.db")

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athlete =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity
        | Error e -> failtest $"add failed: {e}"

      let source, culture = liveCulture es

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/athletes/test/generate",
          fun _ _ -> async { return true }
        )

      let! page =
        probeUi(fun () ->
          Briple.Trainer.Views.Generate.WizardPage(
            generateEnv stores (FakeAgent(noReading)) culture,
            {
              Lead = ValueNone
              Athlete = athlete
              Confirmed = []
              Proposals = []
              Previous = ValueNone
              Gate = gate
              Lifetime = testLifetime()
            },
            ignore
          ))

      // narrow enough that the two 400px columns cannot share it: the
      // headers then pan the hub from one section to the other
      let! _ = mountWindowSized(page, 620.0, 700.0)

      let! hub =
        probeUi(fun () ->
          page.GetVisualDescendants()
          |> Seq.choose(fun visual ->
            match visual with
            | :? ScrollViewer as scroller when
              scroller.HorizontalScrollBarVisibility =
                ScrollBarVisibility.Auto
              ->
              Some scroller
            | _ -> None)
          |> Seq.tryHead)

      match hub with
      | None -> failtest "the hub did not render"
      | Some hub ->
        let! briefHeader =
          probeUi(fun () -> buttonWithText(page, get(es, Keys.wizardStepBrief)))

        let briefHeader =
          briefHeader
          |> Option.defaultWith(fun () ->
            failtest "the brief header did not render")

        do! runUi(fun () -> click briefHeader)

        let! panned = waitFor(2000, (fun () -> hub.Offset.X), fun x -> x > 10.0)

        Expect.isTrue panned "the hub panned to the brief section"

        // a second activation resets the page: the hub returns to memories
        do! runUi(fun () -> gate.MarkShown())

        let! reset = waitFor(2000, (fun () -> hub.Offset.X), fun x -> x < 1.0)

        Expect.isTrue reset "the wizard reset to the first section"

        let! noStepBack =
          probeUi(fun () -> buttonWithText(page, get(es, Keys.wizardBack)))

        Expect.isNone noStepBack "the wizard has no step-back button"

        let! noNext =
          probeUi(fun () -> buttonWithText(page, get(es, Keys.wizardNext)))

        Expect.isNone noNext "the old wizard step button is gone"
    }

    uiTest "the page leaves the form when a run starts"
    <| fun () -> task {
      let db =
        Path.Combine(
          Path.GetTempPath(),
          $"briple-wizard-run-{Guid.NewGuid():N}.db"
        )

      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athlete =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity
        | Error e -> failtest $"add failed: {e}"

      let _, culture = liveCulture es
      let agent = PendingAgent()

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/athletes/test/generate",
          fun _ _ -> async { return true }
        )

      let! page =
        probeUi(fun () ->
          Briple.Trainer.Views.Generate.WizardPage(
            generateEnv stores agent culture,
            {
              Lead = ValueSome(fun () -> TextBlock(Text = "back") :> Control)
              Athlete = athlete
              Confirmed = []
              Proposals = []
              Previous = ValueNone
              Gate = gate
              Lifetime = testLifetime()
            },
            ignore
          ))

      let! _ = mountWindowSized(page, 900.0, 700.0)

      let! generate =
        probeUi(fun () -> buttonWithText(page, get(es, Keys.wizardGenerate)))

      let generate =
        generate
        |> Option.defaultWith(fun () ->
          failtest "the generate button did not render")

      do! runUi(fun () -> click generate)

      let! generating =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> hasText(root, get(es, Keys.wizardCancelGeneration))
        )

      Expect.isTrue generating "the page switched to the generating state"

      let plan =
        match Parse.parse sampleRaw with
        | Ok parsed -> parsed.Plan
        | Error error ->
          failtest $"the sample plan broke: {Parse.describe error}"

      agent.Finish(
        Ok {
          Response = ""
          Output = sampleRaw
          Verdict = Briple.Trainer.Core.Validator.Valid(plan, [])
        }
      )

      let! finished =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> hasText(root, get(es, Keys.wizardConfirm))
        )

      Expect.isTrue finished "the page shows the plan when the run ends"
    }
  ]
