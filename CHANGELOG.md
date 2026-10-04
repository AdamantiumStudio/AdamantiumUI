# Changelog

Notable changes to the Adamantium UI packages, in the form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All packages share one version.

## Unreleased

### Added

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
- `RibbonQuickAccessEventArgs.DropDownItemContainerStyle`: what a drop-down command's rows do, handed over with how they
  are drawn. The themes' quick-access menus use both.
- `PropertyTrace`: values dropped for want of a property - set by a name the element has no property for, or a live
  resource connected to one. Each distinct report goes once to the application log and, under a debugger, to the IDE's
  output; `PropertyTrace.Sink` receives them all.

### Changed

- `AdamantiumComponent.SetValue(string, ...)` reports a name the type has no property for through `PropertyTrace`
  instead of ignoring it without a word. `ThemeResource.Apply` and `ObservableResource.Apply` report such a name too,
  connect nothing and return null.
- The markup compiler refuses `{ThemeResource}` and `{ObservableResource}` on a plain CLR property: nothing there can
  follow a resource. `{ResourceReference}` sets it once.

- `ContextMenu` and `MenuItem` make every row built from data a `MenuItem`, whatever the `ItemTemplate`: the template
  draws the row's header, `ItemContainerStyle` says what the row does. A plain `DataTemplate` used to leave the rows bare
  content - no hover, no check, no command - and only a `HierarchicalDataTemplate` made menu rows. A template that built
  a `MenuItem` itself now puts one inside a row: move what it set (`Command`, `Icon`) into `ItemContainerStyle`, as the
  themes' title-bar and quick-access overflow menus now do. `TreeView` and `TreeViewItem` likewise make a `TreeViewItem`
  for every node and `RibbonTab` a `RibbonGroup` for every group; a tree with a plain template or none used to draw its
  internal row object in place of the node.

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

### Fixed

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

## [0.1.0-alpha] - 2026-10-02

The first public alpha.

[0.1.0-alpha]: https://github.com/AdamantiumStudio/AdamantiumUI/releases/tag/v0.1.0-alpha
