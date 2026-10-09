# Changelog

Notable changes to the Adamantium UI packages, in the form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All packages share one version.

## Unreleased

### Added

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
  something), in the Fluent icons the Fluent, macOS and Editor Pro themes share.
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
  "MainWindow" StartupTheme="EditorPro" StartupThemeVariant="Light">` with its `Resources` and `StyleIncludes`. The
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

### Changed

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

- `SVGParser.Commands` and `SVGCommand`: path data goes straight into the geometry through the engine's
  `SvgPathData.Walk`, the same walk SVG glyphs take.

### Fixed

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
