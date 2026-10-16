module Briple.Trainer.Secrets

// The OS-secured vault for the agent token, selected per platform once
// at construction: DPAPI on Windows, Keychain on macOS, libsecret
// (Secret Service) on Linux. Every implementation is plain P/Invoke or
// BCL crypto — no reflection, NativeAOT-safe. The insecure fallback
// never happens here: the caller (Env) decides what to do when the vault
// reports a failure, so the reason always reaches the user.

/// The platform-agnostic vault.
type ISecureStorage =

  /// Stores the token; Error carries the reason, phrased for the user.
  abstract Save: token: string -> Result<unit, string>

  /// Reads the token; None when absent.
  abstract Load: unit -> string option

  /// Removes the token.
  abstract Clear: unit -> Result<unit, string>

/// Picks the implementation for the current platform once; on a system
/// without a vault, every call answers with the unavailable reason.
val create: unit -> ISecureStorage
