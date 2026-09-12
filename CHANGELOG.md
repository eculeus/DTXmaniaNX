# Changelog (eculeus fork)

This fork of [limyz/DTXmaniaNX](https://github.com/limyz/DTXmaniaNX) adds a sheet-music style
drums view, practice conveniences and multi-player high scores. Everything below is additive:
with the new options left at their defaults the game plays exactly like upstream, and Config.ini
files from upstream keep working (new keys default off).

Builds are produced by GitHub Actions (`.github/workflows/build.yml`, Release x86). Install by
extracting the zip over an existing DTXManiaNX folder; `Config.ini` is not included, so key
bindings and settings are kept.

## 1.5.0-beta.9 — 2026-09-12

### Drums notation view (new)
`Config > Drums > NotationView`: **Off / Scroll / Page** (`DrumsNotationView=0|1|2`).
Replaces the vertical lanes with a five-line staff drawn like drum sheet music; judgement, timing
and scoring are unchanged (notes are placed from the same timing the lanes use).

- **Scroll**: one large staff across the top two thirds of the screen, notes scroll right to
  left into a fixed red playhead just after the lane labels. About two bars of 4/4 are visible
  ahead at SPEED 2.0; the in-game SPEED setting scales it.
- **Page**: two static staves of `DrumsNotationBarsPerLine` bars each (Config.ini only, default
  4). The playhead plays the top staff, then the bottom, then the top again; the staff that is
  not being played always shows the next line and is replaced with a short crossfade the moment
  the playhead leaves it, so nothing ever moves vertically. Played notes are dimmed.
- Standard drum notation positions: hi-hat top space (x head, circled x when open), ride top
  line (diamond), crash first ledger line (bold x), snare, toms and kick on their usual lines,
  hi-hat foot below the staff. Stems up for hands, down for feet, chords share a stem, eighths and
  sixteenths are beamed per beat. `Config > Drums > NotationStems` (`DrumsNotationStems`, default
  on) turns stems and beams off.
- One colour per instrument, also used for the lane labels and hit flashes: hi-hat blue, snare
  yellow, hi tom green, lo tom red, floor tom orange, crash magenta, kick white, ride pale cyan.
- Lane labels in a gutter at the left; bar numbers along the top; a thick song progress bar under
  the staff; the score panel, gauge, combo and background-video window are laid out in a strip
  below the staff so nothing overlaps the music. The skill graph is not drawn in notation mode.
- The lane's line lights up when a pad is hit; judgement popups appear beside the note they judge.
- Both crash cymbals share one CRASH row, and a hit on either crash pad counts for a crash note
  on either lane while notation view is on.

### Skip key
`Config > Key Assign > System > Skip in play` (`Skip=` in `[SystemKeyAssign]`, unbound by
default). Jumps ahead by `SkipTimeMs` during play without entering training mode, so the score is
still saved; every note skipped over is counted as a MISS, so skipping through silence is free and
skipping into the music costs exactly what was skipped. A "SKIP" label shows briefly.

### Named high scores
After a cleared drums run the result screen asks for a name (type A-Z / 0-9, Backspace, Enter
saves, Esc skips; the last name used is remembered as `LastPlayerName`). The top 10 per chart and
difficulty are kept in `<chart>.scores.ini` next to the existing `score.ini` (which is unchanged)
and shown as a table on the result screen; song select shows the top 5 for the selected song and
difficulty. Drums only.

### Fixes
- The result screen's grade now matches the grade shown in song select in XG skill mode.
- No crash when DirectShow refuses the movie play rate at low PlaySpeed (the movie plays at
  normal speed instead).
- Skipping past the end of the background movie crashed with `E_INVALIDARG`: the seek time
  overflowed a 32-bit multiply after 3 min 34 s. Fixed, clamped to the movie length, and seek
  failures are now logged instead of thrown.

### Build and test infrastructure
- `.github/workflows/build.yml` builds on every push, zips the runtime (with the 32-bit BASS
  DLLs in `dll\` for the x86 build) and publishes a GitHub release on `v*` tags.
- A `smoke` job launches the freshly built game on the Windows runner in compact mode with a test
  chart (`Tests/SmokeSong`) on auto-play in both notation modes and uploads screenshots of the
  actual game screen (`smoke-screenshots` artifact). Two opt-in hooks make that possible and do
  nothing unless their environment variable is set: `DTXMANIA_AUTOCAPTURE_MS` (periodic back-buffer
  capture to `Capture_img\`) and `FDK_ALLOW_SILENT_SOUND_DEVICE` (a silent stand-in sound device
  for machines with no audio endpoint).
