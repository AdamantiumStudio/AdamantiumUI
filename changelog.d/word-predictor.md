### Added

- `IWordPredictor` (`Adamantium.UI.Core.Text`), from the application's container: the system's word prediction - the
  words a beginning goes on to (`CompleteAsync`), what a misspelled word was meant as (`CorrectAsync`), the word likely
  to come next (`PredictNextAsync`), each with the words typed before and a cancellation. Windows: its own typing
  suggestions (`Windows.Data.Text.TextPredictionGenerator`, Windows 10 1803 and later), called through
  `Adamantium.Win32.WinRT`; a language is supported when the user has added it to Windows, matched by its first subtag,
  and the user adding or removing a language is seen on the next ask. Not on Linux and macOS yet.

### Changed

- Windows spell checking sees the user adding or removing a language on its next call, where it kept the languages it
  found until the application restarted.
