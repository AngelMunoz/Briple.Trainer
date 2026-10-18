module Briple.Trainer.Core.PromptText

// The single boundary for text that comes from outside the prompt:
// athlete names, exercise names, notes, extras and memory deltas. The
// model reads only the sanitized form; the stored raw is not changed
// here. A line never carries a newline or a pipe, so it cannot forge a
// block or a format row. A block keeps its line breaks and loses the
// control characters.

/// One line: control characters, CR, LF, TAB and '|' become a space;
/// runs of whitespace collapse; the result is trimmed and capped. A cap
/// that falls inside a surrogate pair backs off one char.
val line: maxLength: int -> text: string -> string

/// Prose: CRLF and CR become LF, control characters other than LF and
/// TAB are removed, TAB becomes a space, runs of spaces collapse, the
/// result is trimmed and capped.
val block: maxLength: int -> text: string -> string

/// A short token: one line, and the separators that a list or a delta
/// protocol uses (',', ';') become a space. The result is trimmed and
/// capped.
val token: maxLength: int -> text: string -> string

/// A prose line: CR, LF and TAB become spaces, control characters are
/// removed, runs of spaces collapse, and '|' stays because a title or an
/// extra uses it as its own separator. The result is trimmed and capped.
val flat: maxLength: int -> text: string -> string
