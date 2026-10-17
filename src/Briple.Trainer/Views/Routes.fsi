module Briple.Trainer.Views.Routes

// The route table: pivots with Navs. Route factories load what they need
// from the env and build the pages; `go` is the one imperative
// navigation helper. The shell owns history and hands every page a fresh
// back control through `lead`. Navs keeps no history, so "back" is a
// plain Navigate.

open Avalonia.Controls
open Navs
open Navs.Avalonia

open Briple.Trainer.Env

type RouteDeps<'T
  when 'T :> IProvideStore
  and 'T :> IProvideStores
  and 'T :> IProvideCulture
  and 'T :> IProvideLanguage
  and 'T :> IProvideTheme
  and 'T :> IProvideHost
  and 'T :> IProvideAgent
  and 'T :> IProvideCredentials
  and 'T :> IProvideLogging> = 'T

/// Navigates and reports a failure through the log.
val inline go: nav: INavigable<Control> * url: string -> unit

/// The router plus the page-phase surface the shell needs.
[<Struct>]
type ShellRoutes = {
  Router: Navs.IRouter<Avalonia.Controls.Control>

  /// True when leaving the page at this URL is allowed.
  AskLeave: string -> Async<bool>
}

/// The route table over the environment. `lead` builds one back
/// control per page; the shell owns its behavior and its history.
val build: env: RouteDeps<'T> * lead: (unit -> Control) -> ShellRoutes
