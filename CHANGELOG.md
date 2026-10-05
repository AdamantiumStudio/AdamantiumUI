# Changelog

Notable changes to the Adamantium UI packages, in the form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All packages share one version.

## Unreleased

### Added

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
  buttons on either side are (`TitleBar.TitleCentering`), in Fluent and Editor Pro as macOS already did.
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
- `Pane.IsDirty`: the tab of a pane with unsaved work wears a mark in Fluent, Editor Pro and macOS. Closing such panes -
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
  binding or recorded change left to do, no animation that ends on its own still running, no glyphs on their way - and
  the frame showing that drawn. `IdleBlocker` says what it is still waiting on. An animation that never ends counts as
  idle: one that loops forever, and a ticker registered with `AnimationManager.AddEndlessTicker` (a caret's blink, an
  animated picture). Automation waits for it after every action instead of for two frames, and `adam-auto` says what
  kept the application busy when it does not settle.

### Changed

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

### Fixed

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
