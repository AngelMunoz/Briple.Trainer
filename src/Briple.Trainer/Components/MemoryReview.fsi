module Briple.Trainer.Components.MemoryReview

// One proposed memory as an MVUish component: props in (the culture, the
// entity id and the memory), events out (Confirmed carries the memory
// with the accepted deltas; Discarded drops the proposal). The origin
// renders as localized prose from the data: Core emits no prose.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Memories

/// What the card reports to its parent.
[<Struct>]
type Event =
  | Confirmed of id: Guid * memory: Memory
  | Discarded of discardedId: Guid

/// The delta the trainer is about to add.
type DeltaKind =
  | RirKind
  | RepsKind
  | SetsKind
  | LoadKind
  | VariantKind
  | DurationKind
  | ZoneKind
  | ModalityKind

/// The card inputs.
[<Struct>]
type Props = {
  Culture: PageCulture
  Id: Guid
  Memory: Memory
}

type Model = {
  /// The deltas the model proposed; a toggle removes or restores one.
  Selected: Delta list
  /// The deltas the trainer added in this card.
  Added: Delta list
  Scope: MemoryScope
  Kind: DeltaKind
  Value: string
}

type Msg =
  | Toggle of Delta
  | SetScope of MemoryScope
  | SetKind of DeltaKind
  | SetValue of string
  | AddDelta
  | RemoveAdded of Delta
  | Accept
  | Discard

/// The memory card.
type MemoryReview =
  inherit UserControl

  new:
    props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> MemoryReview
