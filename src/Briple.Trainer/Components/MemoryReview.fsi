module Briple.Trainer.Components.MemoryReview

// One proposed memory as an MVUish component: props in (the culture, the
// entity id and the memory), events out (Confirmed carries the memory
// with the accepted deltas; Discarded drops the proposal). The origin
// renders as localized prose from the data: Core emits no prose.

open System
open System.Globalization

open Avalonia.Controls

open Briple.Trainer.Core.Memories

/// What the card reports to its parent.
[<Struct>]
type Event =
  | Confirmed of id: Guid * memory: Memory
  | Discarded of discardedId: Guid

/// The card inputs.
[<Struct>]
type Props = {
  Culture: CultureInfo
  Id: Guid
  Memory: Memory
}

type Model = {
  Selected: Delta list
  Scope: MemoryScope
}

type Msg =
  | Toggle of Delta
  | SetScope of MemoryScope
  | Accept
  | Discard

/// The memory card.
type MemoryReview =
  inherit UserControl

  new:
    props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> MemoryReview
