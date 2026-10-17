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
  Culture: PageCulture
  /// The shell's back control, placed in the page header.
  Lead: Control voption
  Save: string -> Result<CredentialSave, string>
  OnDone: unit -> unit
}

/// The login page.
type LoginPage =
  inherit UserControl

  new: props: LoginPageProps -> LoginPage
