### Fixed

- Another tab picked in the docking panel being worked in - its header clicked, or selected through automation - is
  the active pane (`ActivePaneId`, `ActivePaneChanged`); the active pane stayed the tab shown before, so what follows
  the active pane - an application's Undo and Save - went on with a tab no longer in sight. Tabs put in a new order
  keep the one worked in, in front.
- A tab closed before the one in front of its panel no longer brings the tab after that one to the front.
