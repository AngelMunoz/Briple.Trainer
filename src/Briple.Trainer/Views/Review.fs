module Briple.Trainer.Views.Review

// The review page for a stored plan: the PlanReview component for field
// edits, save back into the Raw (the source of truth), the .briple
// export, and the note section — the trainer writes what they observed
// and the agent maps it to the closed surface of this plan.

open System
open System.Globalization

open System.Reactive.Subjects

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Platform.Storage
open NXUI.Extensions

open IcedTasks

open Serilog

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.Theme

[<Struct>]
type ReviewPageProps = {
  Culture: CultureInfo
  Plans: PlanStore
  Memories: MemoryStore
  Agent: IProvideAgent
  Host: Window voption
  Entity: PlanEntity
}

// --- page pieces as module functions ---

let inline invalidBody(props: ReviewPageProps, failure: PlanFailure) : Control =
  fluentPage {
    Title = get(props.Culture, Keys.reviewTitle)
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          glyph(Icons.alertCircle, 24.0, errorBrush),
          TextBlock()
            .Text(
              getf(
                props.Culture,
                Keys.reviewInvalid,
                [ Parse.describe failure :> obj ]
              )
            )
            .Foreground(errorBrush)
            .FontFamily(font)
        )
    Measure = 760.0
  }

/// The raw text to persist: the latest edit when there is one, the stored
/// Raw otherwise.
let inline rawOf(latest: Plan option, entity: PlanEntity) : string =
  latest
  |> Option.map Briple.Trainer.Core.Plan.Writer.write
  |> Option.defaultValue entity.Raw

let inline saveCommand
  (
    props: ReviewPageProps,
    latest: BehaviorSubject<Plan option>,
    status: BehaviorSubject<string>
  ) : CommandBarButton =
  iconCommand(
    get(props.Culture, Keys.reviewSave),
    Icons.contentSave,
    fun () ->
      latest.Value
      |> Option.iter(fun _ ->
        asyncEx {
          let raw = rawOf(latest.Value, props.Entity)

          match! props.Plans.UpdateRaw(props.Entity.Id, raw) with
          | Ok() -> status.OnNext(get(props.Culture, Keys.reviewSaved))
          | Error e ->
            Log.Error(e, "plan update failed")

            let action = get(props.Culture, Keys.reviewSave)
            let wording = friendly(props.Culture, action, e.Message)

            status.OnNext wording

            Briple.Trainer.Toasts.show(action, wording, true)
        }
        |> Async.StartImmediate)
  )

let inline exportCommand
  (
    props: ReviewPageProps,
    latest: BehaviorSubject<Plan option>,
    status: BehaviorSubject<string>
  ) : CommandBarButton voption =
  props.Host
  |> ValueOption.map(fun host ->
    let t key = get(props.Culture, key)

    let report(action: string, error: exn) =
      Log.Error(error, "{Actiono} failed", action)

      let wording = friendly(props.Culture, action, error.Message)

      status.OnNext wording

      Briple.Trainer.Toasts.show(action, wording, true)

    let pick() =
      asyncEx {
        try
          let options =
            FilePickerSaveOptions(
              Title = t Keys.reviewExport,
              SuggestedFileName = $"{props.Entity.Title}.briple",
              DefaultExtension = "briple"
            )

          let! picked = host.StorageProvider.SaveFilePickerAsync(options)

          match Option.ofObj picked with
          | None -> ()
          | Some file ->
            match
              exportBriple(
                file.Path.LocalPath,
                rawOf(latest.Value, props.Entity)
              )
            with
            | Ok path ->
              status.OnNext(
                getf(props.Culture, Keys.reviewExported, [ path :> obj ])
              )
            | Error e -> report(t Keys.reviewExport, e)
        with e ->
          // the picker runs fire-and-forget: an unhandled throw here dies
          // on a thread-pool thread and takes the process with it
          report(t Keys.reviewExport, e)
      }
      |> Async.Start

    iconCommand(t Keys.reviewExport, Icons.download, pick))

// --- the page ---

type ReviewPage(props: ReviewPageProps) as this =
  inherit UserControl()

  do
    // the note reading cancels with the page: leaving the review stops it
    let notesCts = new Threading.CancellationTokenSource()

    this.DetachedFromVisualTree.Add(fun _ ->
      notesCts.Cancel()
      notesCts.Dispose())

    this.Content <-
      match Parse.parse props.Entity.Raw with
      | Error error -> invalidBody(props, error)
      | Ok parsed ->
        // the edits travel to the parent and back into the Raw on save
        let latest = new BehaviorSubject<Plan option>(None)

        let status = new BehaviorSubject<string>("")

        let review =
          PlanReview.PlanReview(
            {
              Culture = props.Culture
              Parsed = parsed
            },
            (function
            | PlanReview.PlanChanged plan -> latest.OnNext(Some plan))
          )

        let notes =
          NoteSection.NoteSection(
            {
              Culture = props.Culture
              Agent = props.Agent
              Memories = props.Memories
              AthleteId = props.Entity.AthleteId
              Plan = parsed.Plan
              PlanId = props.Entity.Id
              Cancel = notesCts.Token
            },
            ignore
          )

        let save = saveCommand(props, latest, status)

        let commands: Control list =
          exportCommand(props, latest, status)
          |> ValueOption.fold (fun _ picker -> [ save; picker ]) [ save ]

        fluentPage {
          Title = get(props.Culture, Keys.reviewTitle)
          Commands = commands
          Footer = noFooter
          Body =
            StackPanel()
              .Spacing(8.0)
              .Children(
                TextBlock().Text(status).Foreground(muted).FontFamily(font),
                // the edits live in `latest`: this keeps the pending work
                // visible until Guardar persists it
                TextBlock()
                  .Text(get(props.Culture, Keys.reviewUnsaved))
                  .Foreground(muted)
                  .FontFamily(font)
                  .IsVisible(latest |> Observable.map Option.isSome),
                review,
                notes
              )
          Measure = 760.0
        }
