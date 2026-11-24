### Fixed

- `TabControl`: a tab a virtualizing strip has not made yet is called by the name the item container style gives the tab
  (`AutomationProperties.Name`), else by the item as text, as before - on AT-SPI every gallery tab past the first twelve
  had been a blank.
- A binding of the item container style read for an item with no element - an id or a name - now reads a binding nested
  in it, such as a phrase's key (`{Localize Table, Key={Binding Header}}`), off the item.
- Sandbox: the gallery's tabs carry their titles as automation names.
