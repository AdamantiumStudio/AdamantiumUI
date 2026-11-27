### Added

- A new package, `Adamantium.UI.Audio`: `AudioPlayer` plays sound files - WAV, FLAC, MP3, Ogg Vorbis and Opus, by
  the Pulsar engine's `AudioPlayback` - one after another from its `Tracks` (`AudioTrack`, whose title, format, length
  and number in the list are read in the background once listed): play and pause, stop, the previous and the next,
  `Repeat` (off, the list, the track) and `IsShuffled`, `Position` and `Volume`; what plays (`Info`, `Duration`,
  `Remaining`), how many tracks and how long all are (`TrackCount`, `TotalDuration`), with its spectrum, and its
  waveform where `ShowsWaveform`. Its commands - `PlayPauseCommand`, `StopCommand`, `PreviousCommand`, `NextCommand`,
  `RepeatCommand` - are properties of it. A track that can not be played is said (`HasFailed`, `FailedTrack`) and
  passed by. The theme draws it in four looks by `View` - `Full` (a display, the position, the buttons and the
  volume, the list), `Bar` (one row), `Line` (one row with the title), `Mini` (play, the time, a short position) -
  each part of them a template property an application may set alone: `DisplayTemplate`, `PositionTemplate`,
  `TransportTemplate`, `TrackTemplate`. It plays through its own `AudioPlayback` to the default device while shown,
  unless `Playback` gives one; the accent is the theme's, the digits in its code font.
- Its parts are controls of their own: `Spectrum` (bars that fall, with their peaks); `Waveform`, the position as
  players with a waveform have it - bars of whole pixels apart from each other (`BarWidth`, `BarSpacing`), the played
  ones in the accent, a line where it is, pressed, dragged or stepped by the keys, a slider to automation; and
  `AudioFormatBadges` (codec, kilohertz, kilobits, channels - those of `Marks`, in a row or a column - in the
  language's numbers).
- `TimeSpanToSecondsConverter`, a time to a slider's number and back, and `TimeSpanToClockTextConverter`, a time as a
  player shows it ("01:04", "1:02:05", or what is left, "−01:27").
- The sandbox: an Audio player page, with five sounds of its own made of tones and noise, one of each format.
