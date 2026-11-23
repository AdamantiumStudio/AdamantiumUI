### Changed

- Down the side, a contextual bar's name - turned, as long as it reads - is cut to a strip of its context's color, the
  name its tooltip, when its tray gives the bar less than its whole length: the commands take the room the name took,
  instead of going to the menu for it. Given its whole length again, the bar shows the whole name.
  `ToolBar.IsContextualHeaderShort` says which; the themes draw the strip (`ToolBarContextualStripLength`).
