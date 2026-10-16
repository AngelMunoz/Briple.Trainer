module Briple.Trainer.Toasts

open System.Reactive.Subjects

open Avalonia.Controls
open Avalonia.Controls.Notifications

// the host lands once at shell construction; the subject carries it so
// `show` stays a plain function instead of module state
let hosts = new BehaviorSubject<WindowNotificationManager voption>(ValueNone)

let init(host: Window) =
  hosts.OnNext(
    ValueSome(
      new WindowNotificationManager(
        host,
        Position = NotificationPosition.BottomRight,
        MaxItems = 3
      )
    )
  )

let show(title: string, body: string, isError: bool) =
  hosts.Value
  |> ValueOption.iter(fun manager ->
    manager.Show(
      Notification(
        title,
        body,
        (if isError then
           NotificationType.Error
         else

         NotificationType.Information)
      )
    ))
