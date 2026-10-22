# Changelog

Notable changes to the Adamantium UI packages, in the form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All packages share one version.

## Unreleased

### Added

- `IWindow.SizeToContent` (`Manual`, `Width`, `Height`, `WidthAndHeight`): a window that follows what its content asks
  for on those sides, fitted again when the content's size changes, so a size the user gives it stays until then (the
  fitted size coming back from the OS, rounded to its pixels, is not such a size). By default a window keeps the size
  it is given. `WindowDialogHost` turns it on for the sides a dialog states no size of.
- `IWindow.UpdateClientSizeFromPlatform`: the platform reports the client size it gave the window - a resize by its
  border, maximize, restore, or an asked-for size coming back rounded - the way `UpdatePositionFromPlatform` reports a
  move. Only a size the window did not ask for, in the normal state, ends `SizeToContent`; a size set from code
  always does. The Win32, Wayland and macOS workers report through it, and set the state before the size.
- `LaunchOptions.VirtualDisplay` (`adam-auto start --virtual`): on Linux the application is started by a headless Mutter
  with a virtual monitor of its own, on a D-Bus session of its own, and the compositor exits with it - it shows nowhere
  on the desktop and takes no one's focus. Generated scenario tests use it, so a run no longer pulls the focus away for
  every scenario. Without Mutter the application shows on the desktop, which is said on standard error.
- Docking on Wayland: a panel torn off, and a floating window dragged by its title bar, ride under the pointer through
  `xdg_toplevel_drag` - the compositor carries the window in a drag of our own type, which no other client takes, and
  our windows are told where the pointer is over them. `WindowDragPointer` holds that point while such a drag lasts;
  `DockingArea` aims and drops by it, with its compass drawn inside the area. `WindowBase.DragMove(Vector2 grab)` and
  `IWindowWorkerService.BeginMoveDrag(Vector2 grab)` start a move held at a point of the window that is not under the
  pointer yet, as a panel just torn off; `WindowBase.IsMoveTracked` says that something follows the window's moves -
  only such windows are carried as a drag, the rest are moved by `xdg_toplevel.move`.
- A tool panel's tabs say they overflow as a document strip's do, in every theme: a ▾ listing every tab, and the tabs
  fading out at each edge they went past - along the bottom while docked, down the column once folded against a side.
- `TabStripScroller.EdgeFadeExtent`: tabs that run past an edge fade out over this many pixels, through the strip's own
  opacity mask - only while something is past an edge, so a strip that fits stays an ordinary element. Every theme's
  tab strips fade this way instead of laying a gradient of the window's color over the tabs, which left a visible band
  wherever the strip lay on another ground (a folded panel's tinted strip). Graphite fades over `TabEdgeFadeExtent`.
  A folded panel's tint lies under the strip, not in it, so only the tabs fade and the tint runs on to the ▾.
- More of Linux, on Wayland and the desktop's portals:
  - click-through windows (`TransparentToInput`), as an empty input region;
  - `Hide` unmaps a window and `Show` maps it again; nothing is drawn into it meanwhile;
  - `Activate` through `xdg_activation`, with the token a launcher passes in `XDG_ACTIVATION_TOKEN` for the first
    window; without recent input of ours the compositor may only mark the window as wanting attention;
  - the primary selection: `Clipboard.PrimarySelection` (`IPrimarySelection`), null where the platform has none. Text
    boxes give it what is selected in them, the password box excepted, and a middle click pastes it where it lands;
  - `PlatformSettings.Screens` and `VirtualScreen` from `wl_output` and `xdg_output`, as monitors come and go;
  - the pointer held still with its moves as deltas (`SetRelativeMouseMode`), through pointer constraints and relative
    pointer;
  - dead keys and the Compose key, by the user's locale;
  - custom cursors (`new Cursor(path)`) from .cur, .ico and PNG files, shown 32 logical pixels across as on Windows;
  - the desktop's interface font as the themes' default (`SystemAppearance.FontFamily`), and its light or dark
    appearance followed as it changes, from the Settings portal;
  - a touchpad's pinch as the wheel with Ctrl held, as Windows hands it on;
  - open and save dialogs (`FileDialog`) through the FileChooser portal, modal to their window;
  - pens and erasers of a drawing tablet (`tablet-v2`) as pen pointers, with pressure, tilt, twist and the barrel
    button;
  - windows that remember their place (`RemembersPlacement`) get it back from the compositor by their
    `WindowBase.PlacementName()`, through `xdg_session_management`;
  - spelling through Enchant, with the user's own word list shared by every application;
  - input methods such as IBus through `text-input-v3`: text being composed as a composition, chosen text as text;
  - gamepads through the Linux input devices.
- `PlatformSettings.CanPlaceWindows` (`INativePlatformSettings.CanPlaceWindows`): whether a window can be put at a point
  of the desktop - false on Wayland, where the docking compass is then drawn inside the area it covers.
- Automation: `window <selector> hide | show`; `state` tells the default font and the monitors; `theme --variant System`
  follows the desktop's appearance.
- `RangeSlider` with the keyboard: the thumbs of both bounds take the focus (Tab goes from one to the other) in every
  theme, and the arrows move the bound whose thumb has it by `SmallChange`, Page Up and Page Down by `LargeChange`, Home
  and End to the ends of the scale, as far as the other bound lets it.
- `Slider.FillOrigin`: the value the accent fill runs from to the thumb, to either side of it - a default or a zero in
  the middle of the scale; unset, the fill runs from the start of the scale as before.

- Touch on Wayland (`wl_touch`): every finger is a pointer of its own (`PointerType.Touch`), its contact area from the
  compositor's shape, raised when the compositor closes its frame - or once the events read are dispatched, where it
  closes none. A finger on the edge of a window drawing its own frame resizes it, as the mouse does.
- The automation's input through the system (`--os`) on GNOME's Wayland: fingers go through
  `org.gnome.Mutter.RemoteDesktop`. A Wayland client is not told where its window is, so the pointer is moved over
  the monitors - clear of their edges and corners - until the window says it is under it, and every landing is checked
  the same way right before a finger goes down; a held mouse button, which keeps the pointer on our window wherever
  it is, refuses the input. The session stops after 15 idle seconds. Pens are refused: GNOME's remote desktop has none.
- `Keyboard.HasRightToLeftLayout` (`INativeKeyboard.HasRightToLeftLayout`): whether a keyboard layout the user switches
  between types a right-to-left script, as Hebrew or Arabic - for keys an editor gives to text direction only where
  such text is typed. Windows reads the installed layouts live; Wayland reads the letter keys of each layout in the
  keymap the compositor sends; macOS has no keyboard platform yet, so it says false there.
- Windows on Linux, on a Wayland compositor (`Platforms/Wayland`). A window is a `wl_surface` in the `xdg_toplevel`
  role, drawn by its Vulkan swapchain: it takes the size and state the compositor configures, draws at the scale it
  prefers - fractional ones too, through a viewport - and is resized from its edges and moved by its title bar by the
  compositor (`xdg_toplevel.resize`/`move`) when it draws its own frame. The pointer, the wheel (in 120ths of a notch,
  as on Windows; a touchpad's pixels taken as notches) and the keyboard reach the window as on Windows: keys through
  xkbcommon, letters following the layout and the rest their place, typed text, and repeats timed by the client at the
  rate the compositor gives. Cursors are the compositor's, by shape (`wp_cursor_shape_v1`). The loop waits on the
  compositor's socket and an eventfd, spinning nowhere. A Wayland client places no window and learns no desktop
  position, so a window's client corner is the desktop's origin, and `SetPosition` and `Topmost` do nothing there.
- The clipboard on Wayland, through `wl_data_device`: text, HTML and RTF as UTF-8 under every name they travel by
  (XWayland's among them), files as a URI list, and formats of one's own under `application/x-adamantium-<name>`. The
  bytes go through a pipe when another client asks for a type; what the application put on the clipboard itself is read
  from memory, and a read waits for another client a second at most.
- Drag and drop on Wayland, through the compositor (`start_drag`): a Wayland client is not told which window the
  pointer is over while a button is held, so every mouse drag goes to the compositor, which moves the drag's icon - the
  ghost - above every window and tells the window under the pointer. A drag that stays in the application offers a type
  of its own only, which no other client takes, and arrives on our windows with its live payload; one that may leave
  (`AllowExternalDrag`) offers what it carries as Windows does - files, the picture under its own encoding (or as a
  BMP), HTML, RTF, formats of one's own, text last - each made only when a target asks for it. Drops from other
  applications bring files, pictures, HTML, RTF and text, read only for the drop that takes them. The effect the target
  settles on is asked for again while the pointer rests, so the compositor shows it and takes the drop. The cursor
  during such a drag is the compositor's: `DragDropEventArgs.DragCursor` does not apply, as for a drag between
  applications on Windows. A drag the application carries itself shows its ghost as a subsurface of the window.
- `IWindow.SurfaceDisplayHandle`: the connection a window's surface belongs to where the platform needs one beside it -
  the `wl_display` - passed on to the presenter as `PresentationParameters.DisplayHandle`.
- `TiledPanel`: lays its children out as tiles in a tree of regions (`Layout`), built in markup or code:
  `TileSplit` cuts its room along `Orientation` into parts by `Shares` ("60,40"), `Tile` holds one child - the one whose
  attached `TiledPanel.Tile` is its `Name`, else the next in order - and `TileFlow` fills the children left over left
  to right, in as many columns as gives them the most room, within `MaxColumns` × `MaxRows`, keeping `AspectRatio`,
  with the short last row where `LastRowAlignment` says. A child nothing takes is collapsed; `MaximizedItem` - the
  child or its data - fills the panel alone. The gaps of the splits (`Spacing`) drag, gaps in line moving together, a
  double click shares a split evenly again and `ShareEvenly` every split. `TileLayouts` has the usual layouts: the
  viewport layouts of 3D editors, a gallery, and a featured tile with the rest in a strip on any side. Hosting a
  list's items, a tile drags to another place: it leaves its own, a gap opens where it would land and the other
  tiles slide and resize into their new places. With `SplitTileCommand` a tile's corner dragged inward splits a new
  tile off it - `TileGrip`s mark the corners of the tile the pointer is over, drawn by the theme, and the view model
  gives the new tile its item at the index the command is handed - and with `MergeTileCommand` a
  border dragged all the way over a tile merges it away once the view model lets its item go. A corner dragged back
  to the edge it splits from takes the new tile away again, and letting go there splits nothing. Escape in the middle of
  a drag of a border or a corner gives the layout back as it was and asks the view model nothing.
- `DragDrop.GhostAnchor`: where a source's ghost stands against the cursor - beside it (`Cursor`, the default) or held by
  the point it was grabbed at (`Grab`), as a large tile is carried.
- `IDropGapPanel`: an items host that opens a real gap where a dragged item would drop instead of the insertion caret;
  `VirtualizingPanel` and `TiledPanel` both are one, and the drag works with any - in a `ListBox`, or in any
  `ItemsControl` that allows the drop itself.
- `Expander.ExpandDuration`: the content unfolds in the layout and folds away over it - what lies past the expander
  moves with it - and the chevron turns, one glyph turned by the direction instead of four swapped. The Fluent, Graphite
  and macOS themes set 0.2 s; zero shows the change at once. A template unfolds the content with `RevealDecorator`
  (`PART_Reveal`), which takes `Progress` of its child's size along the axis it opens on, and turns the chevron with a
  `Transform` named `PART_GlyphTurn`.
- `UniverseHost.Output`: the output the universe draws into the host's panel through, for a view model to tell
  one panel's view of a universe from another's (`Mode=OneWayToSource`).
- `PropertyGrid.ValueChangedCommand`: executed after each value written or taken back, with the change as its
  parameter - what `ValueChanged` says, for a view model bound from markup.
- A way in for debugging tools: `UIApplication.AttachDebugTool` starts an `IDebugTool` on the application - its
  windows, the frame verifier, the routed events - and `DetachDebugTool` or shutdown stops it. A tool watches a routed
  event with `RoutedEvent.AddObserver(IRoutedEventObserver)`: it is told every raise (even one nothing hears), every
  handler on the route - where, class or own, run or skipped as already handled, handled before and after, how long it
  took, what it threw - and the end of the route, also when a handler threw. What an observer throws stops nothing
  (the first fault of each kind is logged), nor does what a tool's `Detach` throws at shutdown. Once shutdown has
  begun, attaching throws. An event no observer watches pays one check for it.
  `EventManager.RoutedEventRegistered` announces events registered after a tool started. The sandbox attaches the
  first such tool, a trace of every routed event to a file, when `ADAM_EVENT_TRACE` names the file.
- `UIComponent.ResolveTextComposition(last)`: the element's hyphens, spacing, justification and CJK punctuation
  settings as one engine `TextComposition`, the last one given back while they hold. `TextBlock` and `TextBox` lay
  their text out with it and tell a change by it.
- `Ribbon.RowAlignment`: the commands stacked in a group's column stand from the top, as in Word (`Top`, the default),
  or spread over the group's height, the last at the bottom (`Spread`). Inherited - set on the ribbon or on one group.
- `DropDown.IsEditable`: a value is typed into the header, as Word's font and size boxes take one. Enter picks the item
  that reads the same, ignoring case, or runs `Command` with the text as a value of the items' type - a size no item
  has; Escape, or leaving the box, gives back what was shown, and its automation value is the text.
- `ColorPalette.ColorPlaces` and `HasRecentColors`, and `ColorPaletteSwatch.SelectedColor` and `IsSelected`: the swatch
  of the palette's selected color is ringed, in the grid and in the recent row, and the recent row with its title is
  shown only once a color has been applied through "More colors".
- `DropDownButton`, `SplitButton` and `ToggleSplitButton`: buttons that drop a list of their items, as `DropDown`
  does - `ItemsControl`s, the items written in place as `DropDownButtonItem`s or made for `ItemsSource` and shown by
  `ItemTemplate` - but a picked item is an action, not a value kept: it runs its own command, then `ItemCommand` with
  the item, and raises `ItemClick`; a row with no command of its own that `ItemCommand` cannot run for is drawn
  disabled and cannot be picked (`DropDownButtonItem.IsAvailable`) - its own `IsEnabled` is left to its author. Rows
  written in place take the button's data. A disabled button closes its list and does not open it. A press on the
  button, Enter, Space, Alt+Down and F4 open the list, the arrows walk it past disabled rows and Enter picks; Escape,
  Alt+Up, a pick and a press elsewhere close it. A split button's body (`Command`, `Click`) is pressed by Enter and
  Space, its arrow (`PART_DropDownArea`) drops the list, each half lit on its own (`IsPointerOverDropDown`); a body
  whose command cannot run is disabled alone (`IsBodyEnabled`) and the list stays reachable. A toggle split button's
  body stays down (`IsChecked`, two-way) and shows what a routed command's target answers. Automation: a button that
  expands, a split button that is also invoked, a toggle split button that also toggles; the rows, while the list is
  open, are menu items. Styles in Fluent, Graphite and macOS; the sandbox's "Buttons" tab has all three.
- A key binding runs from anywhere in its window, not only while the focus is in its element. The whole way up from the
  focus has the key first - the window's default and cancel buttons and the Tab and arrow navigation too - then the
  binding nearest to the focus whose element is shown and enabled; Esc clears the focus only when no binding takes it.
  A field being typed in (`ITextEntry`) keeps the keys that type, a key held down repeats only where the focus is, and
  a key pressed in an overlay window stays in it. `KeyBinding` is set in markup with its command bound to the element's
  data - `<View.InputBindings><KeyBinding Gesture="Escape" Command="{Binding Cancel}"/></View.InputBindings>` - and a
  hosted universe, which takes every key, lets one go that a key binding would run (`CommandManager.IsBoundInWindow`).
- `RibbonGroup.CollapsedHeader`: the header as the collapsed button shows it - a name of several words on two lines,
  broken at the space nearest its middle, as Office labels a collapsed group.
- A `RibbonGallery` that runs its choices (`ItemCommand`) lights the one its command answers checked for - the style
  the text has in a gallery of styles - in the band and in the drop-down, following the command as it is asked again.

- `CommandManager.CommandTarget`, attached and inherited: the commands of every source under it that names no target
  of its own go there when nothing on their way takes them - no field has the focus, or the one that has knows nothing
  of them. A ribbon that acts on a document beside it is enabled before anything had the focus and stays so when
  another field takes it, while a field that handles a command (a search box in the document) still gets it first.
  What the target answers reaches the ribbon's buttons without the focus on it, and a command it ran leaves the focus
  in it.
- `RibbonGallery.GroupMemberPath` and `GroupHeaderTemplate`: the items of `ItemsSource` are shown gathered by group -
  the groups in the order they first appear, an item that comes later joining its own - in the band and in the
  drop-down, which titles each group with a `RibbonGalleryGroupHeader`, a row of its own across the cells that Up and
  Down step over. The sandbox's Materials gallery groups metals, stones and natural ones.
- `TreeView.SelectedItems`: every node selected, kept in step with a view-model's list both ways - the tree writes
  into it what is clicked (Ctrl adding, Shift a run), and selects what is written into it, a node in a closed branch
  when its row comes. A node of the list written into `SelectedItem` becomes the one chosen last without letting the
  rest go.
- A `PropertySection` written for a `DataType` takes every object of that type in `SectionsSource`
  (`PropertySection.Targets`), and its rows edit them together, as with `SelectedObjects`: the components of several
  selected entities in one inspector.
- The language server goes where a value points: from `{Localize Table.Key}` to the phrase in the table's base language
  file, from the table to the file, and from the member of `{x:Static Type.Member}` to its declaration. Rider plugin
  1.11.5, VS Code extension 0.3.4.
- `PropertySection.Target` and `Targets` say when they change, so a section can bind to the object it is pointed at -
  its folding to the object's own, `IsExpanded="{Self Target.IsExpanded, Mode=TwoWay}"`, though one section serves
  every object of its type.
- The designer selects a property grid's line, taking it to its definition in markup: a click on a line selected the
  whole section, the line being the grid's own element with no markup of its own. `Design.StandsFor` records what such
  an element shows of the markup, `Design.AuthoredOf` reads it; outside the designer they keep nothing.
- `Calendar`, with WPF's API: the days of a month, the months of a year and the years of a decade (`DisplayMode`), the
  header going up a level and the arrows back and on; `SelectionMode` (one date, one run, several runs or none) with
  `SelectedDate`/`SelectedDates`, `BlackoutDates`, `DisplayDateStart`/`End`, `IsTodayHighlighted` and
  `ShowWeekNumbers`. Days are selected by click, Ctrl, Shift and a drag; the keyboard moves through them, its cursor
  shown only once the keyboard is in use. Month and day names, the first day of the week (or `FirstDayOfWeek`) and
  the week numbers follow the calendar's `Language`. Given a size, its cells share the room - no `Viewbox` needed -
  and it keeps its size from level to level. Unlike WPF, a `SelectedDate` it cannot select goes back to the day
  selected rather than throwing, and blackout days or a range that come to cover selected days let them go. It is in
  the Fluent, Graphite and macOS themes, reaches automation as a grid of selectable dates, and has a "Date and time"
  tab in the sandbox.
  - The page slides up or down to the next or previous month, year or decade, and zooms in or out between levels, as
    WinUI's does - played by the compositor, `TransitionDuration` long (0 moves at once).
  - `MonthsShown` stands several months side by side, each named over it; the keyboard and a drag run across them,
    and automation sees one grid of their days.
  - `CalendarSystem` counts months and years by another calendar - the language's own by default (Hijri for Saudi
    Arabic, Persian for Farsi, Buddhist years for Thai), or Gregorian, Hijri, Um al-Qura, Persian, Hebrew (thirteen
    months in a leap year), Japanese eras, Korean or Taiwanese, named by the language where it knows that calendar.
  - `MarkedDates` (`CalendarDateMark`: a run of days and the classes they wear) marks events and holidays - a dot in
    the themes, and `CalendarDayButton.IsMarked` and the classes for a style; `CalendarDayButtonStyle` and
    `CalendarButtonStyle` add a style to every day or month and year cell, and `CalendarDayButton.DayOfWeek` lets one
    pick out weekends.
- `DateTimeField`: a date, a time or both entered part by part (`DateTimeFieldSegment`) in the order and form of its
  `Language` - `Format` (short or long date, short or long time, short date and time) or a .NET pattern of its own in
  `CustomFormat`, counted by a `CalendarSystem` as the calendar's, Japanese eras a part of their own. Digits fill a part
  and move on once no other digit can follow; the up and down arrows step a part round within itself (the 31st to the
  1st in the same month); the left and right arrows, Tab and a separator move between parts; Backspace and Delete empty
  one; a letter picks a named month or AM/PM. `Value` is null until every part is entered, and empty parts show their
  placeholders. A click anywhere on it puts the keyboard on the nearest part. In the Fluent, Graphite and macOS themes as
  their text field; automation sees an edit of spinners. The sandbox's "Date and time" tab has a field of each format.
- `Clock`: one clock, with hands or in figures (`ClockStyle`) - hands over a face marked Classic, Minimal, Roman or
  with Dots, or figures as Text (the language's own), SevenSegment or Flip cards, the colon blinking. It shows the time
  now (`IsLive`, in a `TimeZone`) or a bound `Time` (nothing while that is null); `ShowSeconds`, and a second hand that
  ticks or sweeps (`SecondHandMotion`). The hands are turned by the compositor; it is drawn to whatever size it is
  given, its face round in any. Its face, `ClockFace`, is the time picker's dial too. Automation reads the time it
  shows. It runs in the designer's live preview too.
- `CountdownTimer` and `StopwatchTimer`: a countdown of a `Duration` and a stopwatch, on a `Clock` in any of its
  styles - with hands, which a countdown turns back round a face ringed at its rim as far as is still left of the whole
  (with no second hand), or in figures counting hours past a day, a digit more past a hundred. Standing at its whole
  length (`IsAtStart` - not started, or reset) a countdown's `Duration` is set in place (`ShowDurationField`, on by
  default): a face with hands gives way to a frameless `DurationField` in figures as large as the room takes, the
  part being typed marked by its figures alone (`FieldSegment.Marking`); figures stay the clock's own, a press on them
  picks the part pressed, they take what is typed into them as it is typed and light the part being set
  (`Clock.MarkedParts`, `DurationField.ActivePart` and `ShownValue`, `CountdownTimer.IsAnalog`). Without seconds shown
  there are none to type; a length typed from nothing lets it start. A countdown rounds up - to the minute when it shows no seconds - and its
  colon blinks with its figures. Started, paused
  and reset (and lapped) by the buttons they show (`ShowButtons`; the keyboard passes to the button that takes the
  pressed one's place), by their commands (`StartCommand`, `PauseCommand`, `ResetCommand`, `LapCommand`) or their
  methods; `Remaining` and `Elapsed` are told every second while shown and `IsRunning` as it changes. A countdown of a
  `Duration` from zero to a hundred years keeps counting off the tree - one on a hidden page still ends on time, woken
  at its end (or once a day) rather than every second, and tells `Remaining` again once shown - stops at zero, raises
  `Completed` and runs `CompletedCommand`, a pause at zero included; one discarded stops. A stopwatch's `Laps`
  (`StopwatchLap`: number, lap time and total, as text to the hundredth too, in the language of the moment) are listed
  under it, the latest first, once there are any (`HasLaps`) and while `ShowLaps`, which shows the lap button too.
  Automation reads what is left and what has passed in whole seconds. The sandbox's "Date and time" tab has both.
- `DatePicker`, `TimePicker` and `DateTimePicker`: the same field with a button at its end (Alt+Down and F4 too) that
  opens a calendar, the times of a day, or both side by side. A day picked keeps the time; a time picked keeps the day
  and closes. The times come as a list a `TimeInterval` apart (15 minutes by default), as columns of hours, minutes and
  AM/PM, or on a clock face whose hand is dragged round, hours then minutes (`PopupStyle`). `DisplayDateStart`/`End`
  bound the calendar; Escape closes. Automation sees a combo box that expands.
- `DurationField`: a length of time - `TimeSpan` - entered part by part, the days, hours, minutes, seconds and
  milliseconds that `Parts` names (hours, minutes and seconds by default). The largest part shown counts everything above
  it - thirty hours with no days shown is 30 - and the others step round within themselves; parts not shown keep what
  the value had. A minus or a plus anywhere sets the sign while `Minimum` lets it go below zero; `Value` is held between
  `Minimum` and `Maximum`, whoever writes it, and is null until every part is entered. Days are followed by the theme's
  word for them; milliseconds by the decimal separator after seconds, by the time separator otherwise. Its
  parts (`DurationFieldSegment`) and those of `DateTimeField` share `FieldSegment`, their template and their automation
  peer (`FieldSegmentAutomationPeer`, which replaces `DateTimeFieldSegmentAutomationPeer`); automation sees an edit whose
  value is the invariant `c` form. In the Fluent, Graphite and macOS themes as their text field.
- `DataGridDateTimeColumn` and `DataGridDurationColumn`: dates, times or both (`DateTime`, `DateTimeOffset`,
  `DateOnly`, `TimeOnly`) and lengths of time in a table, drawn as text in the column's `Format` (or `Parts`) in the
  application's language - left unset, the format is what the column holds: the short date for a `DateOnly`, the short
  time for a `TimeOnly`, both otherwise - edited in the picker for those parts - a calendar for a date, the times of a day for a time,
  both for both - or in a `DurationField`; a character typed on a cell opens the field with it, Enter keeps a part being
  typed, and an emptied field empties a property that can be empty. What is picked goes back as the type the record
  holds, the record's own offset or kind kept - in a cell, in the strip for a new record and in an inspector alike: the
  write through the property turns the picker's `DateTime` into a `DateOnly`, a `TimeOnly` or a `DateTimeOffset`.
  `DataGridColumn.TextOf` is the value as a column reads it - what a search looks in, what the filter lists and
  compares, what a group is named by (`DataGridGroupHeader.KeyText`) and what a copy carries - and `ValueOfText` the way
  back for a paste and for a cell's automation, keeping what the text does not say from the value it replaces (the time
  under a short date) - text the column cannot read is refused (`AdamantiumProperty.UnsetValue`), not written; both new columns read their values as they draw them - a length's days with the theme's word for
  them (`DataGridDurationColumn.DayUnit`, `DurationProperty.DayUnit`) - automation reads a cell of theirs the same way
  (`DrawsItsText`); a new language translates what a column filter ticked and the values its equal, unequal, greater
  and less conditions compare with - text that reads back as itself, other conditions as typed - groups keep their
  place across it (keyed by what the column shows of the value, not by words of the language, `KeyOf`) and the cells
  redraw in it; a
  theme's new word for days renames what was ticked by the old, and a new `Format` or `Parts` keeps the rows ticked; and
  a filter's "greater than" and "less than" compare lengths and dates rather than text, a date by the parts its column
  shows (`DataGridColumnFilter.Passes` takes how the column reads what was typed, `DataGridColumn.ComparedAs`). The
  new-row strip of an empty table edits in the picker for what its source's rows declare. `EditorIsEmpty` says an editor holds nothing; emptied over a member that cannot be
  empty, the cell keeps what it held. A picker's drop-down closed by a click away from the table ends the edit. A date,
  time or length field too narrow for its parts - in a cell, beside a picker's button - cuts them at its edges and
  slides them along to the part being typed, back to the start once left (`FieldSegmentPanel`, the themes'
  `PART_Segments`); a picker's parts no longer run under its button.
- `DateTimeProperty` and `DurationProperty`: the same in a `PropertyGrid`, the picker or the length field live in the
  row, a read-only row in the line's format; moments at two offsets, or clocks of two kinds, are two values, and a line of objects that disagree
  says so in place of the parts (`DateTimeField.Placeholder`, `DurationField.Placeholder`). A field emptied writes its
  nothing on Enter or once it is left, not on the key that emptied a part. `PropertyDefinition.Meet` tells a line the
  type a row holds before the row asks for its editor. Automation reads a date or a length line in the line's own form
  (`AutomationTextOf`), and text written to one by automation is read in the line's own form first, then as ISO
  (`2026-10-05`, `14:30`, `2026-10-05T14:30` as the clock typed) or in a form of the language - with or without its
  year, with a year of two digits - keeping each part the text does not say - year, month, day, the half of the day,
  seconds - from the value the objects hold (`PropertyDefinition.TryConvert` with the current value), a time alone with
  nothing held being today's; text in no such form is refused rather than guessed at. Text naming its zone
  (`2026-10-05T14:30:00Z`, `+03:00`) is a moment, which each object takes in its own offset or kind. A `DefaultValue`
  written as text is read in no language (`PropertyDefinition.TryReadDefault`), and a reset writes it whole. A moment
  for a default - a `DateTimeOffset`, or a `DateTime` of a kind - is met by the same moment in any offset or kind, a
  clock by the same clock; objects that each hold the default in their own offset or kind are at it, and the row then
  shows the default and an edit is made in its frame (`SameAsDefault`, `IsMetInManyWays`). A day picked in a calendar is a day, not a moment in this machine's zone, and one picked over a
  `DateTimeOffset` never set is in this machine's offset. `DateTimeProperty.Format` left unset is read off what the line
  holds, as the column's is. The inspector built from a type picks them by the property's type: the short date for
  `DateOnly`, the short time for `TimeOnly`, both for `DateTime` and `DateTimeOffset`, a length field for `TimeSpan`. The sandbox's "Date and time"
  tab has length fields, a table and an inspector.
- `Inline.FontFamily`: a run or a span sets the family of its own text, as WPF's `TextElement` does; `ClearOverrides`
  takes it off.
- `DragDropEvents.DragCompleted`: the source of a drag hears how it ended as an event on itself, before its
  `DragCompletedCommand` - Move asks it to remove what it gave; handling it keeps the command from running. A control
  that drags its own content, as a text editor drags its selection, answers for it there.
- A ribbon command its author gave no tip shows its label and its key, as Word's "Bold (Ctrl+B)", read as the tip
  opens; a tip that reads as nothing - a command with an icon alone - opens no card.
  `RibbonSplitButton.IndicatorBrush`: a bar under the icon in what the body applies, as Word's font color and
  highlight show the color picked last.
- `WavyLine` and `TextRange.Follow` are public, for an editor outside the controls to draw its spelling marks and keep
  them on their words through edits, as `TextBox` does.
- The sandbox's text tab has a topic "New in typesetting": CJK punctuation with `TextSpacingTrim` and
  `HangingPunctuation` switched, words hyphenated inside a ligature and at an Arabic soft hyphen, a tall line in
  columns, a vertical field and a control in a vertical line, one text through three frames, 'SVG' glyphs of a font of
  our own styled by a sheet and clipped in their box, and 600 color images of another (`sbix`) scrolled under a budget
  of the color atlas, its layers taken shown.
- `TextBlockOverflow` and `TextBlock.OverflowContentTarget`, as WinUI's RichTextBlockOverflow: the text that does not
  fit into a block goes on in the overflow it names, and on in the one that overflow names, wherever they stand. The
  block lays its text out once, through its own room (its columns included) and then through each overflow's, so a
  paragraph, its hyphenation and justification run on across them, and an overflow's lines sit on the baseline grid as
  its own top would have them; an overflow of unbounded height is as tall as its lines and ends the chain. Each draws,
  selects and clicks its part as the block does, in the block's formatting - the block's background and `TextEffects`
  stay in the block; a control among the inlines shown in an overflow takes its own presses. An overflow shows one
  block's text: a second block reaching it ends its chain before it, and takes it when the first lets go; a collapsed
  overflow, or one out of the tree, ends the chain. `HasOverflowContent` on both, `ContentSource` on the overflow;
  `IsOverset` is the text past the last. Text wrapped by words in a block of bounded height (width, for vertical
  text). The paragraph stand shows one text through three frames.
- `IDrawingSession.DrawText(..., effects, window)` and `TextPayload.Window`: only the glyphs of a layout touching a
  part of it are drawn. A text unit rebuilds its glyphs when the run it holds is stale, not when the layout's last
  build was.
- `UIComponent.OnRenderInvalidated` and `OnPaintInvalidated`, for an element whose content other elements show part of.
- `TextSpacingTrim` and `HangingPunctuation` on every element (inherited), taken by `TextBlock` and `TextBox`, as CSS's
  `text-spacing-trim` and `hanging-punctuation`: fullwidth CJK punctuation gives up its blank half side by side and
  where a line would not fit otherwise (`Normal`, the default), and a CJK comma or full stop hangs past the end of a
  line where it would not fit otherwise (`AllowEnd`, the default).
- `TextBox.WritingMode` (on `TextBoxBase`, so a `PasswordBox` too), as a `TextBlock`'s: lines down the field, stacked
  from the right. The caret lies across its line, a click finds the character shown there, the arrows turn with the
  text - Up and Down along a line, Left to the next one and Right back - a wrapping field wraps at its height and
  scrolls across, opening on its first lines at the right and keeping its place from there as the text grows,
  `MinLines`/`MaxLines` limit its width, and the placeholder and a floated label stand at its right.
  The Vertical text stand of the sandbox has one to edit.
- A vertical `TextBlock` selects, as a horizontal one does: dragged down its lines, and with the arrows turned as in a
  vertical field; it could not be selected at all.
- `FontFamily.Source`, as WPF's: the family's name - the one it was made with, or the name its font file gives.
- `RibbonControlGroup`, as WPF's: a row of ribbon commands side by side - Word's Bold, Italic and Underline, or its
  font and size boxes with the size steps - that stands in its group as one row of the group's columns, its commands
  drawn small. To automation it is a group of them.
- `ColorPalette` and `ColorPaletteSwatch`: colors to pick from, as Word's font color menu holds them - a choice of no
  color (`NoColorText`; none, none shown), a grid of `Colors` (`ColorPalette.StandardColors` by default) and, under
  "More colors", a `ColorPicker` whose color "Apply" takes - and a row of `RecentColors`, as Word's: empty places until
  colors are applied through "More colors", the latest first, one applied there again moved to the front, a pick from
  the row leaving the order as it is, at most
  `RecentCount` (10); a list given to several palettes is shared. A pick runs `Command` with the color, null for no color,
  keeps it as `SelectedColor` - for a split button's body to apply again - and closes the menu it stands in. One style
  set serves every theme.
- The clipboard carries several formats of one thing at once: `Clipboard.SetData` puts an `IDataPackage` on it -
  `DataFormats.Text`, `Html` (the fragment; the Windows header is written and read for you) and `Rtf` as strings,
  formats of one's own as `byte[]` under their name - and `Contains` / `GetData` read one back. `IClipboard` has these
  as new members, so a clipboard of one's own implements them too. The Windows clipboard tries to open again for a
  moment when another application holds it, as one does right after every copy.
- `xml:space="preserve"` in markup, as in WPF: on an element, or one around it, the white space of its text is kept as
  written - in a property element, in content and among inlines; `xml:space="default"` inside undoes it.
- `FocusManager.ReturnsFocus`: a focus scope whose commands give the focus back - after a button, a drop-down, a
  segment or a gallery inside it has run its command, the focus goes back to the element that had it in the scope
  around, unless the command put it somewhere of its own. A ribbon and its quick-access bar do, so after a click on
  Bold the caret and the keys are in the field being edited again, as in Office. Its backstage, menus and drop-down
  lists are scopes of their own, and a toggle with no command keeps the focus.
- `DropDown.Command`: a choice runs it with the chosen item's value, as a font box sets the font of the text being
  edited. A routed command's answer is shown as the choice without running it - a value no item has shows in the
  header as it is, none (the selection is mixed) shows the placeholder - and the drop-down is disabled while the
  command cannot run.

- `CharacterStyle` and `ParagraphStyle` on `TextBlock` and `TextBox` (and `PasswordBox`): named text styles for the
  whole text, its `Text` with no inlines included. What the element sets itself wins over them, and an inline or a
  paragraph with its own style takes that one instead; changing a style lays the text out again, and so does moving the
  element under other resources.
- A control written among inlines in markup is set into the line in an `InlineUIContainer` of its own, as WPF wraps it
  (`<TextBlock>Press <Button/> here</TextBlock>`), in the loader and in the generated code alike;
  `ContentWrapperAttribute.ElementWrapperType` names the wrapper.
- A `TextBox` takes the baseline grid (`BaselineGridStep`) as a `TextBlock` does: its lines stand on the grid from
  where its text surface is, its scroll left out; on the grid a single line is not centered off the grid's rows.
- A `Ruby` inside a `Ruby` on the other side of the base is a double-sided ruby: both annotations are shown, where only
  the outer one was.
- `SegmentedControl` and `SegmentedControlItem`: a few choices of one setting side by side, one of them chosen, as
  macOS's segmented control. Segments are written in markup, made from `ItemsSource` by `ItemTemplate` (each made
  segment's `DataContext` is its item, so `ItemContainerStyle` can bind a segment to its model), or made from an enum by
  `EnumType`. A segment's `Command` runs when it is chosen - clicked, or reached with the arrows, Home or End; choosing
  the chosen one again runs nothing. A segment whose command cannot run is disabled and the keys pass it by. One tab
  stop for the whole control; made not focusable, as in a toolbar, a click leaves the keyboard where it was. A
  disabled control fades whole. To automation it is a list with one selected item. Styled in every theme; the Buttons
  tab of the sandbox shows them.
- `Selector.SelectedValue` and `SelectedValuePath`, as in WPF: the selected item's value - its property named by the
  path, or the item itself - and, set, the item whose value it is selected, also when it is named before the items
  come. A view-model holds a plain value while the items are pictures or models written in markup.
- `SegmentedChoiceProperty` (`PropertyEditors.Segments()`): a property grid row choosing among a few values with a
  `SegmentedControl`; its `EditorTemplate` may bring segments of its own, `ValuePath` naming the value of each. A
  `PropertyRow` now follows any `Selector` editor it shows, not only a `DropDown`. A `ChoiceProperty` finds and reads
  its choice through the selector's `SelectedValuePath`, and its enum's values come as one list while the enum stays.

- `RibbonGallery.ItemCommand`: a gallery that runs its choices rather than holds one, as a gallery of shapes to insert
  does. A picked item - clicked or chosen with Enter or Space, in the band or the drop-down - is passed to the command
  and the drop-down closes; the gallery has no selection (one set on it is dropped), so the same item runs again on the
  next pick. A choice the command cannot run is disabled. To automation its choices are invoked, not selected.

- `TextBlock.TextLineBounds`, as WinUI's: `Full` (the default), `TrimToCapHeight`, `TrimToBaseline` or `Tight` - the
  block measures from the top of the capitals of its first line and to the baseline of its last, so a label centered by
  its layout has its capitals in the middle.
- `DockingArea.PanePadding`: the room between a panel's edge and what it shows, for every panel of the area, without
  touching the theme - `PanePadding="0"` puts a scene or a tree right against the frame. Unset, the theme's stands
  (Fluent's 12 came from the tab control's content padding).
- `DockingArea.ToolSelectionIndicatorPlacement`: which side of a tool panel's tab strip the selection bar runs along,
  over `SelectionIndicatorPlacement`; documents keep theirs. The tool templates of every theme follow the placement
  for a docked panel's strip now - they drew the bar under the tabs whatever it said - and their style keeps it there by
  default; a folded panel keeps its bar facing the body.
- `TabItem.StripPlacement`: which side of its tab control the tab's strip runs along, set by the owner, for a theme to
  round the right corners.

- `HyphenLimit` on every element, inherited, as InDesign's hyphen limit: the most lines in a row of its text that may
  end in a hyphen (0, the default, sets none), in `TextBlock` and `TextBox`.

- `Inline.ClearOverrides()`, as InDesign's Clear Overrides: the character formatting set on an inline and on the
  inlines inside it goes, and the text keeps its styles and what it inherits.

- `TextBlock.IsOversetProperty`: `IsOverset` is set after each layout and binds.

- `PropertySection.DataType`: a section written for a type of object. With `SectionsSource` set, the grid points each
  such section at the first item of its type and leaves it out when there is none, so an inspector of an entity shows
  its components in the order its sections are written, whatever order the entity holds them in. The same section
  serves every object of its type and keeps its folding from one selection to the next. Items no section is written
  for show only with `AutoGenerateSections`; a source changed on another thread - a world adding a component - is
  followed on the grid's own thread. Without a source such sections are left out, except in the designer, which has
  no view model to give one: there they are all shown, empty, so the layout can be seen.

- Named paragraph and character styles, as InDesign's: `ParagraphStyle` (`HorizontalTextAlignment`, `Margin`,
  `TextIndent`, `LineHeight` and the text's formatting) on `Paragraph.ParagraphStyle`, `CharacterStyle` on
  `Inline.CharacterStyle`, each with `BasedOn`, kept as resources. A paragraph takes its style under what it sets itself
  (field by field), an inline its character style over its paragraph's and under what it and the spans around it set
  themselves; the nearest inline's style wins. A hyperlink's underline and theme color yield only to a character style
  on the link or inside it, as in CSS: styles around it, and its paragraph's, add their lines to its underline and
  take nothing away; the lines of nested inlines and of their styles add up, an `<Underline>`'s among them. A
  `<Bold>`'s weight and an `<Italic>`'s slant yield, as a link's look does, to a character style on them or inside them. Changing a
  style, or one it is based on, lays out again the text it is
  set on (styles hold their users weakly). Laid out by the engine's styles. A `{ResourceReference}` on an inline or on
  its styles is answered from its block's place in the tree, and again when its theme scope switches (it was answered
  for no inline before). The Text tab's
  Paragraph topic shows a heading, body paragraphs, an emphasis, and links and bolds against the styles around them and on them.

- `Ruby`, a `Span` with an annotation, as HTML's `<ruby>`: `<Ruby Annotation="かんじ">漢字</Ruby>` sets furigana
  centered over its inlines at half their size (`AnnotationFontSize` sets its own), spreads inlines narrower than the
  annotation to its width and keeps them on one line; `RubyPosition="Under"` puts it under the line. The line grows to
  hold it unless its height is set; the annotation takes the block's `TextTransform`; of rubies one inside another the
  outer one's annotation is shown. The Text tab's OpenType topic shows furigana and an annotation under the line.

- `Kerning` on every element, inherited, and on an inline, as InDesign's: the font's pairs (`Metrics`, the default),
  the letters' outlines (`Optical` - for a font with no pairs, or pairs it left out) or none. In a `TextBlock` and a
  `TextBox`; the Text tab's OpenType topic shows one line three ways.

- A baseline grid on any element, inherited, as InDesign's: `BaselineGridStep` (NaN, the default, is none),
  `BaselineGridOffset` and `BaselineGridFirstLineOnly`. The grid starts at the top of the element its step is set on,
  and every `TextBlock` under it puts its baselines on the grid's lines, so headings, body text and columns of other
  sizes keep one rhythm. A `StackPanel`, a `Border` and a `ScrollContentPresenter` tell a child where it will stand
  while measuring it (`PlaceAt`), so a stack of blocks, the rows of a list and text in a popup are on the grid in the
  pass that lays them out; under other panels a block that moved lays its lines out again once the pass settles. A
  block centered or at the bottom of a taller slot - itself or an element above it - moves with its own height and is
  not put on the grid: it keeps the place it first took. Scrolling moves the grid with the text; a `LayoutTransform`
  between the two is not taken into account. The Text tab's Paragraph topic shows a heading and two columns on one
  grid.

- `VectorToColorConverter`: a color a game keeps as numbers - a `Vector3F` of red, green and blue, or a `Vector4F` with
  alpha - shown as a `Color` and written back as the same kind, so an inspector edits it with the color picker:
  `<ColorProperty Binding="{Binding Color, Converter={VectorToColorConverter}}"/>`.

- `x:Key="{x:Static Type.Member}"`: a resource keyed by a constant code uses too - an entity's icon key - so the key is
  written once, in C#. In a resource file, an inline dictionary, and with `x:Shared="False"`; a member that is not text
  is a build error, and `{}` still starts a key that is braces as written. Hot reload sees an `{x:Static}` changed to
  another member, in a key or a property's value. The assembly names such a key by the constant's value, and the
  language server reads it from the code too: completion offers it, and a use goes to the entry it keys; the
  `{x:Static}` text is not taken for a key to rename.

- `TreeView.ParentPath`: the member a node names its parent by, as an entity's `Owner`. With it, a node written into
  `SelectedItem` from outside - an entity picked in a scene, deep inside a closed model - is revealed: the branches
  above it open, it is selected and scrolled to - also when the tree is rebuilt with that node chosen. Without it, such
  a node is selected only when its row is shown.
- An `ItemsControl`, `TreeView` or `TreeDataGrid` follows a source changed on another thread - a game world adding
  and removing its entities on its own loop: the changes are carried over to the UI thread and applied there in the
  order they came, so no container is built on the world's thread. A source that makes and announces its changes under
  its `SyncRoot` (the engine's `TrackingCollection`) is copied under that lock, so a list or a branch read while the
  world changes it shows each item once; the rows are built from the copy after the lock is let go, so the world never
  waits on them. A change that does not fit what the list holds reads the source again. A `DataPager` over such a
  source is not covered.
- Text effects, as WPF's: `TextBlock.TextEffects` and `Inline.TextEffects` draw a range of the text apart from the rest -
  its characters moved, turned, scaled or slanted by a `Transform` and drawn in their own `Foreground`. Each character
  turns about its own center unless `TransformOrigin="Element"` (WPF's way), the transform's `ShiftX`/`ShiftY` count in
  sizes of each character, or of the element, and a turn about X or Y with a `Perspective` tips the characters as
  flipping cards; the brush's `Opacity` fades the range. `Clip` - a `RectangleGeometry`, its corners rounded and placed
  by its own `Transform` - draws only what of the range is inside it: as in WPF it goes with the characters, and with
  `ClipFollowsTransform="False"` it stays, so they pass through it; of the effects over a character, the last that clips
  it does. A block counts its whole text, an inline from its own start. Only the drawing changes: the lines, the caret,
  selection and hit testing stay. The effects are kept in the renderer's transform table, so a changed one - a frame of
  an animation - is a few slot writes: the text is neither laid out, drawn again nor packed again, and allocates nothing
  of its own. An effect binds against its holder's data; name its transform to animate it, as any;
  `TextEffectResolver` splits one effect into one a character, for letters moved in turn. A text box has none: rich text
  while typing is for a rich text editor.
- Spelling checked in a text box, as WPF's `SpellCheck.IsEnabled`: set on a box or on any element above it, off by
  default. A misspelled word gets a wavy line (`TextBoxBase.MisspellingBrush`), and the box's menu offers, for the word
  under the pointer or at the caret, what it may have been meant as, "Ignore all" and "Add to dictionary". The words
  are the system's (`ISpellChecker`; on Windows its own dictionaries and the user's), in the box's `Language`, else the
  application's language, else English; `SpellCheckBehavior.Checker` takes another. macOS comes later.
- Routed commands, as WPF's: a `RoutedCommand` (`Name`, `OwnerType`, its `KeyGesture`s) goes from its target up
  the tree to the first element whose `CommandBinding` takes it - on the element (in markup among its `Behaviors`, or
  `CommandManager.GetCommandBindings`) or on its whole class (`RegisterClassCommandBinding`) - through
  `PreviewCanExecute`, `CanExecute`, `PreviewExecuted` and `Executed`. Asked, it answers `CanExecute`, and may answer
  `IsChecked` and `Value`. A binding's `Handler` hands the command to a view model's:
  `<CommandBinding Command="{x:Static ApplicationCommands.Copy}" Handler="{Binding CopyCommand}"/>` copies what the view
  model holds whenever no field with the keyboard takes Copy first. Its gestures run it from the keyboard, and
  `KeyBinding`s on an element or a class add keys of their own (`KeyGesture.Parse("Ctrl+Shift+K")`); a key whose
  command an element takes stays with it even when it cannot run now.
  A button, a menu row, a segment or a hyperlink sends its routed command from itself, or from its `CommandTarget`; a
  gallery sends its `ItemCommand` from itself, the picked item as the parameter. A focus scope
  (`FocusManager.IsFocusScope`: a context menu, a popup, a ribbon, its application menu and quick-access bar) keeps a
  focus of its own, per window: a command nothing inside it took goes to the element with the focus in the scope
  around it, so a ribbon's Copy copies from the field being edited, and a box's own menu acts on that box, also in a
  ribbon or a popup. A popup's content is in its popup, and a menu goes on to the element it belongs to. A toggle
  button takes `IsChecked` from the answer, a menu row shows the command's first gesture unless `InputGestureText` is
  set. Sources in the tree ask again when the focus moves, when a control calls
  `CommandManager.InvalidateRequerySuggested`, at most once a frame, and a menu asks as it opens.
- `Clipboard.HasText()`: whether there is text to paste, without reading it. `IClipboard` has it as a new member, so a
  clipboard of one's own implements it too.
- `ApplicationCommands`: Cut, Copy, Paste, Undo, Redo, Select all, Find, Replace, with their usual keys. A text box
  takes Cut, Copy, Paste, Select all, Undo and Redo (its keys are these commands' keys now), a password box gives
  nothing away, and a selectable `TextBlock` takes Copy and Select all.
- A text box has an editing menu in every theme: Cut, Copy, Paste, Select all, its rows on `ApplicationCommands`.
- `ContextMenu.Opening`: raised before a menu shows, with the point it was opened at (none from the keyboard), so its
  rows can fit the moment.
- `TextBox.AcceptsTab`: Tab types a tab; Shift+Tab and Ctrl+Tab still move the focus on, and a read-only box lets Tab
  move it too.
- `TextBox.MinLines` and `MaxLines`: the box is at least and at most that many lines tall; past the most, its text
  scrolls.
- `TextBoxBase.Replace(range, text)` as one edit undo takes back, and `GetCharacterIndexFromPoint`.
- `TextBox.CharacterCasing` (`Normal`, `Lower`, `Upper`), as WPF's: what is typed or pasted takes that case, letter
  by letter as the box's `Language` maps it (else the application's language; a tag it does not know loses subtags
  from the right until it does). Text set from code or a binding keeps its own.
- `TextTransform` (`None`, `Uppercase`, `Lowercase`) on `TextBox` and `TextBlock`, as CSS's `text-transform`: the text
  is shown and copied in that case, by the same language rules, while `Text`, its binding, the inlines and what
  automation reads keep it as it is - a view model's text in capitals with nothing written back. Bound, it follows
  a switch at once, and the application's language as it changes. A combining mark whose capital is a letter of its
  own (U+0345) stays a mark on screen, so the caret never stops inside a character.

- `Paragraph` in a `TextBlock`'s inlines, as HTML's `p`: it starts on lines of its own, and the inlines between
  paragraphs make a paragraph of their own unless they hold nothing but white space. `HorizontalTextAlignment` (null
  takes the block's), `Margin` (left and right keep its lines from the edges, top and bottom are the room above and
  below it - between two paragraphs only the larger is kept), `TextIndent` (the first line's; negative hangs it out)
  and `LineHeight` (NaN takes the block's); its inline formatting goes to its inlines as a span's does. It stands only
  directly in the block: in a span the build reports it, and adding one to a span throws. Its end is copied as a new
  line.

- `IDockablePane` asks only for `PaneTitle`: `PaneId` is the type's name, `PaneZone` the document area and
  `PaneAllowed` anywhere unless the view model says otherwise. A `PaneZone` the view model declares with a setter still
  follows a drag both ways. A docking region reads the titles of its panes again when the application's language
  changes, so `PaneTitle => EditorStrings.Scene` keeps the tab in the current language with nothing to raise.
- `IDockingRegion.OpenAsync<T>()` opens the one pane of a type, or brings the open one to the front - for a scene, an
  inspector or anything else there is one of, with no key to make up. `OpenDocumentAsync` keeps the key for documents
  there are many of, such as files.

- Text in a `TextBlock` selects. `TextBlock.IsTextSelectionEnabled` is on by default, with the I-beam over the text;
  off protects the text from being selected and copied and shows the inherited cursor.
  - **By hand:** a drag selects what it passes over, a double click a word, a triple click the whole text, Shift with a
    click extends the selection; a plain click lets it go. As on a page, a press on other text or on the background
    lets it go too, while a right click, a press on a control where text does not select (a button, a list row) and
    the keyboard moving elsewhere keep it; switching to another window and back keeps both the selection and the
    keyboard. Rewritten text - a live diagnostics line - keeps its selection in place (a whole-text one
    stays whole), and a copy takes the text as it is now; new data (a changed `DataContext`, as in a reused list
    container) lets it go. A drag in right-to-left text selects by the screen, as a text box does; a word, `Select` and
    `SelectAll` are ranges of the text.
  - **Keyboard:** Ctrl+A, Ctrl+C; Shift with the arrows, Home, End and Ctrl+arrows moves the selection's end, back
    through where it began and on; Left and Right collapse the selection to its edge, and a click in text that has
    the keyboard sets where Shift+arrows start. Escape is left to the control around the text. No caret is drawn, and
    text selected with the mouse wears no focus ring - a control says so with the new
    `InputUIComponent.ShowsFocusRing`; `FocusManager.IsLeavingWindow` tells a control its focus will come back.
  - **From code:** `SelectedText` (a line break copied as a new line, a control in the line left out), `SelectAll`,
    `Select(start, length)`, `SelectionBrush`.
  - **Never the press of the control around it:** a button or a tab that takes the press keeps it. The property is
    inherited, and the themes turn it off for captions a press already means something on - buttons, check boxes,
    list and tree rows, menu items, table rows and headers, the ribbon's buttons, the title bar, canvas nodes - and in
    the headers of tabs, expanders, docked panes and overlay windows, while a tab's page, an expander's content, a
    pane's content and a table row's details panel still select.
  - Vertical text does not select yet.
- `PropertyMetadataOptions.InheritsFromTypeStyle`: with `Inherits`, a value from a style chosen by type alone reaches the
  subtree as any style's does. For a property by which a control decides for everything inside it; looks such as fonts
  and colors keep the element's own style winning over its container's.
- `PasswordBox`: a text field that shows a mask and edits like a `TextBox`.
  - **Binds:** `Password` is an ordinary property, two-way by default - unlike WPF, a view model can hold it.
  - **Placeholders:** the text box's `Placeholder` and `FloatingPlaceholder` work as they do there; all three themes
    dress it as their text box.
  - **Mask:** `PasswordChar` (`●` by default), one per character a reader sees - an emoji or a letter with its accent
    is one, and the caret never stops inside it.
  - **Reveal button:** `PasswordRevealMode` - `Peek` (the default) shows the password while the eye is held (mouse,
    pen or finger), `Toggle` until the next click, `Hidden` offers no button and leaves `IsPasswordRevealed` to the
    application, for a "show password" control of its own.
  - **Never leaves the box:** copy and cut do nothing, a paste takes the first line without control characters, there
    is no undo history, Ctrl+arrows go to the ends and a double click takes it all. While it has the keyboard the
    window's input method is off (`IWindow.SetInputMethodEnabled`; on Windows; macOS to come), so no composition window
    shows the password in clear; it comes back on when the box loses the keyboard or leaves the window. Automation reports a password field (`AutomationPeer.IsPassword`, UIA `IsPassword`) with no
    value, and `adam-auto inspect` hides it; a value can still be set, so a test can type one.
- Markup hot reload. Save an `.auml` while a debug build of the application runs, and every view, window and control
  built from it takes the change in place at the next frame: changed properties are set again, children are added,
  removed and reordered, and everything that did not change keeps its instance and its state. A version that does not
  build is logged and the running one stays; a view opened after an edit opens with it.
  - **Switch.** The build property `AdamantiumAumlHotReload` is `true` in a Debug build unless the project sets it.
    Generated code then reports each instance it builds through `MarkupHotReload.Track`; a Release build carries
    nothing of it.
  - **API.** `MarkupHotReload` (`Adamantium.UI.Core.Markup`): `Mode` (`OnSave` or `Manual`), `ChangedFiles`,
    `NotifyChanged`, `ApplyAsync` with a `MarkupReloadReport` per file, the events `Reloaded` and `Failed`.
  - **The IDE's apply button.** Rider's Apply Changes, Visual Studio's Hot Reload and `dotnet watch` apply the saved
    markup too, in either mode, and have every window measured and drawn again so that changed control code shows at
    once. A view the updated code builds is not edited a second time.
  - **Themes too.** A style set, a theme variant or a resource dictionary is built again from the saved file, and the
    current theme takes it: its styles are merged anew, its variant repaints the palette brushes, resource references
    resolve again, and every window is restyled - without the theme swap's overlay.
  - **Not applied in place:** a changed root element, a theme's own file (its list of style sets and variants),
    children of a style or a trigger inside a view, and children behind `x:Load`. They are reported; restart to see
    them. A file changed after the build is not followed until the application is rebuilt.

- `RenderTargetPanel.AcceptsTab`, `true` by default: Tab and Shift+Tab typed into the panel are the hosted universe's;
  Ctrl+Tab leaves the panel - to the next focus area, or to the next control when there is none.

- A universe made and owned by a view-model, started off the UI thread.
  - **Making it.** `IUniverseService.CreateUniverseAsync` constructs and initializes a universe on a worker thread; it
    runs in no window's frames until it is shown. A canceled or failed start shuts the universe down.
  - **Showing it.** `UniverseHost`, a behavior of `RenderTargetPanel` (`Adamantium.UI.Universes`), shows the universe
    bound to its `Universe` in the panel: `<universes:UniverseHost Universe="{Binding Scene}"/>`. The universe runs in
    the frames of the panel's window and is paused while the panel is out of the tree; a discarded panel's output is
    taken out and freed. The host resumes only a universe it paused itself, so a game paused by the application stays
    paused across a tab switched away and back. `IUniverseService.Show` runs a universe in a window's frames.
  - The sandbox's Scene tab starts its universe with the application, so the tab opens at once.

- Automation scenarios as tests. A scenario is an XML file, `*.ascenario` (`http://adamantium/automation/scenario`):
  steps such as `<Invoke Target="id=Save"/>`, `<Expect Target="id=Status" Name="Saved."/>` or
  `<Pen Target="id=Canvas" Gesture="Drag" From="400,150" To="700,150" Pressure="0.2..1"/>`.
  - **Tests.** The build of a test project turns each scenario into a test of the fixture `Scenarios`, named after the
    file or the root's `Name`, with the root's `Description`. Each step becomes a plain call of `AutomationSession` and
    `AutomationElement`. Selector paths are read at build time and written as `By` chains, so nothing is parsed at run
    time. The test starts the application named in the project's `AdamantiumScenarioApplication` afresh
    (`LaunchOptions.FreshData`) and closes it again. `#line` maps each step to its scenario line, so a failure's stack
    trace, and a compile error in a step, point at the `.ascenario` line.
  - **Checks at build.** The build checks the schema, every selector path and steps that cannot run (a drag without
    its points, an `Expect` with nothing to check). It also refuses two scenarios of one name, a project without the
    application, and scenarios kept outside a test project. Each is an error at the file's line (`AUS000`-`AUS010`).
  - **Where the tests run.** They are in the category `Scenario` and run one at a time. They need a desktop and a GPU,
    so CI leaves them out: run them with `dotnet test --filter TestCategory=Scenario`. The sandbox's 40 scenarios
    are in `Tests/Adamantium.UI.Sandbox.Tests/Scenarios`.
  - **One application through them all.** With `ADAM_SCENARIO_PIPE` set to the pipe of an application already
    running, every test runs its steps in it rather than starting its own, and leaves it running - so one application
    can be taken through every scenario, again and again, and measured between the passes. Each test closes the
    windows it opened, failed or not (`AutomationSession.CloseWindowsOpenedFromNowAsync`).
  - **Explicit scenarios.** A scenario whose root carries `Explicit="<why>"` runs only when asked for by name. The
    sandbox's `TouchThroughSystem` is one: it drives the system's real pointer input.
  - **Editor support.** The language server completes a scenario's steps, their attributes and values, a selector
    path's keys, and the AutomationIds the markup of the referenced projects declares. Hovering a step or attribute
    shows its description, and the build's problems are shown as the file is typed. The Rider plugin (1.11.0) and the
    VS Code extension (0.3.0) open `.ascenario` files, and Rider's New | Scenario creates one. The uitest template
    ships a scenario.
- What an automation test checks, in the typed API:
  - `AutomationElement.ExpectAsync`, `WaitUntilAsync` with `Expectation`s (an element key such as `name`, `value` or
    `toggle`, or a control property by name), `ExpectAbsentAsync`, `TreeAsync` and `FindAllAsync`;
  - `AutomationSession.Find(string)` for a selector path written as text, plus `PressKeysAsync`, `TypeAsync`,
    `ShotAsync` and `UnnamedAsync`;
  - `PenDragAsync` with pressure that changes along the stroke.
- `ApplicationData.Folder`: the one folder where an application keeps what it remembers between runs. It defaults to
  the user's local application data, under the application's name, and the environment variable
  `ADAMANTIUM_APP_DATA` moves it for one run. `LaunchOptions.FreshData` starts an application with a fresh folder of
  its own and deletes the folder afterwards, so one run leaves nothing behind for the next.

- The language server completes a picture of a content project the markup's project references under
  `Content/{Name}/`, where the build puts it: `Content/` offers the content projects by name, and each its folders and
  files.

- Leak hunting in automation.
  - **`adam-auto memory`** (`AutomationSession.MemoryAsync`) collects all garbage in the application, finalizers
    included, then reports the managed heap left, the committed heap, private memory and handles.
  - **`adam-auto theme [<name>] [--variant <name>]`** (`AutomationSession.ThemeAsync`) moves the application onto a
    theme and variant and waits for the swap to settle. Alone, it says which theme and variant are in use and lists
    every registered theme with its variants.
  - **`adam-auto sweep --themes --memory`**: after each pass over the tabs, every theme in every variant and back to the
    one in use, and a memory reading - so a pass tears every template down and builds it again, and the readings show
    what each pass leaves behind. With `--memory`, a reading is also taken before the first pass.
  - The sandbox's theme chooser, Switch and Dark / Light buttons carry automation ids (`ThemeChooser`, `SwitchTheme`,
    `ToggleVariant`).

- The pen and several fingers on `InfiniteCanvas`.
  - **Pressure.** A pen's pressure sets the ink's width point by point along the stroke: a pen's usual pressure (and
    a pen with no pressure to report) draws `InkThickness`, lighter narrower, harder up to twice as wide; never under a
    pixel. `InkBrush.Widths` gives a width per point, and the ink pass draws each segment as a capsule that narrows
    from one end to the other, each end its own round cap. A mouse and a finger draw at full width: a touch screen
    rarely knows a finger's pressure. Smoothing a finished stroke keeps a point where the width changes and carries
    the width between points; erasing keeps the pieces' widths; a saved drawing keeps them (`data-adm-pressure`).
  - **The eraser end.** A pen held upside down erases, whatever tool is in hand.
  - **Several fingers.** A tool that lays ink (`ICanvasTool.IsInk`) takes every finger and pen at once, a stroke for
    each, told apart by `CanvasPointerEventArgs.PointerId`. Fingers moving side by side draw: on a canvas inking,
    only a pinch makes a gesture of them (`Gestures.ManipulationModes`). A pinch takes back the strokes of its own
    fingers (`ICanvasTool.CancelPointer`), and a pen drawing beside them goes on. Fingers down together are one undo
    step.
  - **Two people at once.** A finger already drawing goes on drawing when another lands, so two people drawing apart
    do not pinch each other (`Gestures.StartsFromStillFingers`, on while a canvas inks): only fingers that land
    still make a pinch.
  - **`InfiniteCanvas.FingerInk`.** `Draw` (the default) lets fingers ink beside a pen, for several people at a large
    screen. `WhenNoPen` lets them ink only while no pen is in reach: while the pen writes, a finger is ignored - a
    hand resting on the screen leaves no mark and moves nothing - and while it only hovers, a finger moves the plane.
    `Never` keeps fingers off the ink, and they move the plane. The plane follows only the pointer that took it.
  - **`CanvasPointerEventArgs`** carries the pointer's id, kind, pressure and eraser.
  - **Automation.** `pen <selector> drag ... --pressure a..b` presses from `a` to `b` along the drag.

- `Gestures.ManipulationModes`: what a gesture on an element may start as. Until one of those is recognized, the
  fingers stay plain pointers.

- `UIComponent.OpacityMask`: a brush whose alpha fades the element and its subtree point by point, laid over the
  element's own box, so a gradient from transparent to opaque melts an edge away. The element is drawn as a layer
  whether `IsLayer` is set or not, and the mask scales the layer as it is laid down.
  - **Brushes.** A solid color masks by its alpha. Linear, radial, conic and four-corner gradients mask by the alpha of
    their stops, with their spread method. Any other brush masks by its `Opacity` alone.
  - **Changes.** A mask changed in place, a stop recolored or animated for example, shows on the next frame.
  - **The stand.** The sandbox's Opacity page has a list whose top and bottom edges melt into a gradient behind it.

- `UIComponent.IsLayer`: the element and its subtree are drawn at full strength into a texture of their own, which is
  then laid down once with the element's opacity, multiplied by its ancestors'. Where children overlap, the background
  shows through as much as anywhere else, instead of each child being faded on its own. The texture covers everything
  the subtree draws, within its ancestors' clip: strokes, auras and children outside the element are included. There is
  one texture per frame in flight. A composited opacity animation on the element fades the laid-down texture. Clips,
  materials and text inside the layer work in window pixels as outside it.
  - **The stand.** The sandbox's Opacity page shows two identical groups of overlapping circles, one as is and one as
    a layer, under one opacity slider.
  - **Not yet.**
    - A layer inside copies drawn by `RenderClones` fades as a plain group, without a texture of its own.
    - Copies drawn by `RenderClones` inside a layer make its texture as large as its ancestors' clip.
    - A backdrop material inside a layer sees only what the layer has drawn so far.

- A universe shown in the UI takes gestures of fingers. The element it is shown in turns gestures on, and their steps
  go to the engine's input as `GestureInput`. By default, two fingers moved side by side turn the camera, a pinch zooms it
  and a turn of the fingers rolls it.

- Gestures of two fingers or more, `Gestures.ManipulationEvent`.
  - **What it says.** Each step carries what the gesture was recognized as, `Pan`, `Pinch` and `Rotate` in any mix,
    and only those parts move. Each part counts once it passes its own threshold: the drag threshold for a pan,
    `Gestures.PinchThreshold` for a pinch, `Gestures.RotationThreshold` for a rotation. A step gives the move, the
    scale, the turn and the middle of the fingers, and the totals so far.
  - **Where it goes.** To the nearest element with `Gestures.IsManipulationEnabled` on, from the first finger up.
    `ZoomBox`, `InfiniteCanvas` and `FractalView` have it on and zoom about the fingers' middle and pan with them.
  - **Taking the fingers.** Until a gesture is recognized the fingers are plain pointers. Once it is, what they
    pressed is told `PointerCanceled` and loses them, so a button under one is not clicked and a stroke begun on the
    canvas is given up. Their moves and lifts go to no element from then on (`Pointer.IsManipulating`). A finger that
    lands later joins the gesture, and it goes on while one finger is left.
  - **Fingers of one frame.** A step is taken once every finger has reported, so the middle of a pinch does not
    wander between one finger's report and the next.
  - **Before it is recognized.** While two fingers rest together on an element that takes gestures
    (`Pointer.IsInGesture`), what follows one finger alone holds still: a finger's scroll and a fractal's drag. A
    canvas's panels and its minimap take gestures themselves, so two fingers over them leave the canvas's camera alone.
  - **Automation.** `adam-auto pinch <selector> <scale> [--rotate <deg>] [--pan <dx,dy>] [--os]` and
    `AutomationElement.PinchAsync`.

- Several fingers at once in automation.
  - **The command.** `adam-auto touches <selector>... tap | hold | drag <dx,dy>` puts a finger in the middle of each
    element, up to ten, down together and lifted together. A drag draws each finger by the same offset, in its
    element's own units. In C#, it is `AutomationSession.TapTogetherAsync`, `HoldTogetherAsync` and
    `TouchDragTogetherAsync`.
  - **Through the system.** With `--os`, all the fingers go through one synthetic device, and each frame carries
    every finger. `ISyntheticPointerInput.Inject` now takes frames of several contacts.
  - **Strokes are repeatable.** A simulated stroke inside the application now has one timestamp for the whole
    gesture. A stroke timed by the clock could be flung on with inertia or not, depending on where the clock ticked
    during it. An in-app stroke therefore never flings; a fling is checked through the system, with `--os`.
  - **Disabled elements.** `touch`, `pen` and `touches` refuse a disabled element for a hold and with `--os` too,
    as they already did for a tap or a drag inside the application.
- The sandbox has a Multitouch page.
  - **What it holds.** Ten columns, each with a button that counts its taps and a list of its own.
  - **The rings.** A layer over the columns rings every contact that is down, sized by how hard it presses, and shows
    the most contacts down at once.
  - **Its scenario.** `Scenarios/MultiTouch.adam` drives the page with two to ten fingers.

- `UIComponent.RenderCloneStagger`: each copy drawn by `RenderClones` plays the endless composited opacity animations
  of the elements it draws this much later than the one before it, so one animation runs along the copies like a
  garland. Copies in the same phase of the cycle share one opacity slot, written by the render thread, so a screenful
  of thousands of copies costs a few dozen slots. A finite animation plays on every copy together. `RenderCloneSteps`
  says which step of the stagger each copy takes, so a copy keeps its phase while the list of copies shrinks or grows
  around it. The stagger is a registered property, so markup can bind it.

- `VirtualizingPanel.HoldSkeletons`: while on, the panel binds no item and every slot in the window still without a
  container shows a skeleton card, for as long as the data behind the list is not ready.

- `Timeline`: the base of every animation, a component. `Duration`, `Delay`, `IterationCount`, `AutoReverse` and
  `Easing` are registered properties and can be bound in markup.
  - `Animation` (keyframes) and `PropertyAnimation` derive from it. `DoubleAnimation` and
    `FontVariationListAnimation` have bindable `From` and `To`, `PulseAnimation` bindable `Min`, `Max` and `Property`.
  - A property animation names the property it drives in `Property`, as a pulse does, so a trigger can run one from
    markup: `<RunAnimationAction><DoubleAnimation Property="Opacity" From="0" To="1"/></RunAnimationAction>`.
  - Bindings resolve against the element the animation runs on, also when its data arrives after the start. A change
    while it runs restarts it at the same point of its cycle.
  - In a style's trigger every element the style applies to runs a copy of its own, so a binding reads that element's
    data. `Timeline.Clone` makes the copy, values and bindings included.

- The sandbox's Layout tab has a Skeletons stand: nothing but loading cards, shown and hidden by a switch, with the
  stagger, the pulse's low and high ends, its half-cycle and the cell on sliders.

- `VisualTreeNotifications.PaintMirrored`: an element's opacity changed while the render thread draws it - nothing on
  screen needs a re-paint, but a copy baked elsewhere (a visual brush) does.

- A finger scrolls a `ScrollViewer`, along the axes it has room to move on, and a flick carries on with inertia.
  - **From a button.** A finger that began on a button scrolls once it moves past the drag threshold; the button lets
    go and is not clicked.
  - **Who keeps the gesture.** What takes the finger's moves itself keeps them: a slider's thumb, a text selection,
    or a scroll area inside this one, which scrolls before the page around it.
  - **Switching it off.** `ScrollViewer.IsTouchScrollEnabled`, true by default.
  - **The mouse.** `PanningMode` still governs only the mouse and the pen.
  - **Rows.** A finger selects a row of a `ListBox`, a `TreeView` or a data grid on its tap, so scrolling a list
    selects nothing. A hold selects the row as it starts, before the menu. A finger dragged across a grid scrolls it
    and does not sweep a selection; a second tap still opens a cell for editing.
  - **Carrying.** In a scroll area the finger can scroll, a finger carries a draggable row or a tab once it has held
    it. Until then its move is a scroll. Where nothing around could scroll, it carries at the drag threshold, as the
    mouse does. Drag and drop follows the pointer that pressed, a finger or a pen included. A drag to another
    application stays the mouse's.

- A long press, `Gestures.HoldingEvent`.
  - **How it runs.** A finger or a pen held still for `Gestures.HoldingTime` (500 ms by default), without moving past
    the drag threshold, raises `Started` on what it pressed. The lift then raises `Completed`. Moving off, or the
    system taking the contact away, raises `Canceled`. The mouse does not hold.
  - **What it changes.** A lift that ends a hold is no tap: a button pressed and held is not clicked
    (`Pointer.IsHolding`; `ButtonBase.LongPressCancelsClick`, which a `RepeatButton` turns off to go on repeating). The
    pointer's capture stays, so a thumb held a moment before it moves still drags. A pen pressed with its side button
    does not hold.
  - **Double tap.** Two taps in a row count as a double tap across touches, though each touch is a new pointer.
  - **Context menu.** A completed hold opens the context menu, the touch way of the right button.
  - **The cue.** While the hold lasts, a `HoldIndicator` drawn by the theme rings the contact. On Windows, the
    system's own press-and-hold ring is switched off.
  - **Automation.** `adam-auto touch|pen <selector> hold` and `AutomationElement.HoldAsync`/`PenHoldAsync` long-press
    inside the application, or through the system with `--os`.

- Touch and pen on Windows. A window reads `WM_POINTER` messages for a finger or a pen into pointer events: position,
  contact, primary, pressure, tilt, rotation, eraser and side button, and a finger's contact area. A gesture the system
  cancels arrives as `PointerCanceled`. A finger no longer reaches the application as the mouse the system used to
  make of it. The mouse and a touchpad still come as mouse messages.

- `ISyntheticPointerInput`: the platform's own synthetic pointer input, which hands a finger or a pen to the system as
  `SyntheticPointerFrame`s. It is for checking a platform's pointer path, since the system presses whatever window is
  on top of the desktop. Windows implements it with a synthetic pointer device.
  - `adam-auto touch|pen ... --os` and `throughSystem: true` on the automation's touch and pen methods use it.
  - The command refuses when the element's window is not on top where the contact lands.
  - It waits until the contact's release has reached the window.

- Touch and pen in automation, simulated inside the application as the mouse is:
  - `adam-auto touch <selector> tap | drag <x1,y1> <x2,y2>`;
  - `adam-auto pen <selector> tap | drag ... [--pressure <0..1>] [--barrel] [--eraser]`;
  - `AutomationElement.TapAsync`, `TouchDragAsync`, `PenTapAsync` and `PenDragAsync`.

  The sandbox's `Touch.adam` scenario taps and drags its buttons, list, text box, slider and context menu.

- `Transform.ShiftX` and `ShiftY`: a move in units of the owner's own size (1 = its whole width or height), added to
  `TranslateX`/`TranslateY` for a render transform. The render thread composes it too, so an animation of it is
  composited: a band can travel a fraction of its card whatever size the card is.

- Pointer input, one model for the mouse, a finger and a pen: `Pointer` (each with its own position, capture and element
  under it, so two fingers drag two things at once), `PointerType`, `PointerPoint` and `PointerPointProperties` (buttons,
  pressure, tilt, twist, eraser, barrel button, contact size), and the events of `PointerEvents`: `PointerPressed`,
  `PointerReleased`, `PointerMoved`, `PointerEntered`, `PointerExited`, `PointerWheelChanged`, `GotPointerCapture`,
  `PointerCaptureLost`, `PointerCanceled`, with `Preview` tunnels for press, release, move and wheel.
  `PointerDevice.Current` turns a platform's `RawPointerEventArgs` into them. A control overrides
  `OnPointerPressed(PointerPressedEventArgs)` and the like, reads the button from `ChangedButton` and what is held from
  `Properties`, and captures with `e.Pointer.Capture(this)`. `IsPointerOver` and `IsPointerDirectlyOver` count every
  pointer over an element. The mouse is `Mouse.Pointer`.

- `FocusManager.Reveal()` and `FocusManager.FocusRevealedEvent`: shows where the focus is without moving it. The first
  Tab after a click now lights the ring on the clicked control instead of jumping to the next one; the next Tab moves on.

- `FocusManager.Return(component, ringVisible)` and `FocusManager.IsReturning`: puts the focus back where it was, with
  the ring only if it had one, and scrolls nothing - a list row coming back into the window no longer pulls the scroll
  to itself, which held the list in place and laid rows out of order.

- `IKeyboardEntry`: a container that Tab enters once says itself where. A list enters at the row the keyboard is on,
  scrolling to it if it is out of the window, instead of at the container last focused, which by then showed another
  row.

- `ItemsControl.KeyboardIndex`: the place of the item the keyboard is on, kept as its index rather than its container,
  and moved with inserts, removes and moves. Every focusable items control now does what only the tree did: a focused
  row scrolled out of a virtualizing panel leaves the focus with the list instead of riding its container onto another
  row (or being lost when the container is parked), and the row scrolled back takes the focus again, with the ring only
  if it had one. A container taking the focus sets it; the tree and the list box set it from the arrow keys. Only a
  focused row is let go of: a focus inside one, such as a cell editor, is left where it is.

- `ItemsControl.CanReuseContainer(container, item)`: a control whose containers differ by item says which recycled
  container may show which item; the most recently parked one that fits is handed out, and only when none does is a
  new container made.

- `FundamentalUIComponent.Follow`: a control listens to something that outlives it - a view model's collection or
  object, a shared resource - under a key; replacing what is followed under that key lets go of the old one, and a
  discarded element lets go of everything it follows.

- The frame verifier, `UIApplication.FrameVerification`: switched on at run time (`IsEnabled`) to hunt a rendering bug,
  it checks what a window's render cache draws with against what the tree says. On each record it notes every
  component's offset, size, clip, parent, opacity and world transform; after the window draws that record it compares
  the cache's frozen layouts and their parent chains, the world transforms it composed and the groups in its paint
  order, and logs each part that differs - the component, the field, both values, how the frame was drawn and its
  motion nodes - to a session log, up to `MaxReports` frames. No GPU work; off, it costs one read per frame. Sandbox:
  "Verify frames" in the diagnostics panel, counts on the plate.
  - **What the shaders read, too.** The slots a unit's records point at - its opacity, and each text run's matrix,
    opacity and clip slots - must be held for that element or an ancestor, not free or a stranger's (matrices,
    opacities and clips share one table); every text run must lie inside the glyph batch, match its text's glyph count
    and share no instance with another run; and the alpha a run is drawn at (its color times its opacity slot) must be
    the tree's - the foreground, the element's `SelfOpacity` and the opacities above it up to a layer.
  - **Each glyph and the order it is drawn in.** Every glyph's place as its shader puts it (its slot's matrix over its
    local rect) and its atlas cell against the tree; the clip rectangle it is cut by against its owner's place; the
    text's color against its `Foreground` in the tree (not the cache's own brush); visible batchable text that has no
    run, a run no segment draws; and text drawn before an ancestor's background, which then covers it.
  - **An error in the log.** Each new alarm is also logged as an error, so a driven run fails on it (see below).

- Automation catches a broken render or tree on its own. An application started by `AutomationSession.LaunchAsync` -
  scenarios, `adam-auto start` - verifies its frames from the start (`LaunchOptions.VerifyFrames`, true by default;
  the variable `ADAM_VERIFY_FRAMES`). After every step, and in `WaitForIdleAsync`, the settled windows and their overlay
  are checked for an element that was thrown away, parked or released while still in a window, and for a visible
  presenter holding content it does not show. Each is reported once to the `ErrorJournal`, as are the verifier's
  alarms, so the step fails with what broke - unless `AllowErrors` says the error is what is being checked.
  `adam-auto visual` prints a node's lifecycle when it is not live, and the foreground of text and presenters.

- `adam-auto wheel <selector> <notches>` and `AutomationElement.WheelAsync`: the mouse wheel turned over an element's
  middle inside the application, a notch at a time - positive away from the user, scrolling up.

- `adam-auto caret <selector> <line>[:<col>] [--to <line>[:<col>]]` and `AutomationElement.PlaceCaretAsync` /
  `SelectTextAsync`: the caret straight to a line and column of a text, or a selection between two, through its text
  pattern; lines and columns from 1, as the element numbers them.

- `IThemeManager.AddStyleSet<T>(byTheme)` and `AddStyleSetToThemes<T>(byTheme)` for a control's static constructor:
  a style set made for each theme a library knows, by the theme's type (one deriving from it included), and `T` for
  every other theme.

- Theme icons `MatchCaseIcon`, `WholeWordIcon`, `RegularExpressionIcon` and `FindInSelectionIcon` for search
  options; `BreakpointIcon` and `BookmarkIcon` for an editor's margin.

- Decoration lines on inlines, as WPF's text decorations: `TextDecorations="Overline"`, and `DecorationLines` - each
  `TextDecoration` with its own `Location` (under, over, through the text or on the baseline), `Brush`, `Thickness`,
  `DashArray` and `Offset`; a span's lines add to its inlines'. Sandbox Text tab, Basics topic: an overline, a dashed
  red underline, a thick strikethrough, a doubled underline and a dotted baseline.

- `BaselineAlignment` on inlines, as WPF's: `Superscript` and `Subscript` where the font sets them, `TextTop` and
  `TextBottom` at the block font's ascender and descender, `Top`, `Center` and `Bottom` of the line; a span gives it to
  its inlines, `BaselineShift` adds to it, underlines follow it. Sandbox Text tab, Basics topic: indices and small runs
  at each place beside a large letter.

- Line height, line stacking and line spacing on text, inherited as WPF's `LineHeight`: `LineHeight` sets the distance
  between baselines, its extra room split above and below the text; `LineStackingStrategy` says whether a line holding
  text of a larger size or a taller control still grows (`MaxHeight`, the default) or every line is exactly that high
  (`BlockLineHeight`); `LineSpacing` adds space between lines. Text blocks and text boxes follow them (an empty box's
  one line too). `TextBlock.MaxLines`, as Avalonia's: that many lines, the rest not shown (`IsOverset`), or the last
  ending in an ellipsis with `TextTrimming`. Sandbox Text tab, Basics topic: sliders for the three on a paragraph with
  a larger run.

- `InlineUIContainer` among a `TextBlock`'s inlines, as WPF's: a control set into the line. It takes the room its size
  asks for, raises a line it is taller than, wraps and aligns as a word does and follows its own size. Its container's
  `BaselineAlignment` places it across the line: on the baseline (the default) the control's own baseline - the first
  line of text it shows, as a button's label, else its bottom - stands on the line's; `Center`, `Top`, `Bottom`,
  `TextTop` and `TextBottom` place its box; a `BaselineShift` adds to it. The control is the block's visual child and its container's logical one, so it
  takes input, focus, its DataContext and the theme as any control; automation finds it under the block, and the
  block's name leaves it out. In markup it is written inside an explicit `<InlineUIContainer>`. Horizontal text only.
  Sandbox Text tab, Basics topic: a check box, a button and a tall swatch in a wrapping line.

- `Hyperlink` among a `TextBlock`'s inlines, as WPF's: a span, underlined and in the theme's accent color, that a
  click, Enter or Space activates - `Click`, then `Command` with `CommandParameter`, then `RequestNavigate`; an absolute
  web or mail `NavigateUri` nobody handles opens in the system's handler (other schemes need a `RequestNavigate`
  handler). Over a link the pointer is a hand. A block with links takes the keyboard unless its author said otherwise:
  Tab and Shift+Tab walk its links and the theme's focus ring goes round the one with the keyboard
  (`InputUIComponent.FocusBounds`); a press on its plain text leaves the focus to the control around it
  (`IFocusableInParts`). Automation sees each link as a Hyperlink child of its block, named by its text, with Invoke and
  focus; `adam-auto click` and `hover` reach a link by its middle (`IElementPartPeer`). Inlines - a link's theme color -
  follow a theme swap. Sandbox Text tab, Basics topic: a link to an address and a link counting its clicks.

- Spans in a `TextBlock`'s `Inlines`, as WPF's and Avalonia's: `Span` groups inlines and gives them what it sets
  (color, size, background, weight, style, features, language, tracking, baseline shift...), the nearer setting winning
  and lines adding up; `Bold`, `Italic` and `Underline` are spans with one thing set; `LineBreak` ends the line within
  the paragraph (U+2028), which keeps its direction. The formatting properties moved from `Run` to `Inline`, so every
  inline has them (source-compatible; code compiled against the old `Run.*Property` fields has to be rebuilt). A
  `Typography.*` value set on an inline lays the text out again. A `TextBlock` of inlines is named by their text for
  automation. Clearing `Inlines` lets go of every inline it held. Sandbox Text tab, Basics topic: nested
  bold and italic, a line break, a colored span with an underlined part and a run of its own color.

- Text written straight into markup among inlines, as WPF's: `<TextBlock>Hello <Bold>world</Bold></TextBlock>`
  holds runs, `<Run>text</Run>` is its text. White space collapses to one space, the ends of the content are trimmed,
  and none is kept around a `LineBreak`. The build, the designer's loader and the language server read it alike
  (`[Content]` on `TextBlock.Inlines` and `Run.Text`; `ContentWrapperAttribute` names the type text is wrapped in,
  `TrimSurroundingWhitespaceAttribute` the elements white space is dropped around).

- Markup reports an element of the wrong type in a collection where it stands - `<Bold><Button/></Bold>` - instead of
  failing in the generated code.

- `Run.BaselineShift`: a run raised above the line's baseline (negative lowers it), its size and the line's height
  unchanged - superscripts and subscripts the font has no forms for, footnote marks. Sandbox Text tab, Basics topic:
  E = mc², H₂O, xⁿ⁺¹, an underlined footnote mark, and a slider moving an underlined, highlighted run.

- `Kashidas` on every element (inherited, on by default), taken by `TextBlock` and `TextBox`: justified Arabic
  stretches its words with kashidas once its spaces are at their maximum. Sandbox Paragraph topic, Justification
  section: a justified Arabic paragraph under a switch for kashidas, and range sliders for word and letter spacing and
  glyph scaling, each setting over the justified text it changes.

- `LastLineAlignment` and `SingleWordJustification` on every element (inherited), taken by `TextBlock` and `TextBox`:
  where a justified paragraph's last line and a line of a single word stand. Sandbox Paragraph topic: buttons for
  both.

- `GlyphScaling` (`"97% 100% 103%"`) and `JustificationAlternates` on every element (inherited), taken by
  `TextBlock` and `TextBox`. Sandbox Paragraph topic: sliders for the glyph scaling limits.

- `WordSpacing`, `LetterSpacing` and `Tracking` on every element (inherited), as InDesign's justification and
  tracking have them: `WordSpacing="80% 100% 133%"` (the default), `LetterSpacing="-5% 0% 10%"`, `Tracking="50"`
  (thousandths of an em); `Run.Tracking` for a run of its own. `TextBlock` and `TextBox` take them. Sandbox Paragraph
  topic: sliders for the word and letter spacing limits and tracking.

- `HorizontalTextAlignment` on `TextBox` (and every `TextBoxBase`): wrapped lines left, centered, right or justified,
  the caret, clicks and selection following the stretched text. Sandbox Paragraph topic: the editor is justified with
  the columns, and a Hebrew column shows justified right-to-left text, its last line on the right. The caret of an
  empty right-to-left field that wraps stands on the right.
- Vertical text on `TextBlock`: `WritingMode="VerticalRightToLeft"` runs its lines down, as long as the block is high
  (its `Height`, else the height it is given), and stacks them from its right edge, however wide the slot; the block
  asks to be as wide as they take, and `Columns` stack one under another. Glyphs turned on their side draw turned in the glyph batch (`GlyphItem.Second.z`),
  and a squiggle runs down beside vertical text. Sandbox Text tab, new Vertical topic: "I Am a Cat" set vertically,
  with the line length, the tiers and the alignment along the lines to change.
- Columns and exclusions on `TextBlock`: `Columns` and `ColumnGap` flow text wrapped by words through columns side by
  side - as tall as the text split evenly needs when the block has no height - and `Exclusions`
  (`"0,0,120,90; 240,200,100,100"`, an `ExclusionList`) are areas it flows around, as a picture placed over it takes;
  `IsOverset` tells that the text did not all fit. Sandbox Paragraph topic: three balanced columns, and text flowing
  around a picture.
- Drop caps on `TextBlock`: `DropCapLines` (2 or more; 0, the default, for none), `DropCapCharacters` and
  `DropCapFontFamily` for a decorative face of initials. Sandbox Paragraph topic: a drop cap across three lines, and
  an initial of the liturgical books (Vertograd) across four.
- `TabStops` and `OpticalMarginAlignment` on every element (inherited): tab stops left, centered, right or on a
  decimal separator, with leaders (`TabStops="120, 300 Right Leader=., 400 Decimal AlignOn=','"`), and punctuation
  and hyphens hanging partly past the margin. Sandbox Paragraph topic: optical margins switched on and off, a
  Japanese column broken by the kinsoku rules, a table of contents with dot leaders and prices lined up on the
  decimal point.
- `LineBreaking` on every element (inherited; `Greedy` by default, `Paragraph`): `TextBlock` and `TextBox` wrapped by
  words break a paragraph at a time, as TeX and InDesign do - evenly spaced lines, an even ragged edge, fewer hyphens.
  The sandbox's Hyphenation topic is now Paragraph: the same text broken both ways side by side, and the columns of
  five languages switched between them.
- Hyphenation. `Hyphens` on every element (inherited; `Manual` by default, `Auto`, `None`): `TextBlock` and `TextBox`
  wrapped by words break a word across lines at its soft hyphens, and under `Auto` by the hyphenation patterns of its
  `Language` (Russian, English, German, French and Spanish built in), a hyphen ending the line. `TextTrimming` now
  works with `WrapByWords` too: a word too wide for its line, and the last line the height allows, end in an ellipsis.
  Sandbox text page: a Hyphenation topic, columns in five languages with the mode, the width and justification to
  change, soft hyphens and an editor.
- Arabic in `TextBlock` and `TextBox`: letters joined, lam-alef, vowel marks, Persian and Urdu, by the engine's Arabic
  shaping; the caret and selection go by the screen as for Hebrew. Sandbox text page: an Arabic paragraph and editor
  beside the Hebrew ones.
- Sandbox text page split into topics, switched by radio buttons as the brushes page is: Basics, Editing, Faces, Color
  and emoji, OpenType, Variable fonts, Scripts — each a view of the same view-model, so the message and its size stay as
  they were across a switch. Scripts adds Urdu in Nastaliq, Church Slavonic (Ponomar), Old Cyrillic (Monomakh) and the
  initials of the liturgical books (Vertograd).
- Theme icons `LockIcon` (a padlock: what cannot be changed) and `CodeIcon` (angle brackets: the source of
  something), in the Fluent icons the Fluent, macOS and Graphite themes share.
- Text in both directions. `TextDirection` on every element (inherited; `Auto` takes each paragraph's direction from
  its first strong letter): `TextBlock` and `TextBox` lay Hebrew right to left among Latin and numbers, and a
  right-to-left paragraph starts on the right. In such text the `TextBox` caret and selection go by the screen: the
  caret stands where the click was and after the letter just typed, the arrows move it a letter left or right on
  screen, Home and End to the line's edges, and a drag selects what lies between the press and the pointer, even
  where those letters lie apart in the text (copy takes them in reading order, typing replaces them all; undo brings
  the text back without the selection). The placeholder of an empty right-to-left box stands on the right. Text in one
  direction behaves as before. Sandbox text page: a paragraph and an editor with a switch between the directions.

- `FontVariations` can move: `FontVariationListAnimation` (From, To) and `FontVariationListTransition`, CSS's
  transition of `font-variation-settings`. Each frame is laid out at its own axis values and drawn between the font's
  key instances on the way, so a weight animation does not fill the glyph atlas a frame at a time; at rest the text is
  drawn as before. `FontVariationList.Between` gives the values on the way. `BeginAnimation` takes either animation
  (`PropertyAnimation`, which `DoubleAnimation` now derives from). Sandbox text page: a line of Roboto Flex that
  moves between light and heavy, wide.
- `Adamantium.UI.LanguageServer` package: the AUML language server, framework-dependent, copied to `AumlServer` beside
  the application that references it - for an application's own AUML editor, as the Rider plugin ships the same server
  for Rider. In a folder of its own because it carries its own Roslyn, decompiler and UI assemblies.
- The AUML build and the AUML language server speak Russian as well as English: their problems, the descriptions of
  the `x:` directives, hovers, quick fixes and completion details. The build says them in the system's UI language;
  the server in the language its client names when it starts it (`locale` of `initialize`), the system's when none.
- AUML build errors point at the line and column of the `.auml` file they are about (`View.auml(12,9): error AUM001`),
  not at the generated code. The language server runs the build's own generator over the open file, so the editor
  shows exactly what the build would say, where it says it.
- The build reports markup it used to accept in silence, or fail on in the generated C#: a second child of an element
  that holds one (`<View><Grid/><PropertyGrid/></View>`), a property set twice, `Content` set and given a child, two
  values for a property that takes one, a template with two roots, a child or text where there is no place for it, an
  abstract or static type as an element, a read-only property given a value, a value of the wrong type, an `x:Name`
  given twice or not an identifier, an `x:Key` given twice in one dictionary, attributes on a property element, an
  unclosed `{`, and a `Setter` naming a property its style's type does not have or giving it a value it cannot take.
- The build checks a `{Binding}` path where it knows what the path reads: from the view's `x:ViewModel`, a template's
  `x:DataType`, or a `DataContext` bound to a path of one of them - with the properties and commands the MVVM generator
  makes. Elsewhere a binding is still the runtime's to report.
- The build checks triggers: a `TargetName` or `SourceName` the template has no part of, and a trigger, condition or
  setter naming a property its element does not have or giving it a value it cannot take - in a style, in a
  `ControlTemplate`'s triggers and in an element's own.
- Themes: setters for properties their types do not have are gone - `TextBox.VerticalContentAlignment` in the data
  grid styles, `RibbonApplicationMenuItem.ForegroundSelected` in macOS. They never applied.
- A property of another type that is not an attached one - `<Grid><PropertyGrid.Bounds>`, `PropertyGrid.Bounds="..."`
  on a `Border` - fails the build, and the language server flags it where it is written. It used to build into nothing,
  or into a call the C# compiler rejected in the generated code.
- The language server checks every xmlns: a URI no referenced assembly declares with `[XmlnsDefinition]` is an error,
  as a `clr-namespace:` it does not find already was.
- An attached property is a static `Get`/`Set` pair - `SetRow(element, value)`: `Clipboard.Text="..."` (a static
  `SetText` of one argument) or `AdamantiumComponent.Value="..."` (an instance `SetValue`) fails the build as not an
  attached property, where it used to fail in the generated C#. The language server's completion leaves out what
  markup cannot write there: an abstract markup extension (`{BindingBase}`), a class whose methods only look like an
  attached property's as an element, a static class as a referenced type (`{x:Type}`, `x:DataType`, `TargetType`), and
  in `{x:Static}` a type with no static values. In a property element it offers what the property can hold, by the
  build's rule (`PropertyValues`): brushes and markup extensions in `<Border.Background>`, `RowDefinition` in
  `<Grid.RowDefinitions>`, anything creatable in resources - no longer every element there is.
- The language server knows colors as the framework reads them (`Colors`: a name in any case, `#` and hex): a color
  in completion is a Color item with the color as its documentation, so an editor draws a swatch of it; and
  `textDocument/documentColor` gives the colors a file writes - a brush or color property's value, attached ones, a
  `Setter`'s on its style's type - with `colorPresentation` writing a picked color back as `#RRGGBB`.
- A right-aligned, trimmed `TextBlock` stands at its slot's right edge after a measure with no arrange after it (its
  size came out the same): it stood at the width it was measured against, short of the edge - the detail column of a
  list whose rows are reused.
- `FontVariations` on every element, inherited by the text inside, and on a `Run`: axis values of a variable font over
  the ones its weight, width and style set (`FontVariations="GRAD=150, opsz=36"`; `opsz=auto`, like leaving it out,
  sets the optical size to the text's size). `FontStyle` sets a variable font's 'ital' or 'slnt' axis, as CSS does.
  Sandbox text page: Roboto Flex's axes by the font's own names and the names of their values, text at its own optical
  size, and axis values by `FontVariations`.

- Sandbox text page: a panel of OpenType features (each feature of Source Sans 3 that changes glyphs, by the font's
  own name, with characters it changes drawn with it on) and a glyph panel (every alternate of a character, drawn with
  the feature and value that ask for it).
- `ColorPalette` on every element, inherited by the text inside, and on a `Run`: the palette of a color font its color
  glyphs are drawn in, by its number in the font's 'CPAL' (`ColorPalette="1"`); 0, the font's first, by default.
- Text set in a font whose color glyphs are PNG images ('CBDT' as Noto Color Emoji, 'sbix') or SVG
  documents ('SVG ') draws them. The sandbox's Text tab shows each, SVG glyphs beside the same drawings in 'COLR', and
  one color font in each of its palettes.
- Text draws characters its font lacks from the system's fallback fonts: Chinese, Japanese and Korean in the family of
  the text's `Language`, emoji and symbols in Segoe UI Emoji and Symbol, each in the weight and slant of the text. A
  character no font has shows the font's missing-glyph box.
- `FontWeight`, `FontStyle` and `FontStretch` on every element, inherited by the text inside: `FontWeight="SemiBold"`
  or `"650"`, `FontStyle="Italic"`, `FontStretch="Condensed"` pick the family's nearest face, as CSS picks it, from the
  system's fonts indexed by family. A `Run` takes its own (`<Run FontWeight="Bold"/>`) and is set in that face within
  the block's line. `FontFamily.GetFont` gives the face for a weight, slant and width. A weight or width the parser
  does not know fails the build and is underlined in the editor.
- A variable font takes any weight and width along its axes, not only its named faces: `FontWeight` and `FontStretch`
  set the 'wght' and 'wdth' of Bahnschrift, Segoe UI Variable or Sitka (`FontWeight="460"`), and of a family made from
  a variable font file. `FontFamily="Bahnschrift"` in markup (`FontFamily.Parse`).
- `FontSynthesis` on every element and `Run`, inherited by the text inside: `FontSynthesis="Weight, Style"` draws a
  bold or italic the family lacks by thickening or slanting the face it has, live as it changes; `None` (the default)
  draws the nearest face as it is, and a face the family has always wins. `UIComponent.TextShaping(font)` gives what
  text set in a face is drawn with, the synthesis included.
- `FontFamily.TryGetFont` and `UIComponent.TryResolveFont`: a face without waiting for its file, the family's own face
  standing in while it loads; `WaitForFonts` and `OnFontsArrived` for a control that lays out text of its own.
- The markup generator warns about a value of a type the type parser has no parser for, with the attribute and its
  line: such a value builds and then throws where the view is built.
- Emoji from 'COLR' version 1 fonts draw with their gradients and shading - Segoe UI Emoji's as Edge draws them - in
  the text batch beside the text around them (`GlyphItem.Paint`).
- `FontFeatures` and `Language` on every element, inherited by the text inside: OpenType features as a
  `FontFeatureList` (`[FontFeature.Ligatures.Off, FontFeature.StylisticSet(1)]` in code, `FontFeatures="liga=0, ss01"`
  in markup) and a BCP 47 language that picks the font's local forms; text without a language is in the application's
  (`Languages.Current`) and reshapes when it changes. `Typography.Ligatures`, `Capitals`, `NumeralStyle`,
  `NumeralAlignment`, `Fraction` and `Variants` name the common features. A feature tag the OpenType registry does not
  have fails the build and is underlined in the editor, with the tag it most likely meant.
- `Run.Background`, `TextDecorations` (underline, strikethrough, squiggle), `FontFeatures` and `Language`. A
  `TextBlock`'s runs lay out as one text: they wrap and align together, a line is as tall as its largest run, and the
  block draws the runs' backgrounds and lines.
- `UIComponent.TextShaping` and `UIComponent.ShapesLike` are public: a control that lays out text of its own - an
  editor laying out a line at a time - shapes it with the element's `Typography`, `FontFeatures` and `Language` the way
  `TextBlock` does, and knows when they changed.
- `Theme.CurrentVariantProperty`: a switch of variant is announced as a property of the theme, once its palette and
  values are all in the new variant - for what has to know which variant is in force, not only its colors.
- Input method editors on Windows (IMM32): the IME's composition and candidate windows open at the caret of a
  `TextBox`, and the text it commits arrives as one `TextInput`. `InputMethod.SetCaretBounds` tells the IME where the
  caret of any text control is; `Keyboard.TextCompositionStarted`, `TextCompositionChanged` and `TextCompositionEnded`
  carry the text being composed, for a control that shows it in place.
- A character outside the Basic Multilingual Plane - an emoji - typed on Windows arrives as one `TextInput`, not as
  two halves.
- `RibbonRadioButton`: a ribbon command that is one of a set - a tool among tools. Drawn as a `RibbonToggleButton`; a
  press only checks it and clears the others of its `GroupName`, which does not reach past its `RibbonGroup`. Each one's
  `IsChecked` binds to the view-model, as radio buttons do.
- A markup file with any control at its root - a `RibbonTab`, a `RibbonGroup`, a panel - is a class of its own deriving
  from that control, which other markup places by name (`<ribbon:HomeTab/>`): a large ribbon can be split into a file
  per tab and a file per group. Until now only windows, views and panes made a class; other roots were skipped.
- `Ribbon.StripContent`: the application's own commands in the strip right after "File" - a drop-down of the modules a
  document is made of, say. Its commands take key tips with the strip's.
- `MenuItem.IsCheckable`, `IsChecked` and `StaysOpenOnClick`: a row that is a switch, with a check mark in the icon
  gutter, and a menu that stays open while several are flipped.
- `Popup.PlacementAlignment`: a popup beside or under its target lines up with it edge to edge (`Start`, `End`) rather
  than centered. The themes' submenus open level with their row.
- `Ribbon.Drawer` and `IsDrawerOpen`: a sheet of the application's own that opens out of the strip in place of the band,
  the ribbon's width - a catalog, a page of settings. A press outside it or Escape puts it away.
- `Ribbon.TabSetsSource` with `TabSetTemplate` / `TabSetTemplateSelector`, and `RibbonTabSet`: what a document is
  made of - its modules - brings its own tabs. Each item is built into a `RibbonTabSet`, a piece of the module's view
  bound against the module: its tabs under a ledge of its own (`Header`, `Accent`, `IsActive`), after the ribbon's own
  tabs, gone when the item leaves the source.
- The themes' menu rows stretch their header across the row, so a header can hold an action at the row's end.
- `RibbonQuickAccessEventArgs.Item`: a command drawn for an item of the application's own data - its DataContext an
  `IQuickAccessItem` - hands that item over, and the ribbon finds it in the bar by that item alone. A band built from
  the application's commands puts them in the bar as they are, with nothing rebuilt from a description.
- `AdamantiumDesignerHost` names the designer host the build points the IDE's live preview to, in place of the one the
  package carries.
- `Adamantium.UI.Source.targets` in the repository root: a project in another repository builds on a checkout of this one
  instead of the package - the same references, generators and designer manifest - by importing it in place of the
  `PackageReference`.
- `AumlDirectiveInfo.Values`: the words a directive's value may be, for one that takes a fixed few (`x:KeepAlive`,
  `x:Load`, `x:Shared`, `x:CreateInDesignTime`). The build checks `x:KeepAlive` against the same list.
- Localization. An application's strings live in language files, one per table and language: `Strings.en.alang`,
  `Strings.ru.alang`, each a `<Language>` of `<Phrase Key="..." Text="..."/>` entries (or the text inside the tag, when
  it runs over lines); a placeholder may carry its own format in each language, `{time:HH:mm}`. The build turns each table into a class with
  a property per phrase and a method per phrase with placeholders (`PageOf(page, count)`), each language a class of its
  own; it checks the translations against the base file (`NeutralLanguage`, `en` by default) and warns of the phrases a
  translation lacks; an empty phrase is one not translated yet. `ILanguageTable` reads a table by key.
- `{Localize Strings.Close}` and `{Localize Strings.PageOf, page={Binding Page}, count=3}` in markup: a binding of its
  own kind (`Localize`, live as `LocalizeExpression`) to the string in the application's language, following the
  language and the bindings that fill its placeholders. It works wherever a binding does, inside a `MultiBinding` too. A
  key, a placeholder or an argument the table lacks fails the build. The designer shows them. In a control's template a
  placeholder takes the control's own value: `count={TemplateBinding PageCount}`.
- A phrase that changes with a number: `<Phrase Key="Files" Count="count" One="{count} file" Other="{count} files"/>`,
  a text per Unicode CLDR form (`Zero`, `One`, `Two`, `Few`, `Many`, `Other`); each language gives the forms it has and
  its own rules pick one (`PluralRules`, `PluralForm`). The build checks the forms against the language; a language may
  count a phrase its base language writes once.
- A phrase that changes with a value: `<Phrase Key="Notify" Select="on" True="Notifications on" False="Notifications off"/>`,
  a case named after each value - `True` or `False`, a member of an enum, `None` for null - and `Other` for the rest
  (`PhraseCases`). Beside `Select`, `Text` and `Count` are cases too, so an enum may have members of those names.
- A phrase as an argument of another: `size={Localize Strings.Bytes, count={Binding Size}}`.
- `{Localize CanvasStrings, Key={Binding Sort}}`: the key read from a binding - the word for a kind of thing. A key the
  table lacks is said as it is. `{x:Static Strings.Current}` hands a control the table itself, and
  `Languages.Say(table, key, ...)` says a phrase from code, for a thing named once as it is made.
- `{Localize Table={Binding Phrases}, Key={Binding Name}}`: the table read from a binding too (`Localize.TableSource`) -
  the words of a thing that brings its own table, as a plugin does. Until the table comes, and for a word it lacks, the
  key is said as it is.
- `Languages.Current` (and `UIApplication.Language`, `StartupLanguage`) switches the language while the application runs;
  every `{Localize}` and every binding that writes numbers or dates follows it.
- `<Language.Format>` in an application's language file sets how that language writes dates, times and numbers;
  `Binding.Culture` departs from it for one binding (`Invariant` or a language name).
- A library's tables are translated or overridden by an application's file of the same table name.
- The themes' strings are language tables of `Adamantium.UI.Themes` in English and Russian: `RibbonStrings`,
  `PropertyGridStrings`, `DataPagerStrings`, `ColorPickerStrings`, `DataGridStrings`, `CanvasStrings`,
  `InspectorStrings`, `WindowStrings`, `TextBoxStrings`. Their keys are public: an application's `CanvasStrings.de.alang` translates the
  canvas.
- The language server completes and checks `.alang` files with the build's own generator, offers a quick fix that adds
  the strings a translation lacks, and completes `{Localize}` - tables, strings and placeholders.
- `DataGridSearchPanel.State`, `CurrentMatch` and `MatchCount`: where a search stands, for the theme to say.
- Properties for the words a control used to carry in its code, set by the themes: `ImageSourceProperty.DialogTitle`,
  `PicturesName`, `AllFilesName`; `InfiniteCanvas.ExportTitle`, `OpenTitle`, `DrawingsName`, `AllFilesName`,
  `DeleteTitle`, `ClearTitle`; `Phrases` - the table a control names things with - on `InfiniteCanvas`, `CanvasNode`
  and `DataGridGroupHeader`.
- `CanvasQuestion`: what the canvas asks before it deletes or clears, a control the theme gives its words and answers.
- `CanvasInspector.ItemCount` and `ICanvasItem.Parts`: numbers the theme says in words.
- `TextBox.ShowsClearButton`: a button at the end of the box that empties it in one press, shown while there is text
  and the box is not read-only. `TextBox.HasText` says whether there is; `TextBox.Clear()` empties the box as one edit,
  which undo brings back. The themes draw the button. `TextBox.ClearButtonToolTip` is what it says when pointed at:
  the themes' `TextBoxStrings.Clear` unless a box says what clearing it means - "Clear the search (Esc)".
- `CollectionView.Refresh()`: filters and orders the source again - for a filter that reads something outside the items,
  such as the text of a search box.
- `UniformGrid` virtualizes as an items panel: a grid that grows downwards realizes only the rows in view and scrolls
  by them. An axis whose count is set shares the space between its cells; along the other one every cell is as big as
  the biggest item realized so far. A grid given only `Rows` grows sideways and realizes every item, as does one with
  `IsVirtualizing` off. Used as a plain panel it lays out its children as before.
- `Popup.StaysBesideTarget`: the popup is measured against the room on its side of the target instead of the whole
  window, so keeping it inside the window never pushes it over what it belongs to; what does not fit is the content's
  to scroll. The ribbon's drawer uses it in all three themes - a catalog taller than the room under the strip used to
  be lifted onto the tabs.
- `UniformGrid.MinColumnWidth`: with neither `Columns` nor `Rows` set, as many columns as fit at least that wide, the
  width shared out between them and never fewer than one - cards in a pane that changes width keep room for what they
  show instead of keeping a count.
- `RibbonQuickAccessEventArgs.DropDownItemContainerStyle`: what a drop-down command's rows do, handed over with how they
  are drawn. The themes' quick-access menus use both.
- `PropertyTrace`: values dropped for want of a property - set by a name the element has no property for, or a live
  resource connected to one. Each distinct report goes once to the application log and, under a debugger, to the IDE's
  output; `PropertyTrace.Sink` receives them all.
- A window shows its application's icon - the project's `ApplicationIcon` - on the taskbar and in Alt+Tab; it showed
  the system's blank one. The sandbox has an icon of its own, in the title bar too.
- `Window.TitleAlignment`: `Center` puts the title in the middle of the whole window, however wide the commands and
  buttons on either side are (`TitleBar.TitleCentering`), in Fluent and Graphite as macOS already did.
- `Window.StartupLocation`, as in WPF: `CenterOwner` by default - over the window that was active when it opened, else
  `CenterScreen`, the middle of the screen the pointer is on - or `Manual`, at `Left` and `Top`. Windows used to open in
  the top-left corner of the primary screen.
- A window comes back where it was closed: on the same screen, wherever that screen has been moved, at its place and size,
  maximized if it was (`RemembersPlacement`, on by default; `PlacementKey` for several windows of one type). A screen no
  longer connected leaves it to `StartupLocation`. The places are kept by `IWindowPlacementStore` - by default
  `FileWindowPlacementStore`, a file in the user's local application data - and dialogs in windows are not remembered.
- `PlatformSettings.Screens` (`ScreenInfo`: id, bounds, work area, scale) and `IWindowWorkerService.RestoreBounds`.
- `IRegionManager.NavigateToAsync<TViewModel>(regionName, ...)`: a named region navigated in one call. A region no
  control has declared yet is created, and the control that declares it later shows what it navigated to.
- The language server underlines a type in `{x:Type}` and a type or member in `{x:Static}` that the build would not find,
  with the build's own message, and paints such a type as unknown.
- A `Pane` written straight into a `DockingArea`, without a `PaneGroup`, is placed by its `Zone` - one panel for the panes
  of each zone. `DockingLayout.ZoneOf` says where a group stands.
- A pane a docking region opens follows its view model: the tab follows `IDockablePane.PaneTitle`, and `PaneZone` goes
  both ways - a view model with a setter moves its pane by setting it, and a drag sets it. `IDockablePane.PaneKind` and
  `PaneMinSize` (a document and 0 unless said) make it a tool or give it a floor; everything a region opened was a
  document before, and the title was read once.
- `IDockingRegion`, from `IRegionManager.Docking(name)`: what a docking region holds and where, for the panes it opened
  and those written in markup alike (with their content's view model) - `ViewModels`, `ViewModelsIn(zone)`,
  `Documents`, `Tools`, `Floating`, `Hidden`, `RecentDocuments`, `ActivePane`, `ActiveDocument`, `Contains`, `Find` and
  `All<T>`, `PlacementOf`; the panels as descriptions, not controls - `Groups`, `DocumentGroups`, `ToolGroups`,
  `GroupOf` (`DockedGroup`: its panes, kind, zone, state, front tab); `Activate`, `CloseAsync`, `CloseAllAsync`,
  `ShowToolAsync<T>` (opens the tool once, then brings it to the front), `OpenBesideAsync<T>`; and `PaneOpened`,
  `PaneClosed`, `PlacementChanged`, `ActivePaneChanged`, each once per change. The same object before a docking
  control shows the region and after it is replaced; the control answers through `IDockingHost`.
- `DockingArea.ActivePaneId`, `LayoutChanged`, `ClosePanesAsync` and `DockBeside`; `DockingLayout.Groups`,
  `DocumentGroups` and `ToolGroups`, the put-away panels included.
- A file whose root is `<Pane Header="Inspector" Kind="Tool" Zone="Right">` is a class of its own, as a `<View>` file
  is, with its own `x:ViewModel` or none: `<local:InspectorPane/>` puts it in a `DockingArea`.
- `Pane.IsDirty`: the tab of a pane with unsaved work wears a mark in Fluent, Graphite and macOS. Closing such panes -
  by a tab, a "close all" or the main window - asks ONE question for all of them, `UnsavedQuestion` (save, don't save,
  cancel) in an overlay window, worded by the theme (`PaneStrings`, `DockingArea.UnsavedTitle`).
  `DockingArea.AsksBeforeClosingUnsaved` (on by default) turns it off, `UnsavedClosing` answers in the user's place,
  `PanesSaving` saves. With nobody to save, or no window to ask in, nothing closes.
- `IDocument` (`IsDirty`, `SaveAsync`): a docking region marks the tab of such a view model and saves it when the answer
  is to save. `IDockingRegion.Unsaved`, `SaveAllAsync` and `OpenDocumentAsync<T>(key)` - the key is the pane's id,
  passed in as `DockingRegion.KeyParameter`, so a document is never open twice. A document a region opened is disposed
  when it is closed, if it is `IDisposable`.
- `DockingArea.ResetLayout` and `DockingWorkspace.Reset`: back to the arrangement written in markup, closed tools
  brought back, open panes kept, closed documents left closed. `DockingWorkspace.SaveAs(name)`, `Apply(name)` and
  `Layouts`: named arrangements, which the application keeps where it keeps its settings.
- `DockingArea.LayoutVersion` (`DockingWorkspace.Version`) is written into a saved layout; one saved under an older
  version goes through `PaneMigrating` on load, which gives a renamed pane its new id. `DockingLayoutSerializer`
  `RenamePanes`, `ReadLayoutVersion` and `ReadStates`.
- A pane's own state travels with a layout: `DockingArea.PaneStateSaving` and `PaneStateRestoring`, and for a view model
  `IRestorablePane.SaveState` and `RestoreState`.
- `IDockingAware`: a view model in a docking region hears its own pane - `OnActivated` and `OnDeactivated`, `OnShown`
  and `OnHidden` (on screen means the front tab of a panel not folded away: what a scene view stops drawing for),
  `OnPlacementChanged` - in the order of the change, the pane left behind first. `PanePlacement.IsShown`.
- Automation peers: a control describes itself to automation - `AutomationPeer` (control type, name, id, rectangle on
  the screen, enabled, on screen, focus, children) made on request by `UIComponent.GetAutomationPeer`, and what can be
  done with it: `IInvokeProvider`, `IToggleProvider`, `IValueProvider`, `ISelectionProvider`, `ISelectionItemProvider`.
  Buttons, toggle buttons, check boxes, text blocks, text boxes, tab controls and their tabs, views and windows have
  peers; panels, borders and the parts of a template are looked through. `AutomationProperties.AutomationId`, `Name` and
  `HelpText` say what an element is called; the id is its `x:Name` unless given, and a view's or window's its class name.
- `WindowCommand.AutomationId`: the themes give it to the caption button, with the command's `Label` as its name.
- `Adamantium.UI.Automation`, a new package: finds elements by id, name, type or class (`By`, a selector path such as
  `id=Shell/id=Cut`) and drives them - invoke, toggle, set a value, select, or click and type by input simulated inside
  the framework, refused when something covers the element - waiting each time for the application to settle.
  `AutomationSession.InProcess` drives windows a test built, headless; `UseAutomationAgent` lets a running application
  be driven through a named pipe when `ADAM_AUTOMATION_PIPE` names one, its windows opening in the background.
- Automation finds out why: `AutomationElement.InspectAsync` gives an element's properties with where each value comes
  from, its bindings and why one does not work, its layout and parents; `VisualAsync` its visual tree with layout;
  `AutomationSession.StateAsync` the keyboard focus, the windows and their popups; `ShotAsync` a picture drawn by the
  application's own renderer. The `ErrorJournal` collects broken bindings, values set by a name the element has no
  property for and errors in the log; an action that leaves new entries fails, unless `AllowErrors` says otherwise.
- `AdamantiumComponent.GetValueSource`: the priority the value of a property comes from. `BindingExpressionBase.Failure`:
  why a binding does not work.
- Automation peers for lists, drop-downs, menus, trees and tab strips: an items control's children are its items in
  order - the item's element where it has one, an `ItemAutomationPeer` (a `TreeRowAutomationPeer` in a tree) where a
  virtualizing panel has not made it or a closed list has not built it, which can be selected and brought into view. A
  drop-down's list and a submenu belong to the control that opens them; a context menu and other popups to their
  window. New patterns `IExpandCollapseProvider` and `IScrollItemProvider`; a `NumericUpDown` is a spinner with a value.
  The driver expands, collapses, scrolls an item into view, hovers and right-clicks, and lists what can be acted on but
  has no name.
- `ItemsControl.ScrollIntoView`: scrolls until an item is in view, a virtualizing panel making its container on the way.
- The themes name what their templates add for automation: the tab strip's overflow button (`TabStrings.MoreTabs`) and
  the pager's buttons, page size and page number (`DataPagerStrings`); the title bar's minimize, maximize, close and
  overflow buttons (`WindowStrings`, ids `Minimize`, `Maximize`, `Close`, `MoreWindowCommands`); a range slider's two
  handles (`RangeSliderStrings`).
- Automation of numbers, scrolling and windows: new patterns `IRangeValueProvider`, `IScrollProvider` and
  `IWindowProvider`. `Slider`, `ProgressBar` and `RingProgressBar` (read-only), a standalone `ScrollBar` and
  `NumericUpDown` hold a number between limits; a `RangeSlider`'s children are its two handles, `Lower` and `Upper`; a
  `ScrollViewer`, and a list through its own, scroll to percents; an `Expander` opens and folds; a window is minimized,
  maximized, restored and closed. The driver sets a number with `SetValueAsync`, scrolls with `ScrollToAsync`, and
  `adam-auto` gains `scroll --vertical/--horizontal` and `window minimize|maximize|restore|close`.
- Automation presses keys as the system would - modifiers around the key, a letter with its character - in an element's
  window, focusing it if it takes the keyboard, or entering the window as activating it does:
  `AutomationElement.PressKeysAsync("Alt H")`, `adam-auto key Alt --into id=MainWindow`. `adam-auto state` lists each
  window's adorners, a key tip with its keys.
- Automation of the ribbon: the ribbon is tabs to choose from, a tab header a tab item selected the way a click selects
  it - a minimized band dropping its groups down - and the open tab a pane of named groups; a group the band collapsed
  opens and closes its commands; a gallery is a list picked from by name, and opens; "File" opens its menu, a row with a
  page selected to show it; a drop-down command holds its menu while open; the quick-access bar is a toolbar. A key tip
  is the element's `AutomationPeer.AccessKey`. The themes name the ribbon and the quick-access bar (`RibbonStrings`).
- Automation of `TreeDataGrid`: a data grid whose children are its column headers, then its rows - a stand-in for a row
  the virtualizing rows did not make, called by its first column. A header sorts as a click does; a row is selected,
  brought into view and, in a tree, opened, with or without an element; a cell is selected and takes a value the way
  committing an edit does - `CellEditEnding`, a blocking rule, the write through the column, the undo history; a row's
  details toggle opens its panel. New patterns `IGridProvider`, `IGridItemProvider` and `ITableProvider`.
- Automation of `PropertyGrid`: a table of property rows, each called by its property's name and written the way its
  editor writes it - converted to the property's type, to every object the row stands for, or refused; a true-or-false
  property is toggled and a composite one opens to its parts.
- `adam-auto run <folder>` runs every scenario in it with a summary; `adam-auto sweep` opens every tab of a tab control
  in turn - the gallery's by default, `--passes`, `--dwell` - and reports how long each took to settle and what it left
  in the error journal. `expect` reads an element's place and size (`left`, `top`, `width`, `height`); `key` knows the
  arrows by `Up`, `Down`, `Left`, `Right`; `state` names the focused element.
- While a gesture is simulated, the pointer the application asks for - `Mouse.ScreenCoordinates`, which a drag's
  threshold and drop target read - is the gesture's, and the system cursor is never moved: drag and drop inside the
  application is driven by automation. A drag that runs through the system's own loop still follows the real mouse.
- An `OverlayWindow` is a window to automation, called by its title and closed as its close button closes it; a
  `SlidePanel` is a pane called by its header that opens and shuts, its overlay content its children while open. The
  themes name their close and pin buttons (`WindowStrings.Close`, `Pin`).
- Automation drags: `AutomationElement.DragAsync` and `adam-auto drag <selector> <x1,y1> <x2,y2>` press the left button
  at one point of the element, move with it held and let go at the other, in the element's own units. An
  `InfiniteCanvas` and its floating panels are panes; a tool on its rail is a button found by the tool's name and
  called by it in the user's language.
- More automation peers: a tab item's children are the close and pin buttons its tab control offers (the themes name
  them, `TabStrings.CloseTab`, `PinTab`); `ColorPicker`, `ColorWheel` and `ColorPickerButton` hold their color as a
  value, `#AARRGGBB`, and the button opens its picker; a `RadioButton` is a radio button selected as a click selects
  it; a `DataPager` is a group.
- An element with no name of its own is called by its tooltip when that is text, and a text box or a drop-down by its
  placeholder.
- `AutomationProperties.LabeledBy`: an element is named by the label shown beside it,
  `AutomationProperties.LabeledBy="{Binding ElementName=VolumeLabel}"`, following the label's text and language.
- A `RibbonRadioButton` is a radio button to automation: one choice of its group, selected the way a press selects it,
  and offering no toggle - a choice is not switched off.
- `ElementInfo.ToString()` describes an element in one line - what it is, its name and id, its value, switch,
  selection and state - and `AutomationSession.TreeAsync` (`adam-auto tree`) lists every element that way, where it
  gave names alone.
- `UIApplication.WaitForIdleAsync` completes once the application is idle: nothing posted to the loop, no layout,
  binding or recorded change left to do, no animation that ends on its own still running, no glyphs or pictures on their
  way (`BitmapImage.LoadsInFlight`, `LoadsFinished`) - and the frame showing that drawn. `IdleBlocker` says what it is still waiting on. An animation that never ends counts as
  idle: one that loops forever, and a ticker registered with `AnimationManager.AddEndlessTicker` (a caret's blink, an
  animated picture). Automation waits for it after every action instead of for two frames, and `adam-auto` says what
  kept the application busy when it does not settle.
- `TypeOfAttribute` and `MarkupFileAttribute` say what a markup value may name. `[TypeOf(typeof(StyleSet))]` on a
  `Type`-valued property admits only types derived from that base - `StyleInclude.Source` takes style sets,
  `ResourceLink.Source` resource dictionaries - and the build and the live preview reject any other type, naming the
  property. `[MarkupFile("png", ...)]` on a type or a property says its markup value is a path to such a file;
  `ImageSource` and `BitmapImage.UriSource` list the picture formats the engine loads. The language server completes by
  both: a style include is offered only style sets, a resource link only dictionaries - classes in C# and markup files
  alike - and an image only the pictures of the project and the folders holding them; a type the build would reject is
  underlined as it is typed. A `Type`-valued attribute is completed in its value too, not only in `{x:Type}`.
- `ApplicationBlueprint`: what an application starts with, written in markup - `<ApplicationBlueprint StartupWindow=
  "MainWindow" StartupTheme="Graphite" StartupThemeVariant="Light">` with its `Resources` and `StyleIncludes`. The
  build makes it a class, names it to the assembly and writes the entry point, so a project has no `Program.cs`; the
  application reads it through `IApplicationBlueprint` while it initializes, the theme and its variant before the
  first frame, and what its own code sets wins over it. A project holds one blueprint and one application class: a
  second blueprint, whatever its file is called, fails the build and is underlined in the editor. The application
  template and New | AUML File in Rider create one.
- `ResourceKeyAttribute`: the build names every key a dictionary, a theme variant's palette or a blueprint declares
  to the assembly, with the type of what it holds. The language server offers keys in `{ObservableResource}` and
  `{ResourceReference}` from them and from the project's own markup - only those that fit the property: a brush for a
  `Background`, a picture for an `Image`'s `Source`.
- `UIApplication.StartupThemeVariant`: the variant of the theme the application opens on - Light, Dark, System or one
  of the theme's own. A variant the theme does not have fails the start, naming the ones it has.
- `IThemeManager.AddStyleSet(Type)`: adds a style set known only at run time to every theme.
- `DataTemplateSet`: a template selector written in markup instead of a class. Its `DataTemplate`s each state the type
  they draw with `x:DataType`; the item's own type wins, then its base classes, nearest first, then an interface it
  implements; one template without a type takes what no other fits. It goes wherever a selector goes - inline, or from
  resources by key - and the build fails on two templates for one type or two without one. `DataTemplate.DataType`
  carries the `x:DataType` at run time. A markup file with a `DataTemplateSet` at its root is a class of its own that
  other markup places by name (`<Ribbon.ContentTemplateSelector><RibbonTabContents/></Ribbon.ContentTemplateSelector>`);
  New | AUML File in Rider offers one.
- `Ribbon.ContentTemplateSelector`: each data tab's groups picked by the tab's item, as `TabControl` has it.
- The language server goes from a resource key to the markup that declares it, and from a class made from markup to its
  `.auml` file rather than the generated code - a view's element, `ResourceLink Source=`, `{x:Type}`. It renames a key
  where it is declared and wherever it is used, across the project's markup and that of projects built from source
  with it, and lists those places. A key added or renamed in a dictionary is known at once, as typed, with no build.
- `IconPresenter` draws an icon of either kind: path data stroked in its `Stroke`, or a picture - a `DrawingImage` of
  the application's resources - in its own colors. The themes' ribbon commands, groups and application menu items draw
  their `Icon` with it, so `Icon="{ObservableResource SaveIcon}"` shows the picture; until now a picture there went
  into a path's data and drew nothing.
- Windows UI Automation: a window answers screen readers and UI Automation clients (Narrator, Inspect, FlaUI) with its
  elements as automation sees them - names, ids, control types, bounds - and does what their patterns ask: invoke,
  toggle, expand, select, set a value or a range value, scroll, a grid's cells, the window's state. An open popup is a
  child of whatever opened it. The bridge is part of `Adamantium.UI` and needs no package.
- `AutomationEvents`: what changes on an element is told to whoever listens - focus, a press, a toggle, an expand, a
  selection, a value, a popup opening or closing - with the element's peer and the old and new value. A property change
  is told only for an element automation has seen. The Windows bridge passes them on to UI Automation clients.
- `IUIComponent.GetAutomationPeer()` and `FindAutomationPeer()`, the peer already made or null.
- UI tests of an application of your own, with `Adamantium.UI.Automation`: `HeadlessApplication.Start<TTheme>()` is
  the application a headless test builds its windows in - resources, a theme, its style sets - and a window from markup
  put in `AutomationSession.InProcess` is initialized as the application would, its view-model made.
  `AutomationSession.LaunchAsync` starts the application itself with its agent on a pipe of its own and closes it with
  the session. `RunScenarioAsync` runs a scenario file of `adam-auto`'s commands, `RunCommandAsync` one of them, and
  `AutomationElement.WaitUntilAsync` waits for a state - its value became 40 - not only for an element; a scenario
  writes that as `wait <selector> value=40`. `adam-auto` is now a wrapper over the same.
- `dotnet new adamantium-uitest --app <Application>`: a test project for an application - its main window driven
  headless and launched, in code and from a scenario file.
- `UIAppContext.Replace`: the current application and platform in place of those before, for a process that runs
  one after another.
- Selector paths go further than "somewhere below": `>` looks among the children only, `[n]` keeps the n-th match
  (from 0, `[-1]` the last), and `parent`, `next` and `previous` step from the element before -
  `id=Orders>type=DataItem[2]/name=Delete`, `id=Cut/next`. In code: `By.At`, `By.Parent` / `Next` / `Previous`,
  `AutomationElement.Child`, `At`, `Parent()`, `Next()`, `Previous()`.
- `adam-auto` is a package of its own, `Adamantium.UI.Automation.Cli`, installed as a .NET tool
  (`dotnet tool install -g Adamantium.UI.Automation.Cli`); `adam-auto start --exe <path>` starts an application of yours.
- `<AdamantiumRequireAutomationId>true</AdamantiumRequireAutomationId>` in a project makes a build warning (AUI011) of
  every button, field, list, slider... in its markup that has neither `AutomationProperties.AutomationId` nor `x:Name`
  to be found by. Controls in templates are left out. Off unless set.
- `ITextProvider`: a `TextBox` is read by character, word, line and paragraph - its selection, where a piece of it is on
  screen, the place nearest a point. The Windows bridge gives it the Text pattern, so a screen reader follows the caret
  through it; `AutomationEvent.TextChanged` and `TextSelectionChanged` tell it the text and the caret moved.
- `ITableItemProvider`, `IGridItemProvider.RowSpan` / `ColumnSpan` and `ISelectionProvider.IsSelectionRequired`. A data
  grid's row is an item of the table taking every column, under the column headers, and is brought into view; a cell
  knows its column's header. One tab and one ribbon tab are always selected.
- A border or a panel given `AutomationProperties.Name` or `LabeledBy` is a group of what it holds - a section under its
  heading - so two sections' controls that read the same are told apart.
- `LoopSignal.PostAwaited` and `Pause`: work another thread waits on runs while the loop holds between frames, not at
  the start of the next one. A UI Automation question is answered in well under a millisecond, not after a frame.
- `MouseDevice.HitTestTopmost`: the element a click at a point of a window reaches - open popups first, newest on top.
- An element given only `AutomationProperties.AutomationId` - a border, a panel, a shape - is found by it: it is a group
  of what it holds. Until now only a name or a label put it in the automation tree.
- Automation peers of their own: an `Image` is a picture called by its `AutomationProperties.Name`; a `BusyIndicator`
  is a progress bar with no amount, shown while it runs; a `Thumb`, a grid or pane splitter, a `DragHandle` and a
  `ResizeGripper` are thumbs; a `Separator` is a separator; a `ToolTip` is a tooltip called by what it says; a window's
  `TitleBar` is a title bar called by the title, holding the caption buttons.
- `ITransformProvider` (`PatternId.Transform`): a splitter is moved along its axis and its neighbors follow; a canvas
  node is moved and resized, each as one step of undo; a `ZoomBox` and an `InfiniteCanvas` are zoomed in percent, about
  the middle of the view. A `ZoomBox` also scrolls through automation by its own scroll viewer. The Windows bridge gives
  them the Transform and Transform2 patterns. In code `AutomationElement.MoveByAsync`, `ResizeAsync`, `ZoomAsync`; in
  `adam-auto` `move <selector> <dx> <dy>`, `resize <selector> <width> <height>`, `zoom <selector> <%>`, and `zoom` to
  expect. `ZoomBox.ZoomTo` zooms holding the middle of the view.
- A canvas node is selected through automation as a click selects it, and the canvas says which nodes are selected and
  tells automation when that changes.
- A control on an `InfiniteCanvas` that is off screen is still found by automation, by its id and name, through a
  stand-in; brought into view (`scroll <selector>`, `ScrollIntoViewAsync`) the camera moves to it without zooming, and
  from then on it is the control itself. A node of the application's graph that has never been on screen goes by its
  model's title, or kind.
- Wires through automation: a node's socket is an element called by its pin's name, whose value says what it is joined
  to; `IConnectionProvider` joins it to another socket and parts them, by the graph's rules and as one step of undo
  each (`adam-auto connect <socket> <socket>`, `disconnect <socket> [<socket>]`; `AutomationElement.ConnectAsync`,
  `DisconnectAsync`). A node folds and unfolds through ExpandCollapse.
- Docking through automation, as the Dock pattern of UI Automation: a pane says where it is docked and goes to an
  edge, into the documents or out into a window of its own by the layout's rules and the application's; beside
  another pane's panel, or into it as a tab. A panel moves with every pane in it, or tears out whole.
  `adam-auto dock <pane> top|left|bottom|right|fill|none [--beside <pane>]`, `dock` to expect,
  `AutomationElement.DockAsync`.
- Windows move and resize through automation (the Transform pattern) while they are neither minimized nor maximized;
  so does an `OverlayWindow`, kept inside the window it is shown over, and a floating `CanvasPane` - moved as its grip
  and widened as its edge would. `adam-auto move` / `resize` take them like any other element.
- Selections of many through automation: `ISelectionItemProvider.AddToSelection` and `RemoveFromSelection` add an item
  to what is selected and take it out, leaving the rest - in a list, a tree, a data grid's rows and cells and on a
  canvas; a container of one selection refuses a second. The Windows bridge passes both on. `adam-auto select
  <selector> --add`, `unselect <selector>`; `AutomationElement.AddToSelectionAsync`, `RemoveFromSelectionAsync`.
  `TreeDataGrid.DeselectRow` and `DeselectCell`, `DataGridSelection.Remove`.
- A data grid's strips are in its automation tree - the search box, the grouping and sorting strips, the button that
  chooses columns, the row for a new record and the totals - where their contents could not be reached before. A key's
  chip turns its sort around (Toggle), moves along its strip (Transform) and is taken out by its ×, a button of its
  own. The search box is named in the themes.
- Automation types for what was a nameless "custom" element: a plain `ItemsControl` is a group; the data grid's
  search, filter and column panels are groups and its sort and group strips tool bars, its totals and group captions
  text; the canvas's inspector and node palette are panes and its selection and view bars tool bars; the canvas's and
  the docking area's questions are panes called by what they ask.
- A ribbon folds down to its tabs and opens again through ExpandCollapse (`adam-auto collapse` / `expand`), as its
  minimize button does, and tells automation when it does.
- A `FlipTile` is a button that turns over through Toggle - on is its back shown - and a `FractalView` a pane zoomed
  in percent through Transform (`adam-auto zoom`), the way the wheel zooms it.
- `IPanProvider` (`PatternId.Pan`): an `InfiniteCanvas` and a `FractalView` are panned through automation as a drag
  pans them - `adam-auto pan <selector> <dx> <dy>`, `AutomationElement.PanAsync`. Automation's own capability: UI
  Automation has no pattern for a plane with no edges.
- A `CanvasMiniMap` is a picture to automation, named in the themes (`CanvasStrings.WholePlane`); the canvas's layer
  tree and node palette are named too.
- The menu key and Shift+F10 open the context menu of the focused element - its own, or the nearest one above it -
  under it, with the keyboard on the first row. Until now a context menu opened only by the right button.
- `AutomationPeer.ShowContextMenu` opens it the same way through automation: `adam-auto context-menu <selector>`,
  `AutomationElement.ShowContextMenuAsync`, and UI Automation's `ShowContextMenu` (`IRawElementProviderSimple2`).
- Drag and drop by element: `adam-auto drop <selector> <target> [--before|--after]`, `AutomationElement.DropOntoAsync`,
  carries one element onto another by a gesture made inside the application - a row among rows of a list or a tree,
  into another list, a column header among the headers or onto the grouping and sorting strips. It takes the element by
  its drag handle when it has one, and says so when the target cannot be reached. `DragDrop.GetDragHandles`.
  It drops into another of the application's windows too.
- A column header's funnel is a button to automation that opens and closes the column's filter
  (`PartButtonAutomationPeer`, which the × of a data grid's chip uses too); the themes name the filter's fields.
- `CurrentColor` in a drawing: a fill or a stroke written `Stroke="CurrentColor"` is painted with the `Foreground` of the
  `Image` showing the drawing (`CurrentColorBrush`). One icon resource is then dark on a light surface, takes the
  on-accent color on an accent - a checked ribbon toggle, an active caption - and follows the color when it changes.
  `Image.Foreground` is inherited and redraws the picture.
- `{ObservableResource Key={Binding ...}}`: a resource whose key comes from the data, followed as the data changes.
  A command names its icon by key and the view finds the resource, so icons stay in markup and the designer shows them.

### Changed

- Each platform's builder registers its gamepads, where `MultiverseApplication` used to pick them by the system it ran
  on: `WindowsPlatform.Initialize` registers `WindowsGamepadBackend` (GameInput, else XInput, chosen when first asked
  for), and the new `LinuxPlatform.Initialize` registers the evdev gamepads and Enchant spell checking - what Linux
  gives whatever draws the windows - before `WaylandPlatform.Initialize`. The desktop's settings and file dialogs
  through its portals moved there from the Wayland platform too; the window a dialog belongs to is named by the display
  server (`IPortalParent`).
- `TextBoxBase.Paste(string)` is where pasted text goes in, from the clipboard or the primary selection alike;
  `PasteFromClipboard` hands it the clipboard's, and `PasswordBox` overrides `Paste` instead.
- A window shown for the first time is activated only when its `ActivateOnShow` asks for it.
- The automation presses, drags and drops in the middle of what shows of an element, not of the whole of it: a column
  header half under the edge of its table is taken by its visible half.
- `ISyntheticPointerInput.Inject` takes the window the contacts are played on and refuses a contact that would land
  on anything else, checked right before the system takes it; the automation's planner no longer checks it.
- On `Adamantium.Vulkan` 1.0.4, as the engine is.
- One universe can be shown by several `UniverseHost`s of one window at once, each panel a view of its own into it:
  it is paused once none of the panels is in the window, and no longer when one of them leaves.
- The engine maps the Vulkan loader itself: `UIApplication` no longer registers it, nor references `Adamantium.Vulkan.Loader`.
- `INativeDragDrop.BeginDrag` takes `external`: false for a drag offered to the application's own windows only. A
  platform whose in-app drags cannot learn the window under the pointer says so with `CarriesInAppDrags` (false by
  default), and every mouse drag then goes through `BeginDrag`.
- A window whose content has not changed draws its frame from a recording: the first clean frame of each frame slot
  records the op stream into a secondary command buffer, and the next ones only fingerprint the stream - nothing is
  sent to the GPU - and execute the recording while the fingerprint is the same. Whatever the stream reads live - a
  brush, a clock, a fade - changes the fingerprint, and the frame is recorded again; a stream that keeps changing is
  issued as before for a while, up to 32 frames, between tries. A frame with an off-screen layer or a backdrop material
  - on a rectangle or on any shape - is issued as before, and so is every frame on a device without `VK_KHR_maintenance7`;
  `ADAM_NO_RECORDED_REPLAY=1` turns recording off. The editor's window, 4K: a clean frame's op stream 1.2-1.5 ms, now
  0.4-0.65 ms, 98% of clean frames executed from a recording.
- A universe shown in the UI runs on a thread of its own, no longer in the window's frames: `IUniverseService.Show`
  starts its loop, `RetireRemoved` takes the place of `RunUniverses`, and `SuspendLoops`/`ResumeLoops` hold the loops
  while the devices are made again (`UIApplication.OnDevicesRecreating`/`OnDevicesRecreated`). A `RenderTargetPanel`
  asks for a frame each time it takes one, and the universe draws one per ask into the free one of three surfaces; it
  reads the pointer moving over the panel as a reason for frames under `FrameMode.OnDemand`. The editor, 4K, two
  animated models: the window ran at 100 frames a second with the scene in its frames, now 260-370, the scene 210-290.
- Under `FrameMode.OnDemand` a universe's output in the UI asks frames of itself alone (`UniverseOutput.Invalidate`):
  for the pointer moving over the panel, for the panel shown again - its window restored, the panel back in the tree or
  made visible - after a resize, until the surface of the new size is handed over to the panel, and after a frame
  dropped for want of a free surface, on the panel's next ask.
- `RuntimeStats.WindowFps` and `RuntimeStats.LastWindowGpuMs`: the frames the render thread draws a second and the GPU
  time of the last window frame.
- `Adamantium.UI.FX` compiles the UI's shaders with `Adamantium.Vulkan.Slang` 1.1.0 - Slang 2026.17, for Windows and
  Linux - and `Adamantium.UI` maps Vulkan's library on Linux too, with `Adamantium.Vulkan.Loader` 1.0.3.
- `Adamantium.UI` takes `Adamantium.Vulkan` 1.0.4, the version the engine's graphics take.
- Colors read as text go through the engine's two readers: `ColorPicker`'s hex box (still six or eight digits - it
  commits as it is typed) and the markup language server through `Colors.TryParse`, the language server reading
  `#RGB` and `#ARGB` as the framework now does and no "#" with 1, 2, 5 or 7 digits; a drawing's SVG colors
  (`CanvasSvgColor`) through `Css.TryParseColor` - rounded as browsers round them, CSS's "grey" spellings and
  `rebeccapurple` read, "zero" and an alpha that is no number no color.
- `FontVariationList.Parse` reads axis values with the engine's `FontVariation.ParseList`, one reader for markup and
  documents; a value that is not finite (`wght=Infinity`) is refused now.
- Bindings, a tree's child and expansion paths, a data grid's column paths, a collection view's sort, filter and group
  properties and a docking pane's zone reach the properties of their objects through the engine's
  `Adamantium.MVVM.PropertyAccessors` instead of reflection and compiled expressions, so nothing is generated at run
  time (iOS, NativeAOT). The non-generic components of the framework and of an application are described by the
  generator - `AdamantiumComponent` carries `[DescribeProperties]` - and so are its non-generic view models; an object
  nothing describes (a generic component, a view written in markup, a plain object, a struct) is still reached, through
  reflection, and the log says so once per type. A path is written only through a public setter (an `init` one
  included) - a tree's or a grid's path wrote a private one - and a tree's, a grid's and a collection view's paths now
  see what an object has through an interface and its public fields, but no longer a static property or field.
- Raising a routed event allocates nothing and takes no lock once each element type has been seen: a handler is called
  directly, by a call the UI generator writes for every handler delegate an assembly declares (and every
  `EventHandler<>` of routed args it names with `typeof`) and registers with `RoutedEventInvokers`, so no reflection runs
  and nothing is generated at run time (iOS, NativeAOT) - an element's handlers and an event's class handlers are read
  from arrays replaced whole on change, the class handlers of a type are picked once, and the route needs no list - a
  pointer move through 20 levels went from 4.1 µs and 1.1 KB to 0.3-0.7 µs and nothing. A delegate declared where the
  generator does not run is still called, through `DynamicInvoke`; one the generated code could not name without a
  warning or an error - nested in a generic type, over a type parameter, `[Obsolete]` as an error or with its own id,
  `[Experimental]` - is called that way too. An exception a handler throws comes out as it is, no longer wrapped in a
  `TargetInvocationException`; a handler given a sender or args it does not take throws `InvalidCastException` where
  the call is generated, `ArgumentException` through `DynamicInvoke`.
- `MSAALeveChangedHandler` is spelled `MSAALevelChangedHandler`.
- The menu key opens a text box's context menu under its caret, by the word the menu is about, not under the box's
  left edge; any element can say where by `ContextMenuPointFromKeyboard`.
- A text box's placeholder shows while the box is empty with the keyboard in it too, as a browser's and the platforms'
  fields keep it: a search box opened with the keyboard in it said nothing of what it was for.
- A menu dropped from a button, a collapsed ribbon group's flyout and the application menu open at the button's left
  edge, as Office drops them, not centered under it.
- A click on a ribbon tab picks it and leaves the keyboard in the field being edited, as a ribbon command does; so
  does a menu dropped by a click and put away with nothing picked. Dropped from the keyboard, it stays on the button.
- A key tip's badge hangs below the command's icon where it shows one, not below the middle of the whole command.
- A hover tip goes as its element is pressed and is not drawn beneath a menu or a list that is open, unless its
  element is in it; it takes no Escape and no outside press of its own.
- A ribbon decides its groups' sizes afresh on every pass, from every group whole: the groups give way from the end
  of the tab as before, and then whatever room is left goes back to them - the one given up last first, each to the
  roomiest layout that still fits, a group it does not fit passed over for the next. The answer is the width's and
  the content's alone: room freed at the same width - a group hidden, labels shortened by another language - is taken
  back, and a width misread while fonts were still arriving no longer leaves a group collapsed. The band stood half
  empty beside collapsed groups, and after a switch of language and back a group stayed collapsed for good.
- A gallery is not cut to its bare chevron unless its author allows it (`Ribbon.CollapseToSmall`): past its compact
  size the group collapses into its labeled button, as Office's does.
- A collapsed ribbon group's button is labeled as a large command is - in the text's own color and size, a name of
  several words on two lines (`CollapsedHeader`) - and is as wide as its label needs, `CollapsedWidth` at the least,
  and the band counts it at that width. The label was a gray caption on one line cut to the metric's width. A group
  whose flyout is open is left collapsed while the row around it changes, and decided again when the flyout closes. A
  header drawn by a `HeaderTemplate` is handed to it as written.
- A gallery shows its scroll arrows only when it has rows out of sight; the chevron to the whole set stays.
- `DateTimeFieldSegment` is a `FieldSegment`, which now holds its `Text`, `IsEmpty`, `IsActive` and `Placeholder`; its
  automation peer is `FieldSegmentAutomationPeer`. Enter in a `DateTimeField` (and a picker) takes the part being typed
  before whoever answers Enter reads the value. A picker gives the keyboard back to its field before its drop-down closes.
- `PropertyRow.ValueType` is the property's declared type where it says more than `object`, so an `int?` holding a
  number can be emptied - of several objects, one whose property cannot be empty decides - and a row whose empty editor
  was refused shows again what the objects hold. A `bool?` row toggles for automation too, and reads as indeterminate
  while it holds nothing.
- A ribbon group's columns start at the top of the band, as Office's, their commands `RibbonGroupPanel.RowSpacing`
  (4) apart: Word's two rows of font commands stand at the top, not touching. The whole block was centered in the band,
  its commands touching.
- A `TextBlock` with no height of its own leaves its text where its lines are, whatever `VerticalTextAlignment` says,
  as WinUI's: the themes' `Center` moved the letters down by half the font's descent, so they, the selection and the
  backgrounds came out of the block - over the text below it, by 56 pixels for Poppins at 320. Labels in every theme
  stand that much higher (under 2 pixels at Segoe UI 14). With a height given, `Center` and `Bottom` place whole lines.
  The key tips of every theme set their letter `TextLineBounds="Tight"`, so it stands in the middle of its plate.
- The splitter between docked areas (`PaneSplitter`) is three dots in the middle of its band, in every theme, instead
  of a band lit in the accent color (Fluent, macOS) or a line down it (Graphite): subtle at rest, brighter under the
  pointer, in the accent while dragged. The whole band is still the grab target.
- The designer shows a file with nothing to preview - a `ResourceDictionary`, a style set, a theme, a template with no
  content - as a plate in the middle of the preview ("ResourceDictionary has no preview"), not as an error: nothing is
  wrong with the markup. Only markup that does not build is an error.
- `ToggleButton.IsChecked` binds both ways by default, as in WPF - for every toggle: `CheckBox`, `RadioButton`,
  `ToggleSwitch`, `RibbonToggleButton`, `RibbonRadioButton`, `RibbonDropDownButton`. `IsChecked="{Binding IsMoving}"`
  sets the flag when the button is pressed; it only showed it, so a tool chosen on a ribbon never reached its view
  model. A binding that is only to show a value says `Mode=OneWay`, as the data pager's page buttons, the data grid's
  check boxes and the property grid's read-only ones now do. `{TemplateBinding}`, `{Ancestor}` and `{Self}` stay one-way.
- A binding writes back only what may be written, as WPF does: not a property whose setter is not public, and not a
  control's read-only property. A write back that fails - a converter with no `ConvertBack`, a setter that throws - is
  written to the binding trace instead of thrown: it ran inside the target's change notification, and every listener
  after the binding, a theme trigger among them, missed the change.
- A collapsed control builds its template the first time it is shown, not when it is themed, and keeps it when it is
  collapsed again (`TemplatedUIComponent.DeferTemplate` is true while collapsed). The scroll viewers of the three
  themes start their bars collapsed, and an `Auto` bar waits for a viewport before it shows. A bar with nothing to
  scroll now builds nothing: every text box and list carried two such bars, and the infinite canvas went from 1029
  elements to 807.
- The content of a collapsed content control waits as the control's logical child while nothing shows it - no
  presenter yet, a presenter that is not laid out, one a theme swap let go of: it inherits its data context and gets its
  view model as if shown. Whatever shows it takes it from there, and with it the colors and the font the control's
  state gives it, as before. A tab's page is not held - the tab strip shows it - and a discarded control lets go of
  what it held.
- Nothing in the UI derives from `PropertyChangedBase` any more, on the way to its removal from the engine: the canvas
  items (`ElementItem`, `ShapeItem`, `PathItem`, `CurveItem`, `StrokeItem`, `TextItem`, `GroupItem`, `ConnectionItem`,
  `CanvasFrameItem`, `CanvasSelectionFrame`) derive from `AdamantiumViewModel`, which keeps `SetProperty` and
  `RaisePropertyChanged`, and `Region` implements `INotifyPropertyChanged` itself.
- `Adamantium.UI.Controls` builds with the MVVM generator. The canvas items' stored properties (`Origin` and `Thickness`
  of a stroke, `Origin` and `FontSize` of text, `World` of a path and a frame) are `[Bindable]` partial properties, and
  a resized stroke or text sets them like anyone else instead of writing their fields and raising by hand.
- `Text` and `TextProperty` moved from `TextBoxBase` to `TextBox`. `TextBoxBase` edits the property its control names
  (`EditedTextProperty`), and a control can mask what is shown (`DisplayedText`), keep no undo history
  (`KeepsUndoHistory`) and decide its word boundaries (`WordBoundary`, `WordAt`). Code that read `TextBoxBase.Text`
  reads `TextBox.Text`.
- The editing keys of a `TextBoxBase` read Ctrl and Shift from the key event (`KeyEventArgs.Modifiers`), so simulated
  input drives its shortcuts too; Ctrl with Alt - AltGr - is no shortcut, and AltGr+A types its letter instead of
  selecting everything. The same holds for `TreeDataGrid`'s keys, so AltGr+Z in a cell editor no longer undoes the
  table. Ctrl+Insert copies, Shift+Insert pastes and Shift+Delete cuts, as everywhere on Windows; in a table only a
  plain Insert adds a row and a plain Delete removes the selected ones.
- `WindowCommand` is a view model in `Adamantium.UI.Core` (it was in `Adamantium.UI.Controls`): it derives from
  `AdamantiumViewModel` and notifies its changes, so a derived command is a view model with no notification code of its
  own. Its icon is `IconKey`, the key of a resource the themes show with `{ObservableResource Key={Binding IconKey}}`
  in the caption's or the row's color. `Adamantium.UI.Core` now depends on `Adamantium.MVVM`.
- The quick-access bar's own button and overflow rows draw a `WindowCommand`-shaped item's icon by `IconKey` too.
- `Drawing.Render` and `DrawingImage.Render` take the color to paint `CurrentColor` with.
- A discarded `Popup` leaves what a content presenter in it was handed through a binding - a control's content, a
  menu's page or footer: that is its owner's, and a template swap shows it again. A closed slide panel's content and the
  File menu's pages and footer came back empty after a theme change or a change of the menu's shape. The owners let
  their own go with them: a ribbon its drawer, a slide panel its header, as a content control its content.
- A `ContentPresenter` handed content its owner parked brings it back alive.
- An in-process automation session releases what was let go when it settles, as the application's idle frames do, so a
  test sees an element that came back dead.
- The infinite canvas's icons are keyed `Canvas…` (`CanvasSaveIcon`, `CanvasToolPenIcon`, `CanvasGroupIcon`…; they were
  `SaveIcon`, `ToolPenIcon`, `GroupIcon`…). A theme's resource is found before the application's, so the canvas's
  generic names took the place of an application's own `SaveIcon` or `GroupIcon` wherever it showed one.
- `BrushParser` is an `ITypeParser<Brush>` (it was `ITypeParser<SolidColorBrush>`): the word `CurrentColor` reads as a
  `CurrentColorBrush`, any other text as before.
- `{ObservableResource}` takes its value away when its key changes to one that resolves to nothing, or to none; a miss of
  the key it already shows still keeps the value.
- The focus ring: after a click hides it, the first arrow key shows it where the focus is, as the first Tab does, and
  only the next one moves. Alt shows it too, as across Windows, and marks the keyboard as in use even with nothing
  focused, so what takes the focus next - a menu opened by its key tip - comes up with the ring.
  `KeyboardNavigation.MoveInto(container, ringVisible)` moves the focus in with or without it.
- A focused element that stops being shown hands the focus to its nearest ancestor that can take it, or lets it go
  when there is none (`FocusManager.LeaveHidden`), and a window coming back does not return the focus into a hidden
  element. Keys used to go on into a hidden button - a game's menu closed by its Continue button kept them all.
- `RenderTargetPanel` holds the controls laid over the picture as its children. Only input aimed at the panel itself
  reaches the universe, and a key the universe takes while it is running is not acted on further up: an arrow turns the
  camera instead of moving the focus off the panel. Keys are left to the interface while the simulation is paused.
- Markup is built one way everywhere. What a document means is decided once, when it is lowered to a build plan
  (`Adamantium.UI.Markup.Building`); the generated class prints that plan as C#, and the runtime loader - the
  designer's preview, `VisualRenderer` - runs the same plan by reflection. The loader used to decide everything a
  second time on its own, and what the designer showed drifted from what the application built.
- `AumlLoader.Reconcile` takes the previous `AumlLoadResult` instead of its AST: an edit is now applied by comparing the
  plans of the two versions. Attached properties (`Grid.Row`) and grid definitions written as child elements are
  applied in place too; children a style, a trigger or `x:Load` holds are reported as needing a rebuild.
- A `ControlTemplate` built by the application carries the `TargetType` its markup names, as the designer's always did.
- The markup generator reads only `*.auml` files. It used to take any `*.xml` file handed to it as well, so an XML
  file another tool lists in `AdditionalFiles` was parsed as markup and failed the build.

- Each project of the repository builds into a folder of its own,
  `artifacts/bin/<project>/<Configuration>/<target framework>/`. Before, all of them shared
  `artifacts/bin/<Configuration>/<target framework>/`, so cleaning one project deleted files another needed (the
  sandbox's runtime configuration) and a running sandbox locked assemblies other builds had to write. The sandbox's
  folder holds `adam-auto` as well, so `adam-auto start` with no `--exe` still finds it. The language server and the
  designer prefer a project's own folder over a copy of its assembly in another's. A build with no configuration
  builds Debug. An existing checkout can delete the old `artifacts/bin/Debug`, `Release`, `net10.0` and
  `netstandard2.0` folders once.

- Window places (`FileWindowPlacementStore`) and the sandbox's canvas and docking layouts are kept in
  `ApplicationData.Folder`. The sandbox's layouts move there from `%LocalAppData%\Adamantium`, so the sandbox opens
  once with its default layout.
- `adam-auto`'s one-off commands go through the same typed API as tests do. Some input used to crash the tool; it now
  reports an error instead:
  - a malformed selector path;
  - a command missing its selector;
  - a wheel count that is not a whole number;
  - an unknown dock position.

  `AutomationSession.Find(string)` reports a malformed path as an `AutomationException`.

- The sandbox's models, pictures and fonts live in a content project of their own, `Adamantium.UI.Sandbox.Content`
  (the engine's `Adamantium.Content.Build`). The build cooks the models and puts all of it in
  `Content/SandboxContent/` beside the sandbox; the code finds each file by a constant of `SandboxContentAssets`, the
  markup by its path from there. The hand-cooked `Models/F-15C_Eagle/` copy, the unused `swordman.dae` (whose textures
  were never in the repository), `SandboxAssets` and the manifest `Content.acontent` are gone.

- A layer (`IsLayer` or `OpacityMask`) keeps its texture between frames. On a frame where nothing it draws has changed,
  the texture is laid down as it is, and the subtree is not drawn again. So a group fading or masked by a composited
  animation of its own costs one textured quad a frame, however much it holds.
  - **What counts as a change.** Each frame a print is taken of everything the subtree's drawing depends on: where each
    unit and the layer sit, how each fades and is clipped, the brushes it paints with, the masks of layers inside it, and
    the glyphs landed in the font atlas. A layer that moves, or whose subtree the compositor moves, is drawn again, as
    is one in a frame where a brush was repainted after the frame began.
  - **What is never kept.** A subtree with anything that changes with time or loads later is drawn every frame:
    images, tile and pattern brushes, noise, materials, living auras, copies drawn by `RenderClones`, text whose glyphs
    are still being rasterized. A frame that walks the tree draws every layer again.
  - Each layer has one texture more than there are frames in flight, so the one written is never one an earlier frame
    still reads. A layer not drawn for 120 frames gives its textures up.

- `PointerDevice` decides which button a finger or a pen presses, whatever its platform said. A finger is the left
  button for as long as it lies on the surface, and a pen's tip is the left button. With its side button held, a pen
  is the right button. Controls answer the left button, so a platform that reported a finger otherwise would have left
  them deaf to touch.

- `WindowBase.ScreenToClient` and `ClientToScreen` ask the window's platform worker
  (`IWindowWorkerService.ScreenToClient` and `ClientToScreen`) instead of calling Windows themselves. The controls no
  longer use `Adamantium.Win32` at all. A window with no platform behind it keeps a point as it is.

- A `DoubleAnimation` plays as a two-key `AnimationCurve`, so the render thread plays a transform or an element's
  opacity by itself, the way it plays a keyframe animation: a slide, the tab indicator, a popup's scale-in or a fade
  keeps moving while the loop thread is busy, and the property still follows what is on screen. Several animations of
  one transform's different properties play side by side in its one matrix; a finished one lets go of its own part
  only. A `DoubleAnimation` of a brush's property stays on the loop thread, where the property goes on changing. A
  layout transform, and the render transform of an element that has one, stay on the loop thread too.

- A composited animation's clock starts once the frame that asked for it is recorded instead of when it is asked for,
  so the frame that builds a page sliding in does not use up the slide: the page appears where the slide begins and
  slides from there. `Compositor.StartPending` starts them; the application loop calls it after every frame.
  Keyframe animations honor the same easing past 0..1 that a `DoubleAnimation` always did, continuing the first or
  last segment instead of stopping at its key.

- An element's own `Opacity` animation is composited: the render thread writes it into the element's opacity slot.
  While the render thread is drawing it, the loop's mirrored value no longer re-paints the element every tick.

- The loading skeleton cards of every theme pulse their fill's opacity, started and stopped by the card's own
  visibility, with each card a little behind the one before it, instead of all pulsing one shared brush at once.

- `BusyIndicator.Shimmer` moves a glare band by `ShiftX` under the card's rounded clip instead of sliding three gradient
  stops, so the render thread plays it and it keeps gliding while the loop thread is busy; it looks the same.

- A pointer event is one event along its whole route, as in WPF: an element that handles a press handles it for every
  element after it, and their `OnPointerPressed` is not called. The left-button-down event used to be raised again,
  unhandled, on every element of the route. So a press that a button, a check box or a text field inside a list row,
  a tree row, a grid row or a tab takes no longer selects that row or tab, and a universe hosted in a window or a
  `RenderTargetPanel` no longer gets a press, release or wheel a control in front of it handled. Something that must
  see a gesture whatever handled it subscribes with `AddHandler(..., handledEventsToo: true)`, as drag and drop, a
  list's double click, a `NumericUpDown`'s drag to scrub, an `OverlayWindow` coming to the front and the canvas's grips
  now do.
- Themes and triggers watch `IsPointerOver` instead of `IsMouseOver`.

- A text block of 2048 glyphs or more builds quads only for the part its window shows and a window's height and width
  around it, instead of for the whole text. Scrolled past that band, the frame builds the new band and draws again;
  a copy of the block drawn by `RenderClones` builds the whole text, as before.

- The Editor Pro theme is called Graphite now, so its name no longer reads as the paid controls': the class is
  `Adamantium.UI.Themes.GraphiteTheme.Graphite`, its variants `GraphiteDark` and `GraphiteLight`, its name
  (`StartupTheme`, `IThemeManager`) `"Graphite"`, and the templates' `--theme Graphite`. Nothing else changed in it.

- `Adamantium.UI.FX` compiles the UI's shaders with `Adamantium.Vulkan.Slang` 1.0.12: the same Slang compiler, in the
  package that also ships `slangd`.
- The `TextBox` caret takes the whole height of its line, as the selection does, instead of the band from the
  ascender line to the baseline.
- Text no longer waits on the loop for font files: a `TextBlock` or `TextBox` whose face or fallback font is not
  loaded yet lays out with what is (the family's own face, an empty place for a character from a font still loading)
  while the file is read on a worker, and lays out again when it arrives. A render with no next frame waits as
  before. The sandbox's text page lays out in 0.66 s instead of 1.84.
- Text lines take the font's own height - ascent, descent and line gap - with the engine: a run's background and a
  `TextBox` selection now cover the descenders, and lines of text set in Segoe UI are 1.33 of its size apart, as on
  Windows, instead of 1.13. A `TextBox` no longer adds room under its last line for them. `FontFamily.BaseLine` is gone
  with the engine metric it read.

- Text is shaped with the font's OpenType rules (engine `TextShaper`): ligatures, contextual alternates, marks placed
  on their letters, and a visible box for a character the font lacks. `TextBox` takes its caret positions from the
  layout instead of counting one glyph per character, so the caret, selection and clicks stay right inside a ligature,
  over a letter with marks and around an emoji.
- A text layout of attributed text (engine `AttributedText`) draws each range in its own color, batched or not; text
  without colors of its own draws in the element's `Foreground` as before.
- `TextBox` moves and deletes by grapheme: an arrow, Backspace or Delete takes an emoji or a letter with its accents as
  one, and a click or Up/Down never leaves the caret inside one.
- A ribbon command that cannot run fades its icon instead of recoloring it, so a picture with colors of its own fades
  alike.

- `UIApplication.StartupTheme` is the theme's type, not its name, and may be one of the application's own themes,
  which is added to the themes; `ADAM_THEME` still names one. `StartupType`, `StartupTheme`, `StartupLanguage` and
  `ShutDownMode` are properties of the property system, so a value set in code outranks the blueprint's whatever the
  order. `Run(window)` no longer opens `StartupType` beside the window it was given.
- `ResourceManager.AddSource` registers the dictionaries a dictionary links (`<ResourceLink>` in its `Includes`) with
  it, whether it is given as a type or as an instance, before it, so its own keys win. A dictionary registered from
  code had its links ignored; only `ResourceContext.Resources` honored them.
- A `Type`-valued property takes a type by name as well as by `{x:Type}`: `<StyleInclude Source="EditorButtons"/>`,
  `EnumType="local:Priority"`, `{ResourceLink Source=AppColors}`. The live preview took a bare name but the build failed
  on it in an attribute, and in a markup extension's argument built code that threw "Type parser not found for
  System.Type" when the window was created. `{Ancestor}` takes its types in `{x:Type}` as well.
- An animation whose frames use no more than 256 colors in all - most GIFs - keeps them on the GPU as one byte per pixel
  and a shared palette (`BitmapImage.FramePalette`), a quarter of the memory with the same pixels: the sandbox's
  200-frame 960x540 GIF takes 117 MB instead of 469. Animations with more colors keep their full-color frames.
- `AdamantiumComponent.SetValue(string, ...)` reports a name the type has no property for through `PropertyTrace`
  instead of ignoring it without a word. `ThemeResource.Apply` and `ObservableResource.Apply` report such a name too,
  connect nothing and return null.
- Fluent's flyout acrylic (`FlyoutSurfaceFill`: menus, drop-downs, flyouts) is thinner - tint 0.6, blur 7 - and its
  tooltip one (`TooltipSurfaceFill`) with it - 0.54, blur 5. The blur is counted over a copy downscaled four times, so
  the old 28 reached about a hundred pixels and averaged everything behind into one flat gray: the material read as a
  plain fill. What is behind now shows through as soft light, about as far as Windows' own acrylic blurs.
- The markup compiler refuses `{ThemeResource}` and `{ObservableResource}` on a plain CLR property: nothing there can
  follow a resource. `{ResourceReference}` sets it once. A setter's value is not such a property: a style written in a
  view (an `ItemContainerStyle`, say) keeps the live resource for the style to apply, as a style set always did - it
  used to be connected to the setter itself, which is to nothing.

- `ContextMenu` and `MenuItem` make every row built from data a `MenuItem`, whatever the `ItemTemplate`: the template
  draws the row's header, `ItemContainerStyle` says what the row does. A plain `DataTemplate` used to leave the rows bare
  content - no hover, no check, no command - and only a `HierarchicalDataTemplate` made menu rows. A template that built
  a `MenuItem` itself now puts one inside a row: move what it set (`Command`, `Icon`) into `ItemContainerStyle`, as the
  themes' title-bar and quick-access overflow menus now do. `TreeView` and `TreeViewItem` likewise make a `TreeViewItem`
  for every node and `RibbonTab` a `RibbonGroup` for every group; a tree with a plain template or none used to draw its
  internal row object in place of the node.
- `DockZone`, `PaneKind`, `IDockablePane` and `IRestorablePane` moved to `Adamantium.Navigation`, so a view model that
  places its pane needs nothing from the controls.
- A tool a docking region opened stays in the region when it is closed: navigating to it or `Activate` brings the same
  view model back, and `DockingArea.Activate` brings a closed tool back. The region used to forget it.
- `DockingArea.Panes` includes the panes of the panels put away along the edges.
- Loading a layout keeps open what is open: a document, which a layout never saves, among the documents, a pane the
  layout does not name by its `Zone`. They used to stay open out of sight.

- `IResolvedMember` has `IsStatic`, `IsPublic` and `ParameterNames`.
- `BindingBase.CreateExpression`: each kind of binding makes its own live expression, so a new kind needs no change to
  the binding engine.
- Bindings, `NumericUpDown`, `DataGrid`, `DataPager` and the drawing board write and read numbers and dates by the
  application's language (`Languages.Culture`), no longer by the operating system's culture. With no language set they
  use the invariant culture.
- The controls carry no words of their own; the themes give them in the application's language. `TreeDataGrid.NewRowHint`
  and `PropertyGrid.MixedText` have no default; an `ImageSourceProperty` line takes its tip from the theme; a floating
  docking window of a pane without a header has no title rather than "Panel"; the filter editor's operator and AND/OR
  lists are items of its template, in the order of `DataGridFilterOperator` and `DataGridFilterLogic`.
- Removed `DataPager.PageText` and `PageCountText`, sentences in English: the theme says "Page 3 of 12" from
  `PageNumberText`, `PageCount` and `IsEndKnown`. The search strip of `TreeDataGrid` no longer has `PART_Count`.
- Removed `CanvasInspector.Counted` (now `ItemCount`) and `InfiniteCanvas.DeleteQuestion` and `ClearQuestion`: the theme
  says the question, in the look of `CanvasQuestion`.
- A canvas tool's `Name` and `Description` are keys of the canvas's `Phrases` where it has them, and are said as they
  are where it does not; the framework's tools name theirs so (`Select`, `SelectDescription`). `ICanvasItem.Title` is
  what the person called the thing, empty by default: the theme names what it is by `Sort`. A `CanvasNode` has no title
  by default; its sockets are called by the phrases' "In 1", else by their number.
- A binding finds a property an object has through an interface, a default interface member included.
- Removed `Ribbon.QuickAccessCandidates` and `Ribbon.ToggleQuickAccess`: a list of the ribbon's live commands, and a
  command taking one. A page that listed them took them out of the band. A customization page lists the application's
  commands as data, and the ribbon knows each by its `RibbonQuickAccessEventArgs.Item`.
- The search fields of `PropertyGrid` and of the canvas's node palette clear by the field's own button
  (`TextBox.ShowsClearButton`), saying their own words through `ClearButtonToolTip`. Removed what drew a cross of their
  own: `PropertyGrid.HasSearchText` and the template part `PART_ClearSearch`, `CanvasNodePalette.HasSearch` and
  `ClearSearchCommand` and the part `PART_Clear`. Escape still drops the inspector's search.
- The navigation lifecycle is asynchronous: `INavigationAware.OnNavigatedToAsync` and `OnNavigatedFromAsync`,
  `IDialogAware.OnDialogOpenedAsync` and `CanCloseDialogAsync`, `IOverlayAware.OnOverlayOpenedAsync` replace the
  synchronous methods. A region shows a view model once its `OnNavigatedToAsync` has completed, and stays as it was if
  that throws; the view model it leaves hears `OnNavigatedFromAsync` only then, and not when the same instance is
  reused. A newer navigation of a region cancels one still in progress (`NavigationContext.CancellationToken`), which
  then changes nothing; one started from that region's own lifecycle method - a redirect, directly or through another
  region's - takes its place at once instead of waiting for it. A navigation of another region cancels nothing. The
  view model of a window hears `OnNavigatedToAsync` too, before the window shows - the main window's and one opened by
  `OpenWindowAsync` (`INavigationService.ArriveInWindowAsync`); it heard nothing before. A dialog or an overlay is
  shown once its open method has completed.

### Removed

- The macOS theme's `SegmentedTrackFadeColor` and `SegmentedTrackFadeColorTransparent`: tab strips fade by a mask now,
  into whatever lies beneath them, and need no color to fade to.
- `DateTimeFieldSegmentAutomationPeer`: `FieldSegmentAutomationPeer` is the peer of every part of a field, a date's
  and a length's alike.
- Text drawn into a target of its own and laid over the element (the engine's `FontRenderer.UseDirectTextDraw` off):
  nothing turned it on. `TextRenderComponent` draws the glyphs straight into the pass and is no image any more; its
  `Padding` and `UpdateText` went with that path.
- `WindowCommand.IconData` and `WindowCommand.Icon`: an icon is not path data in a view model; name a resource with
  `IconKey`.
- `AumlAstTemplateNode.TemplateContent`, the template's markup written back out as text for every template parsed;
  nothing read it.
- The text scenario language:
  - `.adam` files;
  - `adam-auto run`;
  - `AutomationSession.RunScenarioAsync` and `RunCommandAsync`.

  Scenarios are `.ascenario` files, built into tests.
- The mouse events and their device, replaced by pointer input: `MouseDevice`, `MouseEventArgs`, `MouseButtonEventArgs`,
  `MouseWheelEventArgs` and their handlers, `MouseButtonState`, `RawMouseEventArgs`, `RawMouseWheelEventArgs`,
  `RawInputMouseEventArgs` and `RawMouseEventType`; `MouseDown`, `MouseUp`, `MouseMove`, `MouseWheel`, `MouseEnter`,
  `MouseLeave`, `MouseDoubleClick`, `GotMouseCapture`, `LostMouseCapture`, the per-button events (`MouseLeftButtonDown`
  and the rest, left, middle and right, down and up, with their `Preview` and `Raw` forms) and their `On...` methods;
  `IsMouseOver`, `IsMouseDirectlyOver`, `IsMouseCaptured`, `CaptureMouse`, `ReleaseMouseCapture`, `CaptureStylus` and
  `ReleaseStylusCapture`. `Mouse` keeps the cursor, the desktop position and `RawMouseMove`; a double click is
  `PointerPressedEventArgs.ClickCount`.
- `SVGParser.Commands` and `SVGCommand`: path data goes straight into the geometry through the engine's
  `SvgPathData.Walk`, the same walk SVG glyphs take.
- Instruments left from leak and flicker hunts, which nothing read:
  - `UIComponent.LiveComponents` and the finalizer it took on every control;
  - the allocation counters `TextBlock` read on every measure and arrange;
  - `LayoutManager.QueuedNow`;
  - the counters of brush links, theme applications, theme routers, trigger activators, binding hooks, template
    builds and removals, parked visuals and animation holders, and `RenderDirty.DeadMarks`;
  - `ADAM_GRAPH_TRACE`, `ADAMANTIUM_NO_RING`, `BatchArena.DescribeSegment` and `RenderCache.LastCache`;
  - the canvas press log (`ADAM_CANVAS_PRESSLOG`, `ADAM_CANVAS_PRESSCALLERS`), with `PropertyGrid.Refreshes` and
    `LayoutTrace.CountCallers`/`CountCaller`, which only it used.

  The diagnostics overlay and what it reads stay.

### Fixed

- An item moved in an `ItemsControl`'s source (`ObservableCollection.Move`) rebuilt every container of a panel that
  does not virtualize, and a child moved in a panel's `Children` left the visual tree and came back: what the
  containers held - a 3D view, a text being typed - was made again. The container now only changes place.
- A drop into a list landed past an item of that list's selection that was not being dragged - after a touch drag,
  whose press selects nothing, or a drag from another list - so it fell one place off where the gap showed it. The
  items passed over are now only those dragged out of the list.
- A universe shown in a panel under a hidden or collapsed ancestor went on drawing it: the output read only the
  panel's own visibility. It reads the ancestors' too, and asks for a frame when one of them is shown again.
- A type whose `ClipToBounds` is on by default did not clip: the value the renderer reads only followed changes, and a
  default is no change.
- A window whose content asks for less than the window - a dialog shown in a window of its own - drew its frame, title
  bar and content at the content's size in a corner of the window, the rest left empty: a root was arranged at its
  desired size. A root now takes the area it is given, without its `Width`/`Height`/`Min`/`Max` (a window's
  `Width`/`Height` are its OS outer size); a window follows its content only with `SizeToContent`.
  `WindowUniverseOutput` arranges its window at the client size too.
- A docking panel out in a window of its own jumped to another place when the theme changed, and the application could
  fail on Wayland: the swap builds the view anew, and the new `DockingArea` closed the old one's windows and opened its
  own. The `DockingWorkspace` now hands the windows over; a floating root of the new arrangement that shows the same
  panes takes its window, open where it is, and only the windows nothing takes are closed. Loading a layout reuses
  the windows of the arrangement it replaces the same way. Panes a region opens in an area not built yet, and panes
  that may only float, are now placed by the layout being loaded and not first where they asked to be: a pane out in
  a window was opened in a second window as well, and the closing of that one discarded the pane - after the swap
  the window showed a dead tab (wrong colors, no changed mark), and docking it back did nothing. A floating pane the
  loaded layout leaves out takes a window of the replaced arrangement too.
- A tool closed before a theme swap - or before a saved layout was loaded - came back open: a layout did not say which
  tools were put away. `DockingLayoutSerializer.Save` takes them (`hidden`) and writes them with the side each comes
  back to, with their restore keys and states; `ReadHidden` reads them, and `DockingArea.LoadLayout` keeps those tools
  put away. A layout also keeps the pane worked in (`activePane`, `ReadActivePane`), so the accent comes back with the
  arrangement instead of landing on whatever was opened last. A load that puts an open tool away raises `PaneClosed`
  and takes the accent off it; a view rebuilt by a theme swap does not announce tools closed long before. A layout
  loaded into an area not built yet is applied once it is built, so markup arriving later no longer replaces it.
- A dialog fitted to its content in a window of its own (`SizeToContent`) was centered at the size it started with,
  then grew from its corner. It is moved back to center as it is fitted, until it first shows or is moved, and kept
  inside the screen's work area.
- A docking layout file with a value of the wrong kind in one of its tables, or a table named twice, threw on load or
  migration instead of being read as far as it could.
- A dialog in the window's overlay sat twice as far in as the same dialog in a window of its own: the view carried a
  margin of its own on top of the overlay's padding. The window `WindowDialogHost` opens wears the class `Dialog`,
  which every theme pads as its overlay window; the window templates now apply `Padding` to their content.
- A picture chosen by its full path - from a file dialog on Linux and macOS - was not shown: a rooted path read as a
  relative URI, and `BitmapImage` only loads an absolute `file` URI.
- Markup hot reload listed a file as changed - and applied it again on save - when the watcher read it half written:
  a save reports itself as soon as it empties the file. A gathered file whose text is again what was applied is
  dropped before it is listed or applied (`MarkupHotReload.ChangedFiles`). Seen on Linux, where inotify reports the
  emptying at once.
- A theme swap scrolled a tab strip back to its start, the selected tab out of view: the new template's strip starts
  unpanned, and the selected tab is now brought into view again.
- A panel put away against a side stayed as wide as its tabs had been lying flat: tabs added while it was docked turned
  when it folded, and the strip, behind its tab panel's measure boundary, kept the old width. `TabStripScroller` now
  re-measures itself when its panel's size across the strip changes.
- A press on a put-away panel's ▾ revealed the panel, whose flyout then fought the list for the press: the ▾ opened and
  closed out of turn. Only a press on a tab reveals the panel now, and a tab picked from the ▾ list reveals it too.
- A dock panel's caption buttons were out of reach of automation and screen readers: `PaneGroupAutomationPeer` lists
  them first, as they come on screen, named "Auto hide" and "Close" (`PaneStrings.AutoHide`, `PaneStrings.CloseActive`
  - it closes the active pane, not the panel).
- A tool panel's tabs did not pan with the wheel: the `TabPanel` - arranged at its whole length and translated by the
  strip - took that length for its viewport and clamped the pan back to zero. Document strips panned only because
  their edge fades' triggers re-measured the strip on every pan. The strip now tells the panel the length it shows,
  and reads the panel's length whenever the panel changes it - the panel is a measure boundary, so a tab added to a
  strip that fitted left the strip unaware it overflowed. A tool panel's strip also measured its tabs unbounded
  through an Auto column.
- A window's size or scale that changed while the render thread was rebuilding its swapchain was lost: the swapchain
  kept the old size until the next resize, so the picture was stretched and clicks missed what they were drawn over -
  seen at the start of a window on a fractional-scale monitor. `IWindowRenderer.ResizePresenterToWindow()` sizes the
  presenter to the window and leaves a change made meanwhile for the next frame.
- A window closed before its first frame - a tooltip or a popup closed by a theme switch - was registered for drawing
  all the same, with a swapchain made on its destroyed surface: on Wayland the compositor then closed the connection.
- Text and lines are sharp at a fractional scale on Wayland: the buffer was the logical size times the scale truncated
  (1066 pixels for 800 at 4/3), one pixel short of what the compositor expects, and was stretched to fit; it is rounded.
- A `PropertyGrid` editor is never shorter than a line of its font with its padding and border: `EditorHeight` was
  fitted to Segoe UI, and a font with taller lines (Noto Sans) had its text cut in Graphite's 19-pixel rows.
- On Linux, the Japanese calendar no longer throws: ICU begins Meiji on 1868-10-23 while .NET supports the calendar
  from 1868-09-08, and the days between are not counted - the `Calendar` and `DateTimeField` start from the first day
  the calendar counts.
- On Linux, the system UI font is the first of Noto Sans, DejaVu Sans, Liberation Sans, Cantarell and Ubuntu that is
  installed, not DejaVu Sans whether it is or not.
- The language server follows a `ProjectReference` written with backslashes on Linux too, and notices a project's
  first build when its output folder and assembly appear at once.
- A command run in an `OverlayWindow` - a slider of a tool window, its focus scope's own - reached nothing: the card
  hung on the popup layer with no owner. It is its host window's logical child while it is shown, as a context menu
  is its owner's, so the command goes on to the field the host's focus is in, and is asked of it. Opened from a place
  whose commands go to a `CommandManager.CommandTarget` of their own - a ribbon's, to its document - it keeps that
  target, so with no field focused what is run in it goes there.
- An `Expander`'s content always opened downward: the Fluent and Graphite templates read no `ExpandDirection`. The header
  now docks to the side the direction names - top, bottom, right or left - and the content opens on the other, with the
  hairline and the chevron following.
- Closing the main window could take the application down: the shutdown's handlers unloaded the services outside the
  render gate, alongside a frame on the render thread and the closed window's own teardown. They run under the gate
  now, once no frame can start.
- A batched fill could be written into the copy the frame before, still on the GPU, was drawing from. A patch a whole
  number of laps of frame slots after the batch last took a copy wrote in place - "the same frame" was told by the
  frame SLOT, which comes round again; it goes by the device's frame count now (`FrameTicket`). And a frame that
  patched and then walked took two copies, the second being the one in flight; it takes one.
- A vector shape filled in the instanced batch was patched in place - a recolor, a shape blanked or replaced, a new one
  placed on a clean frame - in the copy the frames since the last walk were drawing from. The first patch of a frame
  moves every key to the next copy and fills it with what the keys hold, as the other batches do.
- The cue of a finger or pen held still is kept with its contact (a weak table by `Pointer`), and comes down from the
  window it went up in: the cues were one dictionary by pointer id for every window, so two contacts numbered alike -
  test windows side by side - showed one cue between them, and written from the hold timer's thread on a test run,
  it broke when two windows held at once (a test host crashed on it).
- `EventManager.GetRoutedEvents()` lists the registered routed events; it always gave none.
- A hold that an automation session plays settles before its contacts lift, so it starts where no loop runs, and its
  contacts lift even when the wait fails. With no loop running, a hold starts when `LoopSignal` is drained - not on the
  hold timer's thread - and a tick left from a contact already lifted does not start the next hold of its id.
- A `PreviewGotKeyboardFocus` handler that sets `Handled` keeps the focus where it is; one that moves the focus
  elsewhere wins, and the move it interrupted is dropped - it went on, and two elements lost the focus.
- `PreviewGotKeyboardFocus` is raised on the element taking the focus, with `KeyboardGotFocusEventArgs`: it went to the
  element losing it, with args its handlers could not take, so a handler made the focus move throw. `GotKeyboardFocus`
  says where the focus came from - its `OldFocus` was the new element.
- `SizeChanged` of the items a `StackPanel` or `WrapPanel` arranges in parallel, and of the elements inside them, is
  raised on the thread that runs the outermost such arrange, after its items and in their order, also when one of them
  throws: it was raised on pool threads, and a handler on the panel or above it ran there. A panel with fewer items,
  arranged one by one, still raises each as the item is arranged.
- `GlyphScaling` with a minimum of 0 or below is refused when set, as the engine refuses it, instead of failing when
  the text is laid out. `TextBlock.DropCapLines` of 1 or below 0 and `DropCapCharacters` below 1 are refused too: 1
  line set no drop cap without a word. A `DropCapFontFamily` not installed sets the cap in the text's own face, not in
  the default one.
- An element under a `LayoutTransform` is hit, and the pointer read, where it is drawn: a click into a zoomed view
  went where the unzoomed one has that point, as only a `RenderTransform` was undone.
- Windows spelling no longer calls words joined by a math symbol wrong as one ("Shift+Tab" offered as "Shift Tab"):
  each side of the symbol is checked as the word it is.
- A two-way `{TemplateBinding}` wrote nothing (null) into a property that cannot hold it - an emptied length field over
  a `TimeSpan` - and the slot then read as a null value type; it is refused as a `{Binding}` refuses it, the field
  given back what is held.
- A `Viewbox` set its scaled child at its top-left corner, so content scaled to fit one side hung at the top or left
  of the room it did not fill; it is centered in it, as a WPF `Viewbox`'s is.
- Escape on a menu dropped by a click closes the menu: the button's hover tip, opened after the press, was the
  innermost popup and took the key.
- A ribbon tab with a key tip of its own no longer wears a second badge among its commands.
- A ribbon with groups of medium commands settles: a command switched to another size was measured with its label's
  part as it had been, so a group asked for a roomier variant from a small one answered the small width, the band
  flipped between two layouts on every pass and the window never went idle. A size set on a command now has its
  template measured afresh.
- A `SegmentedControl`'s captions selected as text, with the text cursor over them: a segment is pressed, so its
  caption no longer selects, as a button's.
- An empty edit of a property that cannot be empty is refused, and says so - by an inspector row, a table's commit or
  paste, the strip for a new record, an undo or a cell's automation alike: the write landed nowhere, yet was reported as
  written, and the row stood blank beside an object that still held its value. Now the write reports the refusal and
  the row or cell shows what is held; a property that can be empty - an `int?`, a `DateOnly?` - still takes it.
- An `Ellipse`'s `StopAngle` is the sweep it is drawn with, yet it was held to no less than `StartAngle` and the start to
  no more than it: an arc starting at 180 asked to sweep 90 swept 180. Each is now held on its own - the sweep to one
  turn, the start to no less than zero. One that sweeps nothing draws nothing.
- A ring (`Ellipse.RingThickness`) drawn by its own tessellated body turned solid once its angles or size changed, and
  one swept through nothing was cut from nothing.
- A cursor that changes under a pointer standing still - a style turning it into a tool's - shows at once on Windows:
  set from the UI loop it waited for the next move, `SetCursor` showing only from the window's own thread, which is now
  asked to answer `WM_SETCURSOR` again.
- A click into a `RenderTargetPanel` takes the keyboard focus, with any button: only a mouse-look did, so after a
  click on a toolbar the keys - Esc to a scene's tool among them - went on to the toolbar instead of the universe.
- A ribbon group measured at another size than it is drawn at answers that size's width when it holds a gallery: the
  gallery's new column count was template levels down and the levels between kept their old measure, so it answered
  the width it was drawn at, and the band flipped between two choices on every pass. The same held for the group's
  own padding, read off a measure that had not reached its commands: every width it reported was off. A group's width
  at each layout is now the group measured at it, caption included - a caption wider than the commands of a small
  layout made the old sum of commands and padding come out different for every layout drawn.
- A gallery owns its column in a ribbon group at every size it shows cells at: at its compact size it was stacked
  with the command after it, which was pushed out of the bottom of the band ("Update style to match selection"
  vanished beside the style gallery).
- A component whose classes change is styled by its new classes: class styles were matched only when the theme was
  applied, and a style its classes no longer named was not taken off - detaching checked that the style still
  matched, which it no longer did. A seven-segment figure going from 8 to 1 kept its segments lit. Only the styles
  that stop or start applying change; those that stay keep their triggers as they are, so a hovered button does not
  replay its animations. The same classes set again change nothing.
- A `DispatcherTimer` ticks on the application's update loop: made in the loop it ticked on the thread pool, made while
  the main window was built it ticked on the main thread - either way changing the tree while a frame was recorded. A
  repeat button's repeats, a menu's close delay, a drag's auto-scroll and a calendar's midnight ran beside the loop; a
  seven-segment clock showed segments it had turned off. One tick waits for the loop at a time - a stalled loop does
  not run a burst of them - and one queued before `Stop` or `Start` is dropped. `LoopSignal.Claim` names the loop.
- An animation started while its element is laid out for the first time plays from where layout puts it: the first
  frame drew it as if it stood at its parent's corner, and a designer preview kept it there.
- The designer's live preview runs what is posted to the loop between its frames, as the application's loop does - a
  timer's tick among it.
- Taking off a style that never applied leaves the values other styles gave: it cleared the property's whole style
  value - a style removed from an element's own `Styles`, its class absent, took the theme's background with it.
- A checked menu row's label starts on the line of the others, in every theme: the check mark collapsed the icon
  that holds the gutter's width, so the label moved in by the difference; the icon is hidden now, its room kept.
- A font family the system does not have no longer throws: text in it - Word's Aptos pasted on a machine without it,
  an SVG drawing naming it - threw a `NullReferenceException` as it was laid out. `ResolveFont` and `TryResolveFont`
  set it in `UIComponent.DefaultFontFamily`, as a browser does; the family itself keeps its name (`Source`, written
  back into an SVG) and draws in the system's UI font, or any font the system has when that one is missing too. New
  `FontFamily.IsInstalled` says which it is.
- A ribbon gallery's drop-down is put away by a press anywhere else, as every flyout is: it closed only from its own
  chevron or a pick, its popup being left at the default that keeps it open. A press on the gallery's scroll arrows
  beside the open card puts it away too, rather than turning the rows hidden under it.
- A bound key of `{Localize}` or `{ObservableResource}` takes its binding's `StringFormat`:
  `Key={Binding Kind, StringFormat='{0}Styles'}` read the bare value. Such a binding has no target property of its own,
  and a binding without one kept its raw value, as a trigger's condition must; one read as text now says so
  (`BindingExpressionBase.ProducesText`). A bound argument's `StringFormat` writes it in the phrase's language and
  follows a language switch, while its value still chooses a counted or selected phrase's form; a format the phrase
  gives its placeholder (`{size:N2}`) is applied first, the binding's around it.
- An element added under a hidden one stays unseen: a subtree that arrived after its ancestor was hidden was placed in
  the frame as if visible and drew - a calendar's hidden months showed odd day numbers over its year view.
- Content that grows or shrinks by a splice no longer leaves its neighbors pointing at instances that moved with it: a
  paragraph drawing a background or an underline with its text, an ellipse or a gradient fill beside it, or a cloned
  copy went on recoloring and patching where it had been, and the frame verifier saw its glyphs undrawn. Typing into a
  heading above a formatted paragraph raised it. A paragraph whose text comes before its underline also patches again
  after such a splice, instead of walking the scene.
- A window closed in a `HeadlessApplication` is discarded with its content: the discard asked for a region manager the
  headless application did not register, and threw, so the window stayed alive in its brushes for the rest of a test
  run.
- A pointer coming over an element together with its ancestors - from a side panel straight onto a view that fills its
  document - shows the element's cursor: the ancestors were told of the enter after it, and the outermost put up its
  own. Elements joining a pointer's or the keyboard focus's chain hear of it outermost first.
- A `TreeView` with a `ParentPath` follows its selected node to another parent - a camera hung on a part of a closed
  model: the branches above its new place open and it is scrolled to; it vanished into the closed branch. A branch the
  user closes over the selection stays closed.
- Up and Down between two rows of a ribbon group's commands (Word's font boxes over its Bold and Italic) land on the
  command standing over or under the focus; Up landed on the row's last, Down on its first.
- A snapshot of an element - an `adam-auto shot`, a drag's picture - paints its children in the window's order: a
  child a `ZIndex` lowered covered its siblings in it.
- While an element holds the pointer, what the pointer is over is found where the pointer is: the point was taken in
  the window's units as the element's parent's, so in a panel away from the window's corner the hover lit a cell off
  to the side of the pointer, moving with it.
- A `TextBlockOverflow` in a grid's star columns let its layout settle: it took its room each time the grid measured
  it - first unbounded, then the column's width - and laid the block's text out again every time, never idle, the
  overflow under the others as short as nothing and its lines over what came below. It takes its room as it is
  arranged. A block whose overflows have no room yet no longer lays its text out again on every measure (a room not
  known never compared the same).
- A wrapping `TextBlock` measured twice a pass at two sizes - a grid's star column first at an upper bound, then at
  its share - is answered the size it laid out at the other one, not laid out again each time; what it draws and
  arranges is laid out at the size it ends up with.
- A control among the inlines of a vertical `TextBlock` stands upright in its line, centered across it, as large as it
  asks; it was collapsed to nothing.
- An items host with `IsVirtualizing="False"` asks for what its items take; it asked for all the room it was given, as
  a scrolling list's viewport does, so a row of them standing in a taller space grew as tall as the space.
- A `TreeView` told to select a node that has no row yet - an entity made and selected at once - lets the row selected
  before go at once; it stayed selected beside the new one when its row came. The new row comes selected - a node
  keeping its own selection is marked - and, with a `ParentPath`, is scrolled to.
- A property element's text (`<TextBlock.Text>`, `<Button.Content>`) has its white space collapsed to single spaces and
  none at its ends, as WPF reads it: two spaces stayed two, and a text on lines of its own kept its line breaks. In
  any text, a line break between two East Asian characters is dropped, as WPF drops it (`日本`↵`語` is "日本語").
- A property element in the middle of an element's text (`Hello <TextBlock.Tag>t</TextBlock.Tag> world`) is an error,
  as in WPF; the texts either side were joined with both their spaces. One among elements alone stays allowed.
- The markup loader behind the live preview and hot reload reports what the parser finds - a property element amid
  text, an attribute on a property element, xmlns off the root - where it is written, and builds nothing of it, as the
  build does not: it built such markup and said nothing. A live preview whose edit does not build - mid-typing - keeps
  the tree it shows and its running animations, and says why.
- A text style shared by texts in different theme scopes gives each its own answer to a `{ResourceReference}` in it:
  the answer was written into the shared style, so every text took the one resolved last. A style keeps no answer
  now; each text reads it from where it stands, and a scope switched round a text lays it out again.
- A control in a line whose text moved inside it, its size and slot the same, moves on the line too: the block read
  its baseline only when measured or arranged again. It now reads its controls' baselines again after each layout pass
  that did work - those the text reaches; one past a cut stands nowhere and is left alone.
- Ctrl+arrows in a `TextBox` holding mixed text, and in a selectable `TextBlock`, go a word at a time by the screen, as
  the plain arrows go: they went by the reading order, jumping back across right-to-left words.
- A `TextBox` that does not wrap, its paragraphs running different ways, stands each line at its own edge: the whole
  text stood at the edge of its first paragraph.
- White space between two inlines written on lines of their own is a space, as in WPF: `</Bold>`↵`<Italic>` gave
  "ab". A comment in the gap takes nothing away, and one in text adds no second space (`Hello <!-- c --> world`);
  elsewhere white space is still dropped. In a property element (`<Button.Content>`, `<TextBlock.Text>`) the text after
  a comment is no longer lost. Markup that put the spaces in its runs drops them, as the sandbox's text stands now do,
  and writes runs side by side (`.../><Run .../>`) only where no space stands between them.
- The generator's type resolver takes an interface a type implements as one it is assignable to, as the runtime's does.
- In Fluent, a tab whose strip is not on top rounds its outer corners, not those against its panel: a tool's tabs,
  under its body, were rounded on top and left a notch either side of where they join it.
- A tool panel folded to the edge of a docking area takes the area's tab policy, as a docked one does.
- What a docking area says of its panels' strips - the selection bar's side and thickness, the pinned row, now the
  padding - sits beneath what a panel states itself, and is taken back when it is unset or no longer applies: a panel
  kept the last value it was given, and one turned from a tool into a document kept the tools' bar.
- A nullable enum or bool property - a docking area's `ToolSelectionIndicatorPlacement` or `PinnedTabsPlacement`,
  `ToggleButton.IsChecked`, a run's `Kerning` - is checked as the run time parses it: its null (nothing, or `null` in
  any case) and its type's values, an enum's names in any case. The build, the preview and the language server took
  anything, and a value naming nothing was lost where the view was built; it is now an error in all three, and the
  language server (Rider plugin 1.11.3, VS Code extension 0.3.2) completes the type's values under the type's name,
  where it offered nothing. Nullable numbers are still left to the run time.
- `VerticalTextAlignment` set on a vertical `TextBlock` places its lines across a wider block - Top at its right edge,
  Center, Bottom at its left; unset, the first line still stands at the right edge. It did nothing.
- A `TextBox`'s placeholder is set with the box's spacing, tracking, kerning, glyph scaling, features and language.
- A trimmed or aligned `TextBlock` measured against a new width is laid out once: it was laid out again at its old slot
  before the arrange to the new one, three layouts to a change. Measured with no arrange after it, it asks for one.
- An off-screen render leaves glyph filling as it found it, failed or not: it switched inline filling off after it,
  and the designer, which fills inline throughout, then drew its previews without the letters still to come.
- Clearing a property with a check of its values no longer throws: the check was asked about the cleared value, so an
  inherited property with a check threw as its element moved.
- The Rider plugin (1.11.2) runs the designer host from its copy of the project's build: run from where it was built,
  the host held its dll, and a build of the UI failed while a preview was open.
- The designer preview scrolls: the wheel over it is raised on the element under the cursor as the platform raises it
  (the host's `scroll` op), so a list or a property grid taller than the design size shows the rest, smoothly, as in
  the application. Where nothing takes the wheel, and with Alt, the Rider plugin scrolls its view of the frame, and Ctrl
  still zooms; a burst of turns goes to the host as one. A click on a section's header in the preview opens or folds
  it, as a click on a tab header shows the tab; the markup is left alone, and a rebuild of the preview keeps what the
  clicks opened.
  Before, what the design size cut off could not be seen.
- An idle loop no longer throws an `OperationCanceledException` on the thread pool each time its sleep times out: the
  next sleep waited on the pipe afresh, which canceled the wait before. It keeps that wait instead.
- A docking area gone for good takes its panes off screen: their view models hear `OnDeactivated` and `OnHidden`, so
  one that follows something only while shown lets it go, and an area put in its place shows them again. Before, they
  stayed "shown" with nothing on screen and went on following. Its floating windows close with it: when it went before
  an area in its place came, nothing closed them.
- A `TreeView` whose container style keeps no `IsSelected` on the nodes still shows `SelectedItem` selected after its
  row is built again - its branch closed and opened, its children read again - and selects the row of a node added
  after it was chosen. Before, the row came unselected and choosing the same node again changed nothing. A node added
  to an open branch now comes selected, as at a rebuild, when its view model marks it so.
- A `PropertyGrid` narrower than its `NameColumnWidth` leaves the values `MinValueColumnWidth` beside the grip and the
  buttons: the names took their full width and squeezed the values to a sliver in a docked inspector. The width given
  comes back when the grid widens again.
- A `TextBlock` whose inlines were all removed no longer keeps the last of them alive.
- A `PropertyGrid` or `DataGrid` row bound through a converter writes what its editor produced: it was checked against
  the object's property type before the converter had turned it into one, so a color picked for a vector was refused.
  The converter's `ConvertBack` now says whether it fits.
- A page that slid in through a `ContentPresenter` transition gets the presenter's size back once the page it replaced
  has slid out: the presenter kept the size of the larger of the two, so the topic area of the Text tab scrolled through
  empty space after a long topic.
- The frame verifier no longer throws in `TryGetSegment` after a batch let its GPU buffers go: the segments went, but the
  map from a segment's id to its place stayed, and pointed past the end.
- A long text with a text effect builds all of its glyphs: only the band around the window was built, so a character an
  effect brought in from far off was not drawn.
- A transform two drawings and a text effect share, let go by one of the drawings, stays hung on the other drawing and
  binds against its data; it went to the effect.
- A `RenderTargetPanel` being resized - a docked pane dragged by its splitter - no longer loses its picture for a frame
  or two, showing what lies behind it. Its producer hands over a new surface for every size, faster than frames
  recorded with the old one are drawn, and such a frame found that surface let go of and drew nothing. A frame recorded
  with a replaced surface now draws the newest one that replaced it (`SharedSurfaceImage.ReplaceWith`), and a surface's
  import lives as long as a render component holds it (`HoldImport` / `ReleaseImport`). A surface for a new size is
  given to the panel only once it holds a frame, and the one it replaces is freed after the frames in flight instead of
  waiting the whole device idle on every size. An output going away unbinds its own surface from the panel
  (`RenderTargetPanel.ClearSource(descriptor)`), so the panel no longer keeps a surface whose handles are closed, and
  leaves alone one another output has bound since.
- A `RenderTargetPanel` draws a copy of its producer's newest frame (`SharedSurface.LatchCopy`) instead of the shared
  surface itself, which a frame drawn again while nothing new came read as the producer wrote its next frame. The copy
  costs a second texture of the panel's size and one GPU copy per new frame. A panel that leaves the tree keeps its
  import: made again on the way back, it copied a frame the producer was already free to overwrite.
- A window closed by `WM_CLOSE` - the task manager, a session ending, another process - closes the way its close
  button closes it. The message went to the default handler, which destroyed the window under the renderer still
  drawing it: the application never exited, and its render thread spun on a lost surface.
- A window being resized no longer changes the scissor and viewport of the frame in flight. They were edited in place
  on the loop thread while the render thread drew with them, so a frame could clip to a size between two; the frame
  verifier reported it as a clip of the wrong size.
- A `TreeDataGrid` cell over a property with no public setter is read-only. It opened an editor and took the value,
  which the write-back then dropped; a `PropertyGrid` line likewise no longer reports such a write as made
  (`BindingExpressionBase.IsSourceWritable`).
- An `x:Key` that is neither text nor `{x:Static}` is a build error; such an entry was left out of its dictionary in
  silence, so the resource was simply never found.
- A `TreeDataGrid` grouped by a column, with no sort or filter, shows a record added to its source or taken out of it.
  Its groups are lists of its own making and were made again only for a sort, a filter or a key relation.
- A run's `Foreground` or `Background` brush, or a decoration line's brush, changed in place (a new color, an animated
  one) shows in the text; only a brush swapped for another did. An animation of such a brush, or of a text effect's,
  stays on the loop thread: the compositor repaints what draws with a brush directly, and text holds its colors in the
  glyphs it laid out.
- A drawing shown by an element keeps what it holds in step: a brush, geometry, transform or nested drawing set later is
  hung on it too, so its bindings resolve, and one replaced is let go. `DrawingImage.Detach` and a drawing replaced in
  the image let go of everything the drawing hung (`Drawing.Detach`); a transform let go passes to the element it moves,
  or to a text effect still drawing with it. `DrawingGroup.Children.Clear()` lets go of each child.
- An `{Ancestor}` binding on a non-visual target - a line of an inspector, a behavior - resolves when the element
  holding it enters the visual tree later, or gets a parent there. It listened to its own logical attach only, which
  never comes when the element stands in a template: the logical news of a window stops at a template's edge.
- Content that stretches, adopted by a `ContentPresenter` after a deferred build, fills the presenter. The layout pass
  could arrange it on its own first, into its desired size, and the presenter's next arrange at its old size took that
  for "nothing changed": a page opened for the first time stayed as tall as its content asked.
- A text box lays its text out again when its `Language` or the application's language changes: it kept the forms and
  the case of the language it was first laid out in until the caret moved.
- `adam-auto type` hands a character outside the Basic Multilingual Plane on whole, as the platform's window does,
  not as two halves.
- A span refuses an inline it cannot hold (itself, a span it is in) before taking it: the refused inline used to stay
  in its `Inlines` after the exception.
- A paragraph separator (U+2029) pasted into a text box is a line break, a space in a single-line box.
- The designer's loader takes a nullable enum written in markup (`BaselineAlignment="Top"` on a run) without warning
  that it has no type parser: it knew `Nullable` by the name the build gives it, not by reflection's.
- The application goes idle again after a visual brush or a visual snapshot was drawn: a recolor kept for the next record
  is kept for the loop frame of the record that missed it only. The one-off record of a snapshot went through the shared
  scope, and every later recolor in it outlived every clear, so the loop never stopped recording.
- A busy indicator keeps turning while a theme is switched or a view is being built: an animation the render thread
  plays runs for exactly the records made between its start and its stop, so the record still on screen keeps its
  spinner turning while the loop thread is busy, and a spinner restarted mid-swap does not stand still at its first
  value. Before, every spinner on screen stopped until the loop thread recorded its next frame - half a second for a
  theme, a second for the DataGrid page (`Compositor.Frame`, `Compositor.EndFrame`, `Compositor.Tick(into, drawnFrame)`).
- A deferred view that was built before the tab swap finished now puts its loading visual up for a frame before itself,
  so the spinner turns while the loop thread puts the view up (the DataGrid page: 1.8 s with no spinner at all).
- The theme switch's busy overlay shows for the whole swap: the swap waits for the frame that raises the overlay to be
  recorded before it re-styles the tree. Started from a click, it re-styled in that same frame, and the overlay appeared
  only as the swap ended (`Compositor.Recorded`, `Compositor.FrameRecorded`, `Compositor.RecordedSince`,
  `AnimationManager.Ticks`).
- A recycled DataGrid row no longer draws its new text in the colors of the row before it, and a tab strip whose
  selection moves in the same frame as its page changes no longer keeps the old tab's colors: a recolor that arrived
  while a frame was being recorded is kept for the next record, a structural record carries the frame's recolors, and
  packets applied in one frame merge into the larger of their build kinds instead of the last one.
- A skeleton list that stops loading no longer leaves its collapsed placeholder in the paint order: a clone host that
  draws nothing of its own leaves the order when collapsed in a frame that renumbers it, and moves with the order while
  shown. A hidden clone host no longer runs its copies.
- A view switched away from no longer loses its layout one frame before its content stops being drawn: the window
  forgets an element discarded only once it applies a record made after the discard (`DiscardedVisuals.Discards`,
  `FundamentalUIComponent.DiscardedAt`).
- Text with an opacity, or under a faded parent, is no longer drawn too faint once part of the window is repaired in
  place: the repair baked the whole opacity chain into the glyph color while the shader multiplied it in again from
  the opacity slot - a 0.35 label came out at 0.12. Ink, arrows, the canvas grid, polygons and patterns had the same
  fault. Each batch family now says itself whether its shader reads the slot (`BatchArena.ReadsFadeSlot`), so the
  repair and the full walk cannot disagree about it again.
- A docking pane's body no longer goes blank - drawn empty, its labels gone, its commands dead - after switching tabs,
  after the pane is taken out into a window and docked back, or in a tool panel from the start. The body is the pane's:
  the panel showing it lends it a place and lets it go without discarding it, and so does a closing floating window,
  while the pane lives and holds it as its `Content` (true of any `ContentControl`'s element content, so a plain
  `TabControl`'s page too). A closed document's body still goes with it, and a tab closed by its close button in a
  `TabControl` without `ItemsSource` is now discarded with its page. Layout no longer measures a discarded element: a
  tool panel takes its own template, and the first template's content host, measured after it was thrown away, took
  the body from the second.
- A selected docking tab carried out into a window and back no longer loses its caption, and the strip's selection bar
  no longer slips under it: the tab's border drew nothing while it was not selected, kept its place in the paint order
  from before the trip, and once selected drew its background from there - over its own caption. A group that draws
  nothing is now put at its new rank too, and a group whose rank changes moves in the paint order instead of only
  taking the number.
- Text no longer lands under its own background after an in-place repair:
  - **A text box being typed into** (the DataGrid's search box): the caption's new segment went before the first op
    ranked after it, and that was a segment of fills spanning the caption's rank that had not been cut - one holding the
    box's own background. A newcomer now goes after a segment still spanning its rank; a placed segment states its own
    rank as where it begins, instead of 0.
  - **A docking panel's body after a pane was taken out into a window**: one record re-ranked the whole docking area and
    also placed the body on its own, against its parent's rank from before the move; the second placement won. A parent
    is now read at the rank this plan gives it, and what an outer placement carried is not placed again.
- A docking tab docked back from a floating window, and a whole panel taken out into one, no longer arrive thrown away -
  a tab whose caption went missing after a few switches: the window's dropped panel, and the row a carried panel left,
  still held them until their next layout and discarded them with themselves. Panes and carried panels are the
  layout's; only a close discards them.
- A minimized window brought back by `Activate` - a window opened again by navigation - is restored as it was, normal or
  maximized. It came back on screen still reading Minimized, took no sizes and no input, and showed its last frame.
- The File menu's pages no longer come back empty on a second pass, in either shape. The page shown is the row's, not
  the pane's: the menu parks a page while another shows; a row takes its page with it when it goes, and the menu its
  auxiliary pane and footer.
- The row chosen in the File backstage no longer stays lit in the dropped panel whatever the pointer goes to: the
  choice is the backstage's, kept for when it is back, and the panel lights only the row under the pointer.
- The File menu dropped under its button, opened by a click, no longer shows a focus ring on its first row: it takes
  the keyboard in without one, and shows it once a key asks.
- Markup hot reload no longer makes an editor's save fail: it read a saved file without sharing it for writing, so a
  save made while it read was refused.
- `InfiniteCanvas`: the plane follows a theme variant switch. Its ground copied the colors of `Background`,
  `GridBrush` and `AxisBrush` and kept them when the variant recolored those brushes in place, so the plane stayed
  dark in a light window until the camera moved.
- `CanvasPane` folded is its handles. A pane with a width - the inspector - kept that width folded, so its grip could
  not be carried up to the canvas's far edge; the width now waits and comes back when the pane opens, and a layout
  saved while it is folded keeps it. Folding and opening by the grip leave the grip where it stood, and opening moves
  the pane in only as far as it has to fit. A folded pane offers no resize edge.
- `CanvasPane`'s resize edges lie over the edge of the plate, marked by three dots, instead of beside it: shown on
  opening, the left one stood the handles aside and the grip jumped as the pane was folded and opened.
- The canvas inspector follows an item dragged, resized or moved back by undo. `ICanvasItem` is now an
  `INotifyPropertyChanged`, and every item says when its place or size changes - `X`, `Y`, `Width`, `Height` and
  `Bounds` where it has them, a stroke's `Thickness` when a resize scales it - and nothing when they did not change.
- With several things selected, the canvas inspector shows the place and size of the frame round them all - one line
  each, not a line per item - and writing a number there moves or scales the whole selection as one step of undo.
  A drag of a thousand selected strokes took over a second a step in the inspector; it now costs the same as one.
  `InfiniteCanvas.SelectionFrame` (`CanvasSelectionFrame`) is the frame as one object; a section marked
  `CanvasInspector.ReadsSelectionFrame` reads it while several things are selected, and an item's own place and size
  lines are shown only while it is the one thing selected.
- A line of the property grid goes on following its object after a value was typed into it. The value was written
  over the line's binding, so a dragged item left the line showing the number typed.
- A two-way binding no longer writes back into its source the value it has just carried from it. Every change the
  source reported was written into it once more; a value the source took back - a reset, a theme's answer - came
  back as a local one. A target that coerces the value still hands its answer to the source.
- A property grid line standing for many objects asks again only the one that changed whether they still agree,
  and redraws only when what it shows changed: a line over a thousand objects, all moved by one resize, read all
  thousand once for each.
- A binding no longer leaves its source subscribed after it is gone: the one `PropertyChanged` subscription a source
  is read through is taken off when its last binding closes, so an object selected once and then let go does not keep
  a listener for as long as it lives.
- `TreeDataGrid` keeps the column being edited built while it is scrolled out of view, and a commit whose cell is gone
  closes the edit instead of writing null into the record.
- `TreeDataGrid` follows the column being edited when the columns change under it: a column put in front of it no
  longer sends the value into its neighbor, and removing it ends the edit without a write instead of throwing.
  `MoveColumn`, `GroupBy` and `RestoreColumnState` commit an open cell edit first and do nothing when it is refused;
  `MoveColumn` no longer creates a record from the new-record strip on the way. A cell that goes back to the pool
  leaves edit mode, so a column shown again has no editor left in it.
- The new-record strip keeps what was typed under its column, not under the place the column stood: a column
  inserted, removed or moved while a record is being filled in no longer sends the values into other columns, and a
  removed column's value is not made into the record. Moving the column of the open field keeps the field open.
- With values kept in the new-record strip, Enter and Escape on a cell of the table commit or cancel that cell. They
  made a record out of the strip, or emptied it, and left the cell in edit.
- The Scene tab: a numeric box under the frame-rate labels can be scrubbed again, and the game hears the keyboard after
  the menu closes.
- A `RenderTargetPanel` back in the tree shows its surface again; it forgot the surface on leaving, and a tab switched
  away and back stayed empty.
- The designer no longer keeps every tree it has shown. A full preview now closes the window it showed before and
  releases everything the markup built for it, a drawer not opened and a tab not shown included; its frames clear the
  render marks as the application's loop does, which held every element ever marked; and an edit that takes a child
  out releases it. Added `DiscardedVisuals.ReleaseNow` for a surface that draws no frames of its own.
- The designer knows every built-in type a build plan names by its C# keyword (`byte`, `char`, `decimal` and the
  others it lacked), from the one table the plan's names are printed by.
- The designer passes an empty `params` array as C# does. `new PropertyPath("Name")` took the `(object)` constructor
  instead of `(string, params object[])`, so every binding the designer built had no `PathParameters`. Found by a new
  test that builds every sandbox view both ways and compares the trees element by element.
- The designer and markup hot reload read markup about 30 times faster and leave about 200 times less garbage: lowering
  every sandbox file took 9.5 s and allocated 8 GB, now 0.3 s and 39 MB. What reflection says about a type, an
  assembly's types by name and its xmlns definitions are read once and shared; a type name not found is remembered;
  the parser, the structure check and the transformer stopped building strings, lists and iterators they threw away.
  Saving a 700-line view applies in about 0.2 s instead of 1.5–2 s.
- An animated picture nobody disposed no longer destroys its frame array and palette on the finalizer thread: they
  are retired to the device and freed once no frame in flight can sample them, as a disposed picture's are. Freed on
  the spot, they could vanish under a frame, and left the resource registry in the middle of another test
  (`GraphicsResources_Unregister_OnDispose`).
- An event set in markup (`Click="OnClick"`) is subscribed. The generator asked the event's TYPE whether it was an
  event, never the member, so the handler was written as a plain assignment and did not compile; the designer
  dropped it.
- Markup that names core types through a facade (`clr-namespace:System;assembly=System.Runtime`, as the themes'
  metrics do) is built by the designer: the types a facade forwards were not found by the runtime loader, nor by the
  generator outside a reference pack.

- Deleting an `.auml`, `.alang` or `.ascenario` file rebuilds the project: the build counted these files only by their
  dates, so with one gone it stayed "up to date" and kept the class, table or test generated from it.

- Memory held after theme swaps and tab visits.
  - **A view's content is discarded with the view.** A theme swap rebuilds a host's template and a view's own one. The
    view's content (a `View` written in markup) could then sit between the view's old presenter and its new one, which
    had not taken it up yet. It stayed alive out of the tree, and a behavior on it never detached. The sandbox's 3D
    view left a whole universe running for each swap. A `ContentControl` that is discarded now discards its content
    element when that element is out of the tree. An item container lets go of an item that is an element of its own
    before it is discarded, so the list can show the item again.
  - **Layout work queued before a subtree joins the tree moves with it.** A template is built without a parent, under
    a layout manager of its own. When the subtree joined a window, that manager kept its queue and the elements in it
    for good, and the managers of discarded roots linked one generation of templates to the next
    (`LayoutManager.HandOver`).
  - **Content a presenter built is discarded with the presenter.** A label made for string content, or a view built from
    a data template, used to be let go of and left alive, still listening to the resources.
  - **The render cache lets go of released elements.** The adorner layer's cache never reconciled, so it kept every
    element it had drawn. Every cache now drops the layout it froze for an element once a packet recorded after the
    element's release has been applied.
  - **Drawings follow their brushes weakly.** A `GeometryDrawing` or `GlyphRunDrawing` subscribed to a theme brush's
    `Changed`, and the brush lives as long as the theme. A brush now takes only elements as owners; a run of text or a
    drawing no longer becomes one.
  - **A released element's animations stop.** Parking a page stopped them, discarding one did not: an endless animation
    on a page torn down by a theme swap went on playing, and through it the whole page stayed in memory.
  - **The render cache hands back the opacity, clip and transform slots of released elements.** The adorner layer's
    cache never did, so each slot kept its element and everything that element reached.
  - **A destroyed `InfiniteCanvas` discards the controls it built around the application's objects.** They were only
    taken off the scene and stayed alive, listening to the resources.
  - **A kept view no longer holds the presenter that built it.** A theme swap rebuilds the presenter a kept page was
    built in; the parked page went on naming the old one as its templated parent, and through it the whole old
    template. The presenter that takes the page up now becomes its host (`TemplateResult.Rehost`), and what a presenter
    inside the page parked moves with it (`ParkedVisuals.MoveOwner`).
  - **A discarded element that linked a Global dictionary is no longer kept by it.** The dictionary stays, as global
    resources outlive what linked them, but its owners kept every copy of a page that linked one - one per visit
    (`IResourceManager.ForgetGlobalSourceOwner`, `ResourceProvider.ForgetOwner`).
  - **The render cache lets go of layouts it read live** from an element released since, at once. The adorner layer
    records only when its adorners change, and until then kept every released ancestor of an adorned control.
  - **A discarded `Popup` discards its content.** The content is not a visual child, so a template the popup is part of
    neither reached it when torn down nor counted it among its parts. A docked group's reveal flyout outlived the group,
    its localized buttons listening to the resources and holding the group. What the content shows for others stays: the
    items of a list or menu in it, and anything back in a tree. `DiscardedVisuals.PublishSubtree` publishes an element
    and everything under it.
  - **Overlay stages record again once released elements are all let go of**, so their caches drop what they kept for
    them; an idle stage recorded nothing and kept it.
  - **A closed window is discarded with everything in it**, its focus ring and other adorners among them, and its
    entity leaves the application's world. Kept alive, it stayed an owner of every theme brush it painted with - a
    window is its own root, so to a brush it never leaves a tree - and its renderer queued frames nobody drew, about
    14,000 a run of the sandbox's scenarios.
  - **The pointers let go of released elements** - what they were over, hovered, clicked last or were captured by. The
    window the pointer last touched stayed alive after it closed.
  - **An adorner layer discards the focus ring, hover frame and selection frames it replaces.**
  - **A render scope whose surface is gone takes no marks and drops those it held** (`RenderDirtyRouter.Forget`). A
    context menu a style shares among windows still named the scope of the one it was last shown in, and kept what was
    marked there.
  - **A discarded items control discards its own items** - elements put in it, not from a source - shown or not: the
    tabs of a closed ribbon that were never selected stayed alive. **An element discards the whole of its own context
    menu**, its template with it.
  - **A virtualizing panel discards the skeleton card it drops on a regenerate**, and the drop placeholder with it. Built
    from the theme's template with the panel as its templated parent, the card lived on out of the tree, listening to
    the resources and holding the panel - and with it each list the Layout page threw away, about 60 MB a visit. A
    placeholder dropped that way was also never shown again; it is built anew.
  - **An items control's automation peer lets go of the stand-ins of items it no longer holds.** It kept one for every
    item the control ever held while automation looked at it.
  - **Popups, adorners and live snapshots compose their transforms afresh on every record.** The memo was never cleared
    for them, so it kept every element they had shown. A closed menu kept the whole table it had been opened over, ten
    thousand rows in the sandbox, on every visit.
  - **A data grid's sorting and grouping strips discard the chips that fewer keys no longer need.** Only taken out of
    the strip, a chip went on following the theme and, through its column, held the whole table after the table itself
    was gone.
  - **A property grid discards the rows a pass no longer wants**, such as the parts of a composite folded again. They
    held the grid and, through their reset buttons, stayed owners of the theme's brushes, so each kept its page. **It
    discards the sections it built** for an object no longer inspected or for a type no longer selected, and, when it is
    discarded itself, those out of the tree, such as the ones a search leaves out.
  - **A `Path` lets go of its geometry when it is discarded.** It followed the geometry's changes until it was given
    another one, so an icon a view model hands out kept every path that had drawn it, with its page.
  - **A `Slider` and a `RangeSlider` discard their value badges with themselves.** A badge is not in its slider's visual
    tree; it stayed alive, following the theme, and held the slider.
  - **A discarded `Popup` closes.** One discarded while open - a slider torn down mid-drag, its badge up - stayed on the
    window's popup layer, holding its owner. An overlay closing no longer gives the focus back to an element gone from
    its window since, which the focus manager then held.
  - **A canvas inspector discards the sections the theme made for it that it was not showing** when it is discarded, and
    the whole set when a new template replaces it. Out of its tree, their rows held the panel and followed the theme, so
    every canvas page visited stayed in memory.
  - **An `InfiniteCanvas` discards the dialog it asks a question in once it is answered**, and closes and discards one
    still open when the canvas itself is discarded. Closed and kept, its buttons followed the theme and, through the
    question, held the canvas and its page.
  - **An `InfiniteCanvas` discards its node controls with their templates.** Only the control itself was discarded; its
    template - sockets and labels following the theme - lived on and held the node and the canvas, about 11 MB a visit
    to the sandbox's canvas page.
  - **An `InfiniteCanvas` page opened again shows the drawing's own items, and its history undoes onto them.** The
    scene and history belong to the page and outlive the canvas, but every canvas made new items for the application's
    nodes and objects. An undo there - of a clear made before the page was left - put the old items back, wearing the
    old canvas's discarded controls, and their wires ended on dead sockets and left the graph. Now each node or object
    has one item on a scene: the canvas showing it builds its control, and lets it go when discarded. A wire finds its
    sockets through its nodes' current controls. One canvas shows a scene at a time: a canvas handed a scene takes it
    from the one showing it, which lets go of it at once rather than when its discard is drained, and takes it back
    when it is put on screen again. A canvas handed another scene leaves the items of the first behind without its
    controls. An item no canvas has given a control - one an undo put back for an object the application has let go
    of since - cannot be pointed at, framed or selected.
  - **A picture swapped for another of the same size is drawn as the new one, and no longer takes the application
    down.** Nothing but the texture changed, so the frame patched the element's record in place - and the recorded
    segment binds one texture, the old one. The window kept showing the old picture (a second live snapshot on the
    Visual -> Image page showed the first), and once nothing held that picture and its texture was freed, the next
    replay drew a freed texture and the process died. A textured, material or pattern fill now patches only while what
    its segment binds - the texture, a material's pass, source and mapping, a pattern's kind - is what it was recorded
    with, and the render cache keeps the brush of what it recorded, so nothing it paints from is finalized under it. A
    pattern turned into another kind used to keep drawing the old one the same way.
  - **A vector picture's bake is no longer freed under the elements drawing it.** A bake thrown away when its drawing
    changed, or when the drawing was baked at too many sizes, re-records the elements that draw bakes. A
    `BitmapSource` thrown away while the render thread is about to draw it has no texture to give
    (`GetOrCreateTexture` answers null, as for a picture still decoding) rather than throwing on that thread.
  - **An element a `VisualBrush` painted from is no longer kept for good.** The raster kept every source it had ever
    watched in a set of its own and never took one out, and a source that lived on kept the elements thrown away after
    drawing with it. A source that changed waited in another set for a wake that, once its page was gone, never ran.
  - **The cache every off-screen picture shares lets go of the trees it drew.** It kept every group it had ever drawn in
    a map nothing read, and the elements the compositor moved in its last frame until it drew again. With a shared
    glow or shadow and a theme brush also holding on (below), a pass of the sandbox's brushes scenario kept about
    12 MB of managed memory; it now keeps none.
  - **An element gives up exactly the brushes it holds when it is discarded.** It gave up the brushes its properties held
    at that moment, so a theme brush taken by any other way kept the element for good, and with it its page. Each
    element now lists what it is an owner of (`IRenderAttachable.Release`), at no cost to painting or recoloring.
  - **A shared `Aura` or `Shadow` lets go of an element when it is discarded.** The element listened to it for as long
    as the glow lived, so one handed out by a view model held every page lit with it.
  - **A closed window takes its regions and kept pages with it.** A region lives in the application's manager, so the
    tabs a closed window showed stayed in it, and the next window asking for the name opened on them. Closing a window
    now removes each region declared in it by name that no other window shows (`IRegionManager.RemoveRegionAsync`) -
    a region a view model hands over with `Region.Source` stays the view model's: its view models
    hear `OnNavigatedFromAsync` and its journal is emptied. The view model of a window opened by `OpenWindowAsync`
    hears `OnNavigatedFromAsync` when the window closes (`INavigationService.LeaveWindowAsync`), as it heard
    `OnNavigatedToAsync` when it opened. A page kept between visits (`x:KeepAlive`) goes when the control that kept it is
    discarded; it waited to be evicted, and one kept as `Required` stayed for good.
  - **A binding's converted value is kept once per item and property, not once per converter.** A converter written as
    `Converter={conv:ToBrush}` is a new instance on every element, and each was kept as a key of its own with the item,
    so an item kept a brush for every element ever realized for it - a pass of the sandbox's scenarios kept about 7,000
    brushes more each time. The converter's parameter now tells two bindings apart too: one converter on one property
    with two parameters handed both the first one's value.
  - **A closed overlay window lets go of what it was shown on.** The manager discards the card and the dim it made for
    that showing, and a window the framework made for one dialog, overlay or question is discarded with its view once
    closed. Kept, the window's frame stayed an owner of the default overlay brush, which lives as long as the
    application, and held the dialog's view for good.
  - **Parts made for a moment are discarded when they are done.** A data grid's total cells of a column gone and a drag's
    insertion cue as the pointer moves on were only taken out; each went on following the theme and was kept for good. A discarded docking area closes
    the window its compass is shown in - a real window, with its renderer and GPU memory, only hidden between drags.
  - **A closed document's pane is discarded, and a docking area discards the panes it holds out of sight.** A document
    closed is gone for good, and so is the pane a docking region made for a view model that left it, but each was only
    taken out, and a tool put away or a tab never shown was never reached
    by the discard of its area. Each went on following the theme and the language, holding the view it showed.
  - **An element's context menu made by its template is discarded with it, and so is a ribbon drop-down's menu.** Only
    a menu set on the element itself was; one a template made for that very element was treated as one a style
    shares and only unhooked, and a closed ribbon window's application menu kept its modules and commands for good.
  - **A released element is not subscribed to a resource again.** A style or trigger still running on it as it went - a
    pane's selection undone while its region let it go - connected it to the resource manager, which outlives
    everything, and nothing closed that connection.
  - **A tab control showing a region follows it until it is discarded.** It let go of the region when it left the tree,
    so on a kept page it stopped following its region after the page came back, and one never in a tree never let go.
  - **The focus manager knows focus scopes only weakly.** A scope dropped without leaving its tree stayed in the map of
    what each scope focused last.
  - **A `ContentPresenter` lets go of its content when that content is released or taken by another parent**, told so
    by the content itself (`FundamentalUIComponent.OnVisualChildReleased`, `DisownVisualChild`). It used to be found
    out by a static register of every presenter, walked whole on every batch of discarded elements; the register is
    gone. A root another parent took is no longer named by the presenter it left.
  - **A tip set as an element works every time it is shown.** The shared tooltip card discarded the tip it showed as
    soon as it showed another, so after hovering from one element to the next a tip came back with dead bindings, or
    not at all. The card now hands a tip back to the element that set it. It also lets go of the last tip and of the
    element it was for as it hides, and comes down when that element or its tip is discarded while it is up.
  - **A canvas tool rail discards the buttons and tips it replaces** when it is rebuilt - on every mode switch - and a
    family's list, with its rows' tips, once it closes; discarded, it takes its buttons' tips and an open list with it. Only taken out of the
    panel, each button's picture followed the theme and held the button, its tip and the rail. A pass of the sandbox's
    canvas scenario kept about 6.5 MB of managed memory; it now keeps about 0.3 MB.
  - **The canvas inspector no longer hands a new selection to the lines of the old one.** A wire picked after a node had
    the node's lines read its kind and title off the wire, one binding error each.
  - **A discarded adorner leaves the element it adorned as its inheritance parent**, and a hold's cue is discarded as
    the hold ends. The element listed every adorner it had worn as an inheritance child for as long as it lived: each
    focus ring a button had shown, each hold cue of the session on the window.
  - **A docking area discards the panel groups and split hosts it drops** when a layout change leaves their nodes
    behind, and **leaves its `DockingWorkspace` when it is discarded.** Kept out of the tree, a dropped group's buttons
    followed the theme and held the view model they were bound to; the workspace the view model keeps held the area -
    each docking page visited stayed in memory.
  - **Popups, adorners and live snapshots hand back the clip, opacity and node slots of what they no longer show.** It handed them
    back only for elements thrown away, so a closed dialog went on holding the canvas that had asked its question, and
    the page around it.
  - **The render cache lets go of what a splice staged once the splice is done.** The stage was kept until the same
    family was spliced again, and with it the controls last spliced and everything they reached.
  - The render cache clears the buffers of its structural pass after each frame.
  - `FundamentalUIComponent.ReleasedInBatch`: which batch of discarded elements released this one, 0 while it is not
    released. A released element is final; nothing brings it back.

- An application whose GPU device was lost makes a new one and draws again, in a few seconds and without input. Every
  window used to stay blank until the application was restarted.

- The sandbox's theme chooser shows the theme in use when the theme was changed some other way. It kept showing the one
  it was opened on.

- `InfiniteCanvas.BringIntoView` brings something wholly out of view to the middle. Moved only as far as the edge, a
  node not yet built - a point, having no size - stood on the edge, just outside what the plane looks up, and stayed
  unbuilt until the camera moved again.

- Ink no longer leaves single dark pixels inside a stroke. Where two of its segments were all but equally near a
  pixel, each segment's quad measured that pixel from its own interpolated position, and the two could each leave it
  to the other. The ink pass now measures from the pixel itself, the same numbers for every quad.

- The antialiased edge of a shape filled with a conic gradient took the gradient's colors as if it were linear. The
  gradient's parameter now comes from one shader header, `GradientMath.fxh`, for the fills, their edges and the
  opacity mask.

- `FractalView` follows only the pointer that began a drag. Two fingers on it used to pan by the distance between
  them.

- A flick of scrolled content carries on at the speed the hand moved. That speed is now measured by the times the
  moves were made, not by when the loop handled them. Several moves handled in one frame read as a speed no hand
  has, and sometimes flung the content far past where the gesture ended.

- With two pointers at once, one leaving an element no longer acts as if all had. A submenu stays open, a tooltip
  stays up and a tree row stays lit while another pointer is still over it. A drawing canvas keeps a pen's stroke from
  the mouse passing over it until the pen lifts.

- The data pager's page row changes in place as the page moves (`PagerPageItem` now raises `PropertyChanged`). It was
  rebuilt on every turn, so the clicked page button left the tree with the focus, and the next Tab started from the top
  of the window. A focused button that comes to show another page as the row shifts hands the focus to the page turned
  to, and a clicked gap no longer stays marked beside the current page.

- A control a data template built wears the focus ring itself. The ring climbed to its templated parent, the presenter
  showing the item, and took in the control's margin - the data pager's page buttons wore a ring wider on one side.

- A focus ring under a scaling ancestor (a zoom box, a scale transform) keeps its whole standoff at the edge of a
  scroll area. The viewport admitted the adorner's `ClipStandoff` in world units as is, while the ring is drawn scaled,
  so a control flush against a zoomed viewport's edge wore a ring cut by the scale.

- A two-way binding whose converter answers `UnsetValue` on the way back writes nothing to the source. It used to write
  the marker, which threw inside the target's change report, and every listener after the binding missed the change:
  ribbon tools bound to one choice (`EnumToBoolConverter`) all kept their checked look as the choice moved between them.

- A ribbon group filled from data (`ItemsSource` + `ItemTemplate`) behaves as the same commands written out: each item's
  command is the control its template builds, bound to the item, rather than a presenter around it. The size the group
  chooses, the quick-access key and menu reach the command, its own `MaxSize` and collapse limits are the ones obeyed,
  and automation sees it by its name - before, all of it stopped at the presenter, and the group went from its roomiest
  straight to collapsed. A command given back by a removed item goes only to an item its template builds (a
  `DataTemplateSet` with several), and without the item's data, quick-access identity, or a check a press left on it
  that no binding holds. That identity stays with the
  item, so a command rebuilt on a move or reset is still the one in the quick-access bar. `RibbonRadioButton`s from data
  are one of a set too: a press clears the other tools of the group, where before it looked for them among the items,
  which are data, and left two checked. Sandbox: the ribbon's Mesh group is now data.

- Zooming an infinite canvas no longer makes its shapes jump and fly past the canvas's edge for a frame or two (or for
  good). Which controls left the tree is now decided where the frame is recorded and carried on its packet; the render
  thread used to ask the live tree while the canvas was moving its controls from one layer to the next, dropped their
  layout, and froze them on a layer that had no parent yet.

- A page that was closed - a gallery tab switched away from, content replaced - stayed in memory for the life of the
  application, and every page visited added to it. What kept it:
  - a control following a collection or an object of the view model it was given (`TreeDataGrid`, `TreeView`,
    `ListBox.SelectedItems`, `PropertyGrid`, `Ribbon`, `DataPager`, `InfiniteCanvas`, `Polygon`, `Polyline`, a table
    row and its record): they now follow through `FundamentalUIComponent.Follow`, and a discarded element lets go of
    everything it follows, changing nothing in it;
  - the containers a list dropped on a reset: they are announced discarded now, so what is inside lets go too;
  - a navigation region shown in a `ContentControl` or an `ItemsControl`: the region lives as long as the application,
    and its host stayed among its listeners;
  - an `InfiniteCanvas` showing the application's scene: a discarded canvas takes the node controls and wires it put
    on the scene off it again, leaving the graph as it was - the scene kept the canvas, and a canvas shown on the same
    scene again laid a second set beside the first;
  - a table row following a record that reports only its errors (`INotifyDataErrorInfo` without
    `INotifyPropertyChanged`);
  - an `Image` showing a shared `DrawingImage` (a theme icon): the icon kept the first image that showed it;
  - an `{ObservableResource}` that a trigger set after the element was unloaded;
  - a theme brush, which lists the elements painting with it: a discarded element now leaves the list even when it
    never left a tree to give it up there, and an element that came back into a tree after the brush it inherits had
    changed no longer takes it twice;
  - a context menu, which is not in the visual tree: one bound to a view model's collection kept the element it belongs
    to. An element's own menu now goes with it, and one its style shares with others is unhooked from it;
  - the runs of a `TextBlock`: they are not in the visual tree, so a discarded block left them painting with their
    brushes, and a theme brush kept each run and the block with it;
  - the node controls an `InfiniteCanvas` built for the application's graph: their bindings to the nodes stayed open,
    so every node it ever showed kept its control, the canvas and the page;
  - the drawing raster (`DrawingImage` baked for an `ImageBrush` or a nine-slice), which kept every element that ever
    asked for a bake so a repainted palette could redraw it - it holds them weakly now;
  - a `DockingArea`, which listened to its window's closing and activation and never stopped;
  - a window's layout manager, which kept the elements of its last layout pass in a reused list until the next pass;
  - two registries kept for a leak hunt - one weak reference per layout manager ever made and per brush that ever had
    an owner - which only grew;
  - the render cache's fallback for composited animations, which remembered every element ever animated - it keeps
    only the ones animated now;
  - the render cache: units of a removed control in its pre-render list, the stroked shapes of a longer earlier frame
    in the instanced fill's records, the motion nodes of the last scroll, and the node of every material (acrylic,
    mica) any earlier frame drew.

- A `ListBox` thrown away with its page emptied the view model's collection bound to its `SelectedItems`, and any list
  thrown away ran its selection logic once more, raising `SelectionChanged` for nobody.

- Recoloring a cloned subtree - the loading skeletons a virtualized list draws, pulsing - walked the whole scene on
  every frame of the pulse: the render cache remembered one copy's records. It remembers each copy's now and repaints
  them all in place. A shape that wears a shadow or a glow its outline cannot carry (an arc, text) no longer walks
  the scene on every repaint either.

- An animated `BitmapImage` built its frame texture from every frame's full-color pixels at once - 400 MB for a 200-frame
  960x540 GIF, thrown away the moment it was uploaded, on every visit to the page that shows it. The frames are now read
  and indexed one at a time through a single buffer; only an animation of more than 256 colors still needs them all.

- A rectangle, ellipse or polygon drawn turned or slanted kept the color its brush had when it was first drawn that way:
  a brush recolored in place (a palette variant) never reached it.

- The designer preview: a value-type element with its value as text (`<Color x:Key="Accent">#FF0000FF</Color>`,
  `<Thickness>1,2,3,4</Thickness>`) is the value its text says - `Color` no longer resolves to a nested type of the
  same name in a referenced assembly, and the text is parsed as the generator parses it.

- Render clones (the loading skeleton cards): outside a scrolling area every copy was drawn at the last copy's place,
  a repaint without a full redraw - the skeleton's pulse among them - reached one copy of a child and no copy of the
  card itself, and a child that began drawing more shapes lost its other copies. Each copy is drawn at its own place,
  and a change to cloned content redraws the frame.

- A control that stopped drawing one of its vector shapes could get it back on the next recolor of its brush, drawn
  over the record of the control after it, which went missing.

- A frame where a control with a stroked vector shape changed its shape count patched the other controls first and
  only then fell back to a full redraw; it falls back before patching anything.

- A selection band drawn round something inside a larger hollow outline - a shape with no fill, a freehand loop, a
  comment frame - took the outline too: the band was matched against each item's box. It is matched against what the
  item draws now (`ICanvasItem.Touches`, the box by default), the way a press already was; a band still takes whatever
  it touches, so a long stroke is taken by a band round part of it.

- A frame whose batch buffer was too small dropped shapes filled with a gradient, a pattern, noise, a fractal or a
  material for that frame - shapes already on screen blinked out - since only the batches paint those brushes. Such a
  frame is now held back (`IGraphicsDevice.WithholdFrame`): the window keeps the frame before it, and the next one,
  its buffers grown to hold everything, draws the scene whole.

- A control that drew one more vector icon (a `DrawingImage`) kept its old icons drawn too, and showed a stray copy,
  until something redrew the window: the patch could not find the instanced batch's recorded draw and left the old run
  in place. It replaces the run now.

- Controls in a `SlidePanel` or another popup kept the previous theme's templates and styles after a theme switch;
  only their colors changed. Content a new template re-homes takes the new theme, open or closed.

- An element under a collapsed one was measured and arranged on its own when it changed, at the size it asked for,
  so a hidden control behaved as if it were on screen. Its layout now waits until the collapsed element is shown.

- An open popup's content kept every element it had asked to be measured or arranged - removed ones too - for as
  long as the popup lived: its layout queues were never drained. A measure a list in a popup put off to the next
  frame, to finish filling, never ran either; it runs on the next frame now.

- An inherited value (font size, color, font) changed on an element stopped at a node the property is not registered
  on - an inline between a text block and a control set into its line - so the control kept the old value; and taking
  a child away from such a node threw. Such a node now passes the change on.

- A canvas node on screen could not be scrolled into view by automation - only its off-screen stand-in could - so
  `adam-auto scroll` on a node failed whenever the camera already showed it, and the InfiniteCanvas scenario stopped at
  its first step. The node's own peer brings it into view too now.
- A designer edit inside a collection the element owns (`<TextBlock.Inlines>`, `RowDefinitions`) added the edited items
  to the ones already there, so the preview showed them twice; children written into a `[Content]` list were not
  updated at all. Both are rebuilt now.
- A `TextBlock` laid out again with the same text and size - a new last-line alignment, single-word justification,
  kashidas switched off, a run's baseline shift - kept its old glyphs on screen: the renderer told text apart only by
  its string and size. It compares the layout's revision too now.
- A text line thinner than a pixel - the strikethrough of 12 px Segoe UI, the underline of a small footnote mark - could
  fall between pixel rows and not be drawn. `TextBlock` draws underlines and strikethroughs on whole device pixels, at
  least one thick.
- `WrapPanel` arranged each item of a horizontal line at its own height, so `VerticalAlignment` did nothing: a label
  beside taller buttons stuck to the top. Each item now gets the line's height and stands in it by its alignment.
- A `{TemplateBinding}` on an attached property in a template - `ToolTipService.ToolTip`, `AutomationProperties.Name` -
  bound the part's own property of the same name instead: its `Name`, or its `ToolTip`, which no trigger of the
  template could then override. It binds the attached property now, and the designer previews it.
- `TextBox.MaxLength` with room for half of an emoji inserted a lone surrogate; it inserts none of the emoji now.
- A virtualized list that wants its rows kept the height of the rows it had: a folder opened in a tree with a
  `MaxHeight` scrolled its new rows under the tree's edge. It now grows with them, up to the slot.
- A list nothing scrolls - an `ItemsControl` on a scrolling page, measured unbounded - realized a default screenful of
  rows and left the rest of its height empty. A virtualizing panel arranged larger than the window it realized now
  measures again with the size it was given, as `StackPanel`, `WrapPanel`, `UniformGrid` and `TabPanel` alike.
- `VisualRenderer`'s first picture lost its rectangles - a panel's background, a border - when the window finished a
  frame while it was being drawn; the engine no longer rewinds its constants then.
- A backdrop capture wrote its blur's descriptor while the copy was still a transfer destination, and a capture too
  small for a pyramid was sampled in that layout; the copy is now readable before anything reads it. The validation
  layer reported both.
- Path data reads through the engine's `SvgPathData`, one reader for geometry and SVG glyphs: an arc's flags written
  without separators (`a1 1 0 11 5 5`, as minifiers write them) were read as the number 11 and the path went wrong.
  Reading stops at the first error and keeps what came before, as SVG renders a path, and data that does not begin
  with a move draws nothing; the commands before a later move were skipped and the rest drawn. A step after `Z` with
  no move begins a new figure at the closed one's start; it was added to the closed figure. A smooth curve after `Z`
  no longer reflects the control point of the curve before it.
- A virtualized list - a `ListBox`, by default - in a slot that caps it, as a popup's `MaxHeight`, took the whole cap
  however few its rows; it now wants its rows, up to the cap, as a list that does not virtualize already did.
- A `TextBlock` or `TextBox` showed no more than 4096 glyphs - with color emoji drawn a quad per layer, some 65 emoji -
  and dropped the rest without a word. The cap is gone; text drawn directly, not in the batch, gets a glyph buffer
  as large as it needs.
- A `TextBox` took every line as tall as a line of its own font, so on a line with taller text (emoji from a fallback
  font) the selection, the caret and the click target sat off the glyphs. It takes each line's top and height from the
  layout now.
- An open popup - a slide panel, a flyout - vanished when its window moved to a monitor of another scale, and came back
  on the first one. Popups and adorners are baked at the window's scale, and a move keeps every position the same in
  DIPs, so nothing told their stages to bake again; their gate now asks about the scale too.
- `FontFamily="Name"` in markup built and then threw where the view was built, as `FontFamily` had no parser: the view
  came up empty with nothing said.
- What an open popup shows took the popup's data context as it was at the moment of opening, as a value of its own,
  and never followed it: a `SlidePanel` open through a theme swap reopened on its new template before the context
  reached it, and its content lost the view-model until it was closed and opened again - picks in it did nothing. The
  content now inherits the context from the popup. An open `SlidePanel` given a new template shows its drawer in place
  rather than sliding it in again.
- A property set in markup on an element of the project's own, declared through a `clr-namespace` without an
  assembly - a part of a control template, say - failed to build with "Type ... could not be found in any linked
  assembly": the property's type was looked up by the element's short name only, which never reaches the project's
  own types. It is now looked up by its namespace, as the element is.
- A drag made inside the application dropped into the window it began in even where another of the application's
  windows lay over that spot; it now drops into the one on top.
- A `TreeView` whose item container style binds `IsExpanded` reported a broken binding for every node without that
  member - a leaf of another kind. The tree now keeps the member itself: it writes it as rows open and close and follows
  it on the nodes that have it.
- A `GridSplitter` anywhere but in a `Grid` threw as it was shown. With nothing to resize it now does nothing.
- After a control started drawing text where it had drawn none - a data grid's search showing "1 of 1" - every frame
  threw and nothing more reached the screen until something forced a full redraw: the segment made for it carried no
  font sheet. A frame that fails to draw is now also logged as an error, and waiting for idle says so.
- A drag made inside the application - by automation - could be dropped on another application's window that lay over
  the target; it now looks only among the application's own windows.
- Selecting an item of a list or a tree that selects many, through automation, toggled it as a click does - selecting
  the selected one again took it out. It now makes it the one selected item.
- A floating docking window whose last pane was moved out by code - a `Zone` written, `DockBeside` - stayed open and
  empty: its document area survived being emptied, as the main window's does. A floating window with no pane left
  now closes.
- Waiting for the application to go idle did not wait for work invoked on the dispatcher - a window closing, a drop
  finishing - so automation could read the state before it changed.
- A `BezierLine` could not be made: its properties were registered with the value type and the owner swapped.
- A layer an `InfiniteCanvas` took off its stack - the camera moved away from what it held - kept those controls as
  its children, so each still had the dead layer for a parent. The layer now lets them go.
- A name given inside a template (`x:Name` in a `DataTemplate`) made a field of the view that nothing ever set - a
  warning, so a build with warnings as errors failed. Such a name belongs to each copy the template stamps; the view
  gets no field for it.
- A markup file that is not well-formed XML - a prefix nobody declared - stopped the AUML generator for the whole
  project: no class from any file, each reported missing, the cause only a warning. It is reported against that file
  now, and the rest is generated.
- An editor in a data grid's cell or a property grid's row had no name for a screen reader; it goes by the column's
  header and the property's name. The color picker's fields go by their labels, a list's item with no text by what the
  list calls the item, and a property grid is a pane of properties rather than a table that offers no table.
- Automation called an element on screen when what clips it - a scrolled tab strip, the window - cut it away entirely;
  `IsOffscreen` is true for it now, and false for one partly in view.
- A mistake in the application blueprint - a `StartupWindow` the build does not find - also cost the entry point, and the
  build added "no static Main method" to the real error; the entry point is written regardless now.
- `{ResourceReference}`, `{ObservableResource}` or `{ThemeResource}` written with no key - `Icon="{ResourceReference }"`
  mid-edit - crashed the AUML generator with an index out of range and the preview said nothing; both now report that
  the marker on that property names no key.
- A view with `x:ViewModel` built from a template for a view model it was given - by navigation into a docking area,
  a tab control, a list - made a second view model of its own from the container before taking the given one: the
  presenter now hands the view its view model before the view enters the tree. A view nested in a view of the same
  view model shares the parent's instead of making another.
- An application key named like a theme's - `SaveIcon` - was silently never found: a theme's dictionaries are searched
  before the application's global ones. The language server warns on such a key; the sandbox's two are renamed.
- The language server offered a binding against an `x:ViewModel` only the properties the view model's own class makes
  with the MVVM generator, not those of its base classes; and nothing at all after `<ResourceContext.`, whose
  properties are all attached.
- A property element with nothing in it - `<ApplicationBlueprint.StyleIncludes>` holding only a comment - crashed the
  AUML generator with an index out of range; it sets nothing now.
- The language server painted a type written as a value - `StartupWindow="MainWindow"`, `TargetType="Button"` - as plain
  text; it paints it as a type, and underlines one the build does not find. For a project not built yet, where it has no
  types to check against, it says so on the file instead of staying silent.
- A markup extension whose type the build does not find, nested in another - `Converter={conv:Missing}` - crashed the
  AUML generator; it is reported as a type not found.
- A value its property's type cannot take - `HorizontalAlignment="Middle"`, `Width="wide"`, `IsEnabled="yes"` - came out
  of the build as a C# error in generated code and passed the preview of a template unnoticed. The build and the preview
  name it in the markup, with what the property expects.
- The language server's completion threw inside the `<?xml ...?>` declaration, in a file cut short, and for a file
  not saved to disk; it offers what fits there, or nothing.
- A window snapped onto a monitor with a different scale - a quarter of a 100% screen, from a 150% one - drew its
  content at the old scale in the window's corner. The resize arrives before the DPI change and was divided by the old
  scale; the window's size is now read again once the scale has changed.
- A view that left a window was never let go, and with it every texture it drew: video memory grew about 230 MB with
  each pass over the sandbox's tabs until the device ran out. The window's render cache now hears the departure, does
  not freeze the layout of a destroyed element again, and keeps no departed element past the frame that dropped it.
- An element's own resources (`ResourceContext.Resources`, a non-global `ResourceContext.Source`) are released when the
  element is destroyed. They waited for an `Unloaded` that content leaving a presenter never raises, so the resource
  manager held every such view for the life of the application.
- `Unloaded` is raised for a loaded element that is destroyed - a view a presenter let go of never heard it, so a
  handler that unsubscribed there leaked. It comes once: a template part torn down and then released hears it a single
  time, and an element that never loaded hears nothing.
- An `Image` of a still picture draws the picture itself instead of a copy of its first frame, so every image showing one
  file shares one texture rather than uploading its own. `BitmapImage.GetMipLevel` keeps the frame it makes, so images
  of one mip level share it too.
- `ColorPickerButton` wrote `IsOpen` on a click and when its flyout closed, and `IsIndeterminate` when a color was
  chosen, as local values - an application's binding on either stopped driving it. They are current values now.
- A template took over the parts of a control built inside it that had applied its own template already - as code
  made from markup does. A tab control in a data grid's row details showed no tabs: its strip no longer knew the tab
  control the tabs were written in, and refused them. A template now stamps only what it built.
- `RibbonGroup.IsDropDownOpen` was written and read by nothing, so setting it did not open a collapsed group. It is the
  flyout's state now: set, it opens or closes the flyout; the group's button and the flyout closing itself report back
  to it.
- `IUIComponent.GetVisualDescendants` returned the children alone; it is every element below, at any depth, and returns
  `IEnumerable<IUIComponent>`. The ribbon looked for its quick-access bars among the window's children with it, so a bar
  in the caption got no key tips.
- A window that does not take activation when shown (`ActivateOnShow` false) took it when restored, and handed it to
  another window when minimized.
- The focus ring on a part of a control's template wrapped the whole control even when the control has several parts
  to stop on - a question's two answers - so it did not show which one Enter presses. The whole control is ringed only
  when the focused part is its one stop, as a numeric's editor is.
- The adorner stage measured every ring and key tip again on every frame, and each time asked for another: while one
  was on screen the loop never went to sleep. A ring is measured again when its control's size or its own content
  changes.
- A window holding a collapsed element whose template was queued for arrange - a ribbon's hidden contextual tab header
  - never finished a layout pass: the queue took the part back every iteration, waiting for a measure that nothing
  would run while it was collapsed, and every frame ran to the iteration limit. A part goes back on the queue only while
  its measure is queued too; showing the element lays it out.
- A minimized window stopped every other window of the application from drawing: the render thread they share waited
  for it to be restored, so the main window took clicks and showed nothing of them. A minimized window is skipped - it
  neither draws nor holds the others' changes back - and is recorded whole when it comes back.
- A window that closed stayed the application's `ActiveWindow` when the system sent no deactivation - as when the
  application is not in front - so the next overlay or overlay dialog opened in a window that was gone and nothing
  appeared. A closed window is no longer the active one.
- A dialog shown in a window of its own was titled "Dialog", an English word in the framework, instead of its
  `IDialogAware.Title` as the overlay shows it; its title now follows the dialog's, live.
- The rows of the caption's overflow menu were called by their command object's class name to automation and screen
  readers; the themes name each row by its command's label and give it the command's `AutomationId`. An item shown
  without an element of its own is no longer called by its class name.
- An active `OverlayWindow` in the Fluent theme drew a gray outline around its accent caption, a box inside the window.
  The outline takes the caption's color when active, as the main window's does.
- A context menu's rows took the text color of what the menu belongs to, not of the menu's own card: the caption's
  overflow menu showed the caption's white words on the light theme's white card. The card states its own color in all
  three themes. A row's color is a style by type, which inheritance outranks by design.
- A border around a material fill was never drawn - so the acrylic card of every Fluent menu, drop-down and flyout had
  no edge and melted into what was behind it. The material batch takes the framed rectangle whole and baked only its
  pen; a uniform border now rides in the same record as a ring inside the outline. A border with different widths per
  side is still not drawn on a material.
- A drag ghost built from a `DragTemplate`, and the count badge of a multi-item drag, showed nothing under the cursor.
  `VisualRenderer.RequestSnapshot` of an element in no tree recorded a picture with nothing in it - the render cache
  leaves out whatever is not in a tree; such an element is now hosted off-screen at its arranged size, as
  `RequestRender` does.
- A style written inside a template styled nothing. The generator set a part's properties by name at template priority,
  and a property with no `AdamantiumProperty` behind it - a style's `Selector` - was dropped without a word. Such a
  property is now assigned, as the designer's loader already did.
- `{ResourceReference}` on a component's plain CLR property, or on an attached property, was dropped without a word: the
  first is now resolved and assigned at once, the second is named with its owner (`Grid.Row`).
- A style setter whose value is built per element (`x:Shared="False"`) or is a `{ResourceReference}` threw on an element
  that does not have the property - a style matched by class reaches elements of every type. It passes over such an
  element, as a plain value always did.
- A submenu whose rows were written in markup closed as the pointer moved into it: only rows made from data held their
  submenu open.
- A popup put away by a press outside it or by Escape cut the application's binding on `IsOpen`: the view model could not
  open it again.
- Releasing the mouse on a button inside a menu row chose the row as well.
- A press inside a popup did not put away another popup that a press elsewhere closes: two flyouts opened from one menu
  stayed open together. A press counts as inside only in the popup's own card or a popup opened from it.
- `SetCurrentValue` on a property a `{TemplateBinding}` feeds wrote above it and masked it for good: a popup whose
  `IsOpen` follows its control, put away by a press outside, could not be opened by the control again. It writes into
  the template binding's own slot.
- What was added to an open popup - the rows a list or a menu makes in its first layout, a template applied late -
  looked for the window among its visual ancestors, found none (a popup's card has no visual parent) and stayed outside
  the tree: a template taken from the view by key never reached it, and the list showed its items' type names, on some
  opens and not others. An element joins the tree its parent is in.
- Popups were measured in the popup stage, on the render thread. They are laid out in the window's layout pass, on the
  loop thread, like everything else; the stage only draws them.
- A radio group named in a control's template was shared by every copy of the control: two canvas inspectors switched
  each other's faces. A name written in a template now groups the radios of that one control.
- `DataContext="{Binding X}"` read `X` from the element's own new DataContext on the next refresh and broke: a panel
  bound to the picked item stayed on the first one. It reads the parent's context and follows it.
- An `ItemsControl` given an element that stands elsewhere - written into another list, or already in the tree - took it
  and left its place empty: a page listing a ribbon's buttons emptied the ribbon's groups for good. Such an element is
  left where it stands, its slot stays empty and the mistake is logged. A list in a control's template still shows the
  items written into that control.
- A ribbon tab's header in the strip kept the label the tab had when the strip was built: after a language switch the
  tabs went on in the old language.
- An items list whose `ItemsPanel` changed to a virtualizing panel (a `StackPanel` or a `WrapPanel`) showed nothing: the
  new panel found every item realized already, in the panel it replaced, and the replaced panel took back whatever was
  shown elsewhere. The replaced panel lets go of its items and stops hosting.
- Completion of `x:` directives in the language server: after `{` and `{x:` it offers `x:Null`, `x:Static` and `x:Type`;
  inside `{x:Static}` a type and then its public static fields and properties, base types included; the values of
  `x:Load`, `x:Shared`, `x:CreateInDesignTime` and `x:KeepAlive`. All of these offered nothing before.

- `DockingArea` and `PaneHost` threw "Invalid size returned for Measure" when offered unbounded space - an `Auto` row
  of a `Grid`, a `StackPanel`, a `ScrollViewer`. They now measure to what their panes need.
- An inspector line whose name, description or tip changed while shown - a language switch - kept the old words.
- A `DropDown` whose picked item's content changed went on showing the old content in its header.
- A number written out in markup, `count=5`, took the `Other` form of a counted phrase whatever it was.
- A `{Localize}` argument that was itself a `{Localize}` was built as its text; the designer read it right.
- A file dialog given a file type without a name showed an empty line for it; it shows the type's patterns.
- A `ListBox` wrote its selection above the application's binding: after the first pick the view model could no longer
  change what the list had selected. It writes current values, as the other selectors do.
- A `ListBox` kept its selection by position when its items changed: an item removed or inserted above the selected one
  moved the highlight to a neighbor, and a selected item that left the list stayed `SelectedItem`. The selection follows
  its items; one that leaves is no longer selected, and a bound view model hears so.
- A `ListBox` whose `SelectedItem` was bound before its items arrived came up with nothing selected.
- A binding a style's setter states - `{Binding}`, `{Localize}`, `{Ancestor}`, `{Self}` - took the element's own binding
  slot: it replaced a binding the element stated itself, outranked a value its template set, and stayed when the style
  went. It is now that style's value: under what the element and its template say, following the DataContext, gone with
  the style. A control's current value on such a property goes into the style's slot, so a two-way setter keeps
  following its source. A trigger's `{Ancestor}` and `{Self}` go when the trigger lets go - a data grid row kept its
  error wash after the error was fixed - and a trigger's `{Binding}` is a live binding rather than the binding object
  written as the value.
- `RemoveBinding` did nothing, and threw when given a property; it removes the element's binding.
- `RibbonApplicationMenu` wrote `IsOpen` at Local priority when it opened or closed itself, above a two-way binding:
  after that the view model could no longer open or put away the backstage. It writes current values.
- Popups and adorners were recorded on the render thread, from the live tree, while the loop went on changing it: a
  list rebuilt in an open popup flashed for a frame with its items piled up, not yet laid out. They are recorded on the
  loop thread right after layout, as the window's content is, and the render thread only applies what was recorded
  (`RenderCache.RecordComponents`, `ApplyComponents`). Adorners are laid out on the loop thread too.
- `ShowDialogAsync` of a dialog in its own window never completed when the user closed the window while the dialog
  refused to close; the closed window now completes it as `Cancel`.
- The language server looked a type written without a prefix up only in the registered xmlns, where the build falls back
  to its short name: after `{x:Static RegionNames.}` it offered nothing, and `<Thickness>`, `<CornerRadius>` or a
  project type as an element were reported unknown. It finds them the build's way now, offers the project's own types
  bare, and knows the classes the project generates from its markup (style sets, resource dictionaries).
- `clr-namespace:System;assembly=System.Runtime` was "CLR namespace not found" in the editor and painted every
  `<sys:Double>` red.
- `Pane.Zone` was neither read nor written by the area. It now says where the pane is after a drag, a fold or a restore,
  and writing it - from code or a two-way binding - moves the pane there the way opening it there does. A zone the pane
  is not `Allowed`, or a move the application refuses in `PaneDocking` or `PaneTearingOff`, snaps it back. A closed
  tool comes back to the zone written while it was away.
- A `PaneGroup` declared before the documents' group became the documents.
- A saved docking layout lost the document area whenever no document was open in it - documents are never saved, so
  that was every save - and so did a reset after the last document was closed: the tools closed up over the hole and
  one of them came back as the documents. The area is kept, empty if need be.
- A panel in a side column split top and bottom counted as standing at the top or bottom: a pane opened at the bottom
  joined the column's lower half instead of a band along the bottom.
- Navigating a docking region to the view model it already had as current did not bring its pane to the front.
- Content one control let go of and another took up before it was released came back dead, its bindings closed: after a
  docking layout was reset, a document's body lost the words of its labels and its commands.
- A window's `Width` and `Height` written in markup were taken as physical pixels: on a 150% monitor a
  `Width="1280"` window opened two thirds as large, and was remembered so. They are logical now, like every size in
  markup.
- An element moved under another parent did not tell the elements below it that what they inherit through it had
  changed, unless something watched the value at the element itself: a binding below kept the value of the moment the
  element was out of the tree. A watched element moved under a new parent no longer takes that parent's mere default
  over its own theme value - only what an ancestor states is inherited, as when it is read.
- A tab that is its own container, taken out of a virtualizing tab strip and put into another, was parked by the strip
  it left - hidden, its bindings closed - and shown so by the next: after a docking layout was reset, the labels of the
  tool tabs changed size by a pixel, and back on the next reset.
- `{ObservableResource}` and `{ThemeResource}` on an element moved from one tree to another stopped following their
  resource for good: the tree it left unloaded it, and nothing connected them again. Unloaded, they still let go of the
  resource manager and the theme; they connect again once the element is in a tree again.
- A pane opened under the id of a closed tool left that tool kept as well: bringing it back put the id in the layout
  twice. Likewise a closed tool that a loaded layout has open.
- A style setter for an attached property of a static service - `KeyTipService.KeyTip`, `ToolTipService.Placement`,
  `AutomationProperties.AutomationId` - was passed over without a word: only component owners such as `Grid` were
  looked for.
- `ItemContainerStyle` of a `TabControl`, a `DropDown`, a `Ribbon` and a `RibbonApplicationMenu` did nothing: the
  containers were themed after it and lost it. It is applied after the theme, as `ListBox` always did.
- A `TreeView` wrote its rows' `IsExpanded` and `IsSelected` at Local priority, above the item container style's
  bindings: once a row had been shown, clicked or toggled, the view model could no longer open or select it. The tree
  writes current values.

## [0.1.0-alpha] - 2026-10-02

The first public alpha.

[0.1.0-alpha]: https://github.com/AdamantiumStudio/AdamantiumUI/releases/tag/v0.1.0-alpha
