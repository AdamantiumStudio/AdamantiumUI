### Added

- A new package, `Adamantium.UI.Audio`: `AudioPlayer` plays sound files - WAV, FLAC, MP3, Ogg Vorbis and Opus, by
  the Pulsar engine's `AudioPlayback` - one after another from its `Tracks` (`AudioTrack`, whose title, format and
  length are read in the background once listed): play and pause, stop, the previous and the next, `Repeat` (off, the
  list, the track) and `IsShuffled`, `Position` and `Volume`; what plays (`Info`, `Duration`, `Remaining`) with its
  spectrum, and its waveform where `ShowsWaveform`. A track that can not be played is said (`HasFailed`,
  `FailedTrack`) and passed by. The theme draws it in four looks by `View` - `Full` (a display, the position, the
  buttons and the volume, the list), `Bar` (one row), `Line` (one row with the title), `Mini` (play and the time) -
  each part of them a template property an application may set alone: `DisplayTemplate`, `PositionTemplate`,
  `TransportTemplate`, `TrackTemplate`. Its parts are controls of their own: `Spectrum` (bars that fall, with their
  peaks), `Waveform` (drawn as it is read, the played part in the accent) and `AudioFormatBadges` (codec, kilohertz,
  kilobits, channels, in the language's numbers). It plays through its own `AudioPlayback` to the default device while
  shown, unless `Playback` gives one; the accent is the theme's.
- `TimeSpanToSecondsConverter`, a time to a slider's number and back, and `TimeSpanToClockTextConverter`, a time as a
  player shows it ("01:04", "1:02:05", or what is left, "−01:27").
- The sandbox: an Audio player page, with five sounds of its own made of tones and noise, one of each format.
