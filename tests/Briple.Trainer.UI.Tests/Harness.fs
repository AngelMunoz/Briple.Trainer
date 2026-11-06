module Briple.Trainer.UI.Tests.Harness

// The headless harness: one session for the whole assembly in PerAssembly
// isolation (PerTest resets the global UI dispatcher, which strands the
// MVUish update loop on a ghost dispatcher). Every control touch runs on
// that one UI thread through an awaited task. The 8-second cap turns a
// blocked dispatcher into a test failure instead of a hung suite.

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Reactive.Subjects
open System.Threading
open System.Threading.Tasks

open Avalonia
open Avalonia.Controls
open Avalonia.Headless
open Avalonia.Threading
open Avalonia.VisualTree

open Expecto
open MVUish.Avalonia

open Briple.Trainer.Components.Page
open Briple.Trainer.Startup

type TestApp() =
  inherit App()

  /// The suite's carrier: Skia over the headless platform with the
  /// emulated drawing off, so CaptureRenderedFrame returns the pixels
  /// a user would see and the camera can save them.
  static member BuildAvaloniaApp() =
    AppBuilder
      .Configure<TestApp>()
      .UseSkia()
      .UseHeadless(AvaloniaHeadlessPlatformOptions(UseHeadlessDrawing = false))

let private session =
  HeadlessUnitTestSession.StartNew(
    typeof<TestApp>,
    AvaloniaTestIsolationLevel.PerAssembly
  )

/// Hard cap for one UI dispatch. A body that blocks the dispatcher fails
/// instead of hanging the suite.
let testTimeout = TimeSpan.FromSeconds 8.0

let private timeout() =
  TimeoutException($"the UI test exceeded {testTimeout.TotalSeconds} seconds")

/// Runs an async body on the UI thread. The body must await its work and
/// must never block the dispatcher.
let onUi(work: unit -> Task<'T>) : Task<'T> = task {
  use cts = new CancellationTokenSource(testTimeout)

  try
    return! session.Dispatch<'T>(Func<Task<'T>>(work), cts.Token)
  with
  | :? OperationCanceledException -> return raise(timeout())
  | :? AggregateException as error when
    error.InnerExceptions
    |> Seq.exists(fun inner -> inner :? OperationCanceledException)
    ->
    return raise(timeout())
}

/// Runs a synchronous action inside the UI test body.
let runUi(work: unit -> unit) : Task<unit> = task { work() }

/// Reads a value inside the UI test body.
let probeUi(read: unit -> 'T) : Task<'T> = Task.FromResult(read())

/// Alias for probes that read a page model slice.
let probe(read: unit -> 'T) : Task<'T> = probeUi read

/// Polls inside the UI test body until the predicate holds or the timeout
/// expires. Task.Delay yields the dispatcher, so the MVUish loop and the
/// render pass advance between attempts.
let waitFor(ms: int, read: unit -> 'T, predicate: 'T -> bool) : Task<bool> =
  task {
    let watch = Stopwatch.StartNew()
    let mutable found = predicate(read())

    while not found && watch.ElapsedMilliseconds < int64 ms do
      do! Task.Delay 10
      found <- predicate(read())

    return found
  }

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

// --- windows and page lifetimes ---------------------------------------------

/// A disposable bag for route pages that need a page lifetime in tests.
type TestLifetime() =
  let items = ResizeArray<IDisposable>()

  interface Navs.IDisposableBag with
    member _.AddDisposable disposable = items.Add disposable

  interface IDisposable with
    member _.Dispose() =
      for item in items do
        item.Dispose()

      items.Clear()

let private trackedWindows = ResizeArray<Window>()
let private trackedLifetimes = ResizeArray<TestLifetime>()
let private gate = obj()

let private trackWindow(window: Window) =
  lock gate (fun () -> trackedWindows.Add window)

/// Registers a window the test created itself, so the wrapper closes it.
let trackWindowForTest(window: Window) = trackWindow window

let private trackLifetime(lifetime: TestLifetime) =
  lock gate (fun () -> trackedLifetimes.Add lifetime)

/// The lifetime of one route page; the test wrapper disposes it on the UI
/// thread after the test body.
let testLifetime() : TestLifetime =
  let lifetime = new TestLifetime()
  trackLifetime lifetime
  lifetime

/// Attaches a control to a shown window. The test wrapper closes the
/// window after the test, which detaches the page programs.
let mountWindow(control: Control) : Task<Window> = task {
  let window = Window(Content = control)
  window.Show() |> ignore
  trackWindow window
  return window
}

/// Attaches a control to a shown window with an explicit size.
let mountWindowSized
  (control: Control, width: float, height: float)
  : Task<Window> = task {
  let window = Window(Content = control, Width = width, Height = height)
  window.Show() |> ignore
  trackWindow window
  return window
}

/// Attaches a control to a shown window and discards the handle.
let mount(control: Control) : Task<unit> = task {
  let! _ = mountWindow control
  return ()
}

/// Saves one rendered frame of a shown window as the named PNG under
/// artifacts/snapshots (gitignored: captures are evidence, not fixtures)
/// and returns its path. The session renders through Skia, so the pixels
/// carry the theme and the text a user sees. Runs on the session thread:
/// a uiTest body holds the one UI dispatch until it returns, and a nested
/// session dispatch queues work the blocked thread can never serve, so
/// every UI touch in a body runs inline before the body's first await.
let capture(name: string, window: Window) : string =
  match window.CaptureRenderedFrame() with
  | null -> failwith $"no rendered frame to capture for {name}"
  | bitmap ->
    let file = Path.Combine("artifacts", "snapshots", name + ".png")

    let dir =
      match Path.GetDirectoryName file with
      | null -> "."
      | found -> found

    Directory.CreateDirectory dir |> ignore

    use stream = File.Create(file)
    bitmap.Save(stream)
    file

let private closeTracked() : Task<unit> = task {
  let windows, lifetimes =
    lock gate (fun () ->
      let windows = List.ofSeq trackedWindows
      let lifetimes = List.ofSeq trackedLifetimes
      trackedWindows.Clear()
      trackedLifetimes.Clear()
      windows, lifetimes)

  for window in windows do
    window.Content <- null
    window.Close()

  for lifetime in lifetimes do
    (lifetime :> IDisposable).Dispose()
}

/// One UI test: the whole body runs on the one UI thread, then the
/// wrapper closes its windows and disposes its route lifetimes there.
let uiTest (name: string) (body: unit -> Task) : Test =
  testCaseTask name (fun () -> task {
    let! _ =
      onUi(fun () -> task {
        let mutable failure = None

        try
          do! body()
        with error ->
          failure <- Some error

        let mutable cleanupFailure = None

        try
          do! closeTracked()
        with error ->
          cleanupFailure <- Some error

        match failure, cleanupFailure with
        | Some error, _ -> return raise error
        | None, Some error -> return raise error
        | None, None -> return ()
      })

    return ()
  })

// --- control probes ---------------------------------------------------------

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

/// Finds the first button whose tooltip text equals the given text.
let buttonWithTooltip(root: Control, text: string) : Button option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? Button as button ->
      match ToolTip.GetTip(button) with
      | :? TextBlock as tip when tip.Text = text -> Some button
      | _ -> None
    | _ -> None)

/// Finds the first check box whose content contains the text.
let checkBoxWithText(root: Control, text: string) : CheckBox option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? CheckBox as check when hasText(check, text) -> Some check
    | _ -> None)

/// Finds the overflow menu item with the given header text.
let menuItemWithText(button: Button, text: string) : MenuItem option =
  match button.Flyout with
  | :? MenuFlyout as flyout ->
    if not flyout.IsOpen then
      flyout.ShowAt button

    let found =
      flyout.Items
      |> Seq.choose(fun item ->
        match item with
        | :? MenuItem as menuItem -> Some menuItem
        | _ -> None)
      |> Seq.tryFind(fun item -> hasText(item, text))

    if found.IsSome then
      flyout.Hide()

    found
  | _ -> None

/// Clicks a menu item through its routed click event.
let clickMenuItem(item: MenuItem) : unit =
  item.RaiseEvent(Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent))

/// The first control with the given name under the root.
let controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? Control as control when control.Name = name -> Some control
    | _ -> None)

/// Clicks through the routed event, the way MigrondiUI's tests do.
let click(button: Button) : unit =
  button.RaiseEvent(Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))

/// Records the last model, stream completion, and the dispatch function,
/// so a teardown test can drive messages by hand and assert the freeze.
type RecordingHarness<'Model, 'Msg>() =
  let last = ref Unchecked.defaultof<'Model>
  let mutable dispatch = Unchecked.defaultof<Dispatch<'Msg>>
  let completed = ref false

  member _.View: IObservable<'Model> -> Dispatch<'Msg> -> Control =
    fun model next ->
      dispatch <- next

      model.Subscribe(
        (fun value -> last.Value <- value),
        (fun _ -> ()),
        (fun () -> completed.Value <- true)
      )
      |> ignore

      Border()

  member _.Dispatch(message) = dispatch message
  member _.Model = last.Value
  member _.Completed = completed.Value
