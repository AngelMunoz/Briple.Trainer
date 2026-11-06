module Briple.Trainer.Views.Import

// The plan import page: the trainer picks a .briple or .txt file, the
// page shows what the file holds (every gender, every variant, through
// the read-only plan review), the trainer chooses one variant of the
// athlete's gender block, and the page writes one imported plan. The
// store keeps the writer's render of the slice, the same rule as a
// generated plan; the picked file's own text is not kept.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// The services the import page needs, built once at the route; the host
/// window is the file picker's owner and may be absent while the shell
/// builds and in tests.
[<Struct>]
type ImportEnv = {
  Plans: PlanStore
  Host: Window voption
  Logger: Microsoft.Extensions.Logging.ILogger
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

/// The data of the page.
[<Struct>]
type ImportPageProps = {
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  Athlete: AthleteEntity
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

/// What the page reports to its host.
type Event = Imported of planId: Guid

/// One picked file: still none, structurally unreadable, or parsed with
/// the warnings the parser recovered.
type Document =
  | NoFile
  | Unreadable of PlanFailure
  | Readable of ParsedPlan

/// Parses picked file text into the page's document; recoverable
/// problems stay visible as warnings inside `Readable`.
val documentOf: text: string -> Document

/// The variants the athlete can import: the variants of the plan's
/// gender block, in file order. Empty when the file has no block for
/// the athlete's gender.
val variantsFor: athlete: Athlete * plan: Plan -> Variant list

/// The draft one chosen variant produces: kind "import", the counts of
/// the athlete's block, and the plan sliced to that one variant. None
/// when the plan has no such block or variant.
val draftOf:
  athlete: Athlete * variantId: string * plan: Plan -> PlanDraft option

/// The import page.
type ImportPage =
  inherit UserControl

  new:
    env: ImportEnv *
    props: ImportPageProps *
    [<InlineIfLambda>] onEvent: (Event -> unit) ->
      ImportPage
