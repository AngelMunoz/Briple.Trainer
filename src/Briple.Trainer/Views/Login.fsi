module Briple.Trainer.Views.Login

// The provider login page: the DeepSeek token and nothing else. The
// token goes through the env credentials capability. A vault save
// continues straight to the app; the insecure fallback stays on the
// page with the warning and an explicit continue button.

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Env

/// The data and callbacks of the page.
[<Struct>]
type LoginPageProps = {
  /// The shell's back control, placed in the page header.
  Lead: (unit -> Control) voption
  OnDone: unit -> unit
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

/// The login page.
type LoginPage<'T when 'T :> IProvideCredentials and 'T :> IProvideCulture> =
  inherit UserControl

  new: env: 'T * props: LoginPageProps -> LoginPage<'T>
