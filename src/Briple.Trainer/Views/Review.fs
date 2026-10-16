module Briple.Trainer.Views.Review

// The review page for a stored plan: the PlanReview component for field
// edits, save back into the Raw (the source of truth) and the .briple
// export through a file picker on the host window. The latest edit and
// the status line are subjects the controls bind to; nothing mutates
// children by hand.

open System
open System.Globalization

open System.Reactive.Subjects

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Platform.Storage
open NXUI.Extensions

open IcedTasks

open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type ReviewPageProps = {
  Culture: CultureInfo
  Store: IStore
  Host: Window voption
  Entity: PlanEntity
}

// --- page pieces as module functions ---

let inline invalidBody(props: ReviewPageProps, error: ParseError) : Control =
  StackPanel()
    .Margin(24.0)
    .Spacing(8.0)
    .Children(
      title(get(props.Culture, Keys.reviewTitle)),
      TextBlock()
        .Text(getf(props.Culture, Keys.reviewInvalid, [ error.Message :> obj ]))
        .Foreground(errorBrush)
        .FontFamily(font)
    )

/// The raw text to persist: the latest edit when there is one, the stored
/// Raw otherwise.
let inline rawOf(latest: Plan option, entity: PlanEntity) : string =
  latest
  |> Option.map Briple.Trainer.Core.Plan.Writer.write
  |> Option.defaultValue entity.Raw

let inline saveButton
  (
    props: ReviewPageProps,
    latest: BehaviorSubject<Plan option>,
    status: BehaviorSubject<string>
  ) : Control =
  primaryButton(
    get(props.Culture, Keys.reviewSave),
    (fun () ->
      latest.Value
      |> Option.iter(fun _ ->
        asyncEx {
          let raw = rawOf(latest.Value, props.Entity)

          match! props.Store.UpdatePlanRaw(props.Entity.Id, raw) with
          | Ok() -> status.OnNext(get(props.Culture, Keys.reviewSaved))
          | Error e -> eprintfn $"plan update failed: %O{e}"
        }
        |> Async.StartImmediate))
  )

let inline exportButton
  (
    props: ReviewPageProps,
    latest: BehaviorSubject<Plan option>,
    status: BehaviorSubject<string>
  ) : Control voption =
  props.Host
  |> ValueOption.map(fun host ->
    let t key = get(props.Culture, key)

    let pick() =
      asyncEx {
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
          | Error e -> eprintfn $"export failed: %O{e}"
      }
      |> Async.Start

    button(t Keys.reviewExport, pick))

// --- the page ---

type ReviewPage(props: ReviewPageProps) as this =
  inherit UserControl()

  do
    this.Content <-
      match parsePlan props.Entity.Raw with
      | Error error -> invalidBody(props, error)
      | Ok parsed ->
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

        let save = saveButton(props, latest, status)

        let actions =
          exportButton(props, latest, status)
          |> ValueOption.fold (fun _ picker -> [| save; picker |]) [| save |]


        ScrollViewer()
          .Content(
            StackPanel()
              .Margin(24.0)
              .Spacing(8.0)
              .Children(
                title(get(props.Culture, Keys.reviewTitle)),
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(8.0)
                  .Children(actions),
                TextBlock().Text(status).Foreground(muted).FontFamily(font),
                review
              )
          )
