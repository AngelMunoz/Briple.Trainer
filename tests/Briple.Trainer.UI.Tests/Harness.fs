module Briple.Trainer.UI.Tests.Harness

// The headless harness, per the F5 plan's test rules: one session for the
// whole assembly in PerAssembly isolation (PerTest resets the global UI
// dispatcher, which strands the MVUish update loop on a ghost dispatcher),
// every control touch through onUi, and a deterministic Background-priority
// frame pump for dispatcher work queued from other threads.

open System
open System.Globalization
open System.Reactive.Subjects
open System.Threading

open Avalonia
open Avalonia.Controls
open Avalonia.Headless
open Avalonia.Threading
open Avalonia.VisualTree

open Briple.Trainer.Components.Page
open Briple.Trainer.Startup

type TestApp() =
  inherit App()

let private session =
  HeadlessUnitTestSession.StartNew(
    typeof<TestApp>,
    AvaloniaTestIsolationLevel.PerAssembly
  )

/// A live page culture a test can switch: the subject drives the pages.
let liveCulture
  (culture: CultureInfo)
  : BehaviorSubject<CultureInfo> * PageCulture =
  let source = new BehaviorSubject<CultureInfo>(culture)

  source,
  {
    Current = fun () -> source.Value
    Changed = source :> IObservable<CultureInfo>
  }

/// Runs a unit-of-work on the UI thread and waits for it.
let onUi(test: unit -> unit) : unit =
  session.Dispatch(Action test, CancellationToken.None).GetAwaiter().GetResult()

/// Runs a probe on the UI thread and brings its value back.
let probeUi<'T>(probe: unit -> 'T) : 'T =
  let mutable value = ValueNone

  session
    .Dispatch(
      Action(fun () -> value <- ValueSome(probe())),
      CancellationToken.None
    )
    .GetAwaiter()
    .GetResult()

  match value with
  | ValueSome v -> v
  | ValueNone -> invalidOp "the dispatch never ran"

/// Drains every queued dispatcher job, including Background priority
/// (RunJobs clamps to the minimum active priority and never reaches it).
let pump() =
  onUi(fun () ->
    let frame = DispatcherFrame()

    Dispatcher.UIThread.InvokeAsync(
      Action(fun () -> frame.Continue <- false),
      DispatcherPriority.Background
    )
    |> ignore

    Dispatcher.UIThread.PushFrame frame)

/// Gives cross-thread producers a beat, then drains.
let settle() =
  Thread.Sleep 50
  pump()

/// Polls until the predicate holds or the timeout elapses; each probe runs
/// on the UI thread and each miss pumps, so the MVUish update loop and the
/// render pass advance between attempts.
let waitFor(ms: int, probe: unit -> 'T, predicate: 'T -> bool) : bool =
  let sw = Diagnostics.Stopwatch.StartNew()
  let mutable found = false

  while not found && sw.ElapsedMilliseconds < int64 ms do
    if probeUi(fun () -> predicate(probe())) then
      found <- true
    else
      Thread.Sleep 100
      pump()

  found

/// Attaches a control to a shown window and answers the window, so a
/// test that mounts a singleton (the dialog host) can detach it again.
let mountWindow(control: Control) : Window =
  probeUi(fun () ->
    let window = Window(Content = control)
    window.Show() |> ignore
    window)

/// Attaches a control to a shown window: the visual tree (and therefore
/// GetVisualDescendants and template parts) only exists once attached.
let mount(control: Control) : unit = mountWindow control |> ignore

/// True when a TextBlock under the root carries the text.
let hasText(root: Control, text: string) : bool =
  match root with
  | :? TextBlock as textBlock when textBlock.Text = text -> true
  | _ ->
    root.GetVisualDescendants()
    |> Seq.exists(fun visual ->
      match visual with
      | :? TextBlock as textBlock -> textBlock.Text = text
      | _ -> false)

/// Finds the first button whose content equals the text, whose label
/// does (CommandBarButton), or whose content subtree carries it (rows
/// with rich content).
let buttonWithText(root: Control, text: string) : Button option =
  let buttons =
    root.GetVisualDescendants()
    |> Seq.choose(fun visual ->
      match visual with
      | :? Button as button -> Some button
      | _ -> None)

  buttons
  |> Seq.tryPick(fun button ->
    let direct = string button.Content = text

    let labelled =
      match button with
      | :? CommandBarButton as command -> command.Label = text
      | _ -> false

    let nested =
      match button.Content with
      | :? Control as content -> hasText(content, text)
      | _ -> false

    if direct || labelled || nested then Some button else None)

/// Clicks through the routed event, the way MigrondiUI's tests do.
let click(button: Button) : unit =
  button.RaiseEvent(Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))
