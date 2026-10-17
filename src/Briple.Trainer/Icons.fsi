module Briple.Trainer.Icons

// The app's own glyph collection: one StreamGeometry per icon, no icon
// font, no SVG files, no third-party packages. Every glyph is a single
// 24x24 fill path lifted from Pictogrammers MDI Light v0.2.63
// (`@mdi/light-svg`), with MDI v7.4.47 (`@mdi/svg`) filling the names
// MDIL leaves out. PathIcon renders the geometry and takes Foreground,
// so theme and state colors flow with no per-icon styling.

open Avalonia.Media

/// The pane toggle. MDIL `menu`.
val menu: StreamGeometry

/// Back one page. MDIL `arrow-left`.
val arrowLeft: StreamGeometry

/// Settings section. MDIL `settings`.
val settings: StreamGeometry

/// New item action. MDIL `plus`.
val plus: StreamGeometry

/// Generate, the wizard entry. MDIL `flash`.
val flash: StreamGeometry

/// Save to storage. MDIL `content-save`.
val contentSave: StreamGeometry

/// Next step. MDIL `chevron-right`.
val chevronRight: StreamGeometry

/// Previous step. MDIL `chevron-left`.
val chevronLeft: StreamGeometry

/// Start the generation. MDIL `play`.
val play: StreamGeometry

/// Cancel a running generation. MDIL `stop`.
val stop: StreamGeometry

/// Leave the wizard. MDIL `logout`.
val logout: StreamGeometry

/// Retry a failed generation. MDIL `refresh`.
val refresh: StreamGeometry

/// Confirm. MDIL `check`.
val check: StreamGeometry

/// Discard, the destructive exit. MDIL `delete`.
val delete: StreamGeometry

/// Export to a .briple file. MDIL `download`.
val download: StreamGeometry

/// Inline edit. MDIL `pencil`.
val pencil: StreamGeometry

/// Read and interpret a note. MDIL `comment-text`.
val commentText: StreamGeometry

/// Error or warning condition. MDIL `alert-circle`.
val alertCircle: StreamGeometry

/// Athletes section. MDI `account-group` (no MDIL glyph).
val accountGroup: StreamGeometry

/// Undo an inline edit. MDI `undo` (no MDIL glyph).
val undo: StreamGeometry

/// Provider token. MDI `key` (no MDIL glyph).
val key: StreamGeometry

/// Empty athletes list. MDI `account-search-outline` (no MDIL glyph).
val accountSearchOutline: StreamGeometry
