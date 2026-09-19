# Changelog (eculeus fork)

This fork of [limyz/DTXmaniaNX](https://github.com/limyz/DTXmaniaNX) adds a sheet-music style
drums view, practice conveniences and multi-player high scores. Everything below is additive:
with the new options left at their defaults the game plays exactly like upstream, and Config.ini
files from upstream keep working (new keys default off).

Builds are produced by GitHub Actions (`.github/workflows/build.yml`, Release x86). Install by
extracting the zip over an existing DTXManiaNX folder; `Config.ini` is not included, so key
bindings and settings are kept.

## 1.5.0-beta.18 — 2026-09-19

- The practice panel opens with **`/`**. Shift+F2 was only ever chosen because upstream reserved
  that combination in 2011, which made it certain to be free rather than good to reach for
  something you use between every run; nothing else in song select uses `/`.
- `/` is accepted only while that key is not bound to a drum pad — the care the Decide key
  already takes with Enter, generalised to any key code — so a pad tap in song select cannot
  open the panel. **Shift+F2 still works unconditionally**, and is the way in if you have bound
  `/` to something.
- The smoke test drives `/` rather than the Shift chord, so the key a player actually presses is
  the one under test. (That chord had also failed silently once in CI, costing a re-run.)

## 1.5.0-beta.17 — 2026-09-18

- **Play speed works again with TimeStretch ON** (WASAPI/ASIO). Changing the speed used to swap
  the mixer channel from the raw stream to the pitch-preserving tempo stream without ever taking
  the raw one out, so both were mixed at once, both pulled on the same decoder, and the song ran
  roughly twice as fast instead of slower — and putting the speed back to x1.000 never recovered
  it. A sound that has a tempo stream now plays through that tempo stream for its whole life,
  x1.000 included (tempo 0%), so the mixer channel never changes identity. Should a handle ever
  change again, it is now removed from the mixer and re-added at the same position, volume and
  pan, and the end-of-stream callback follows it.
- With TimeStretch ON, a sound loaded before the setting was switched on has no tempo stream; its
  play speed used to be set on an attribute that stream cannot honour, and was silently ignored.
  It now falls back to changing the frequency, as with TimeStretch OFF.
- With TimeStretch ON, the random detune on a bad hit no longer re-applies the play speed as a
  pitch shift on top of the tempo change.
- The play speed and detune are re-applied after a sound device change, instead of being lost
  with the rebuilt streams.
- The routing rules behind all of that are checked in CI by a small offline harness
  (`Tests/TimeStretchRouting`) that needs no sound device.

- Notation view: a note takes the colour of its judgement as it is played and keeps it while it
  is on screen, so the part of the staff already gone by reads as a report of how it went. The
  head eases from its lane colour over to Perfect ice blue, Great sea green, Good gold, Poor
  violet or Miss red over 180 ms (cubic ease out, on the clock, so the fade is the same at any
  frame rate), and a missed head is boxed as well as reddened - the one cue that is not a colour.
  A judged head is also drawn stronger than a plain played one (alpha 170, against 70 scrolling
  and 102 on the page) since its colour is the thing being read. Works in both scroll and page
  mode; the playhead, bar lines, beat ticks, legend and the rest of the HUD are untouched.
- Notation view: the flash at the playhead takes the judgement's colour too, and a miss now
  flashes where it used to flash nothing. In scroll mode the note itself is behind the legend
  about a tenth of a second after the playhead, so the flash is what is actually read there.
- Auto-played lanes are not judged and keep their lane colour, in both the head and the flash.
- New setting `DrumsNotationJudgeColour` (CONFIG -> Drums -> NotationJudge), default ON.
- Scroll mode: the playhead has moved from x=150 to x=400, so a note you have played stays on
  screen to be looked at. At the SPEED 2.0 setting the staff moves 0.286 px per ms, so the old
  40 px between the playhead and the legend gutter were 140 ms - and a miss is not decided until
  117 ms after its note, which is why a miss used to be readable only as the flash. There are now
  290 px of played staff, about half a 4/4 bar at 120 BPM or a full second of judged notes, and
  1.5 bars (880 px, 3.1 s) of music still coming.
- New setting `DrumsNotationPlayheadX` (Config.ini only, 120-900, like `DrumsNotationBarsPerLine`)
  moves it: bigger for more history, smaller for more lookahead. Out of range or not a number
  keeps the default. The chip lookahead, the judgement popup, the lane flash and the bar lines
  all follow the playhead. Page mode is unaffected - it does not use this position at all.
- A played chip used to be dropped a flat 65 px past the judgement line, which in the notation
  view is 52 px: with the old playhead that was already inside the legend gutter, but at any
  playhead further right the note would have blinked out in mid-staff. In the notation view a
  chip is now kept until it reaches the gutter, which is also why the stage now clears when the
  last note has left the staff rather than a fifth of a second after it passed the playhead.

- **Practice loop mode.** `Config > Drums > PracticeMode` (`PracticeMode=` in `[PlayOption]`,
  off by default). With it on, **Shift+F2** in song select opens a PRACTICE panel: pick a named
  section of the song, or type a bar range and save it under a name, and the performance screen
  plays only that range and loops it in place. `=` rewinds to the start of the range instead of
  going back to the song-begin screen; seek and speed keys keep working and seeking is clamped
  inside the range; the loop key clears the range if you want out. Combo, judgement counters,
  score and gauge are folded back at each lap, so the numbers describe the lap you are on.
  Nothing is scored or saved (no `score.ini`, no named high score, no rank), on the same
  training-mode flag the existing seek and speed keys use.
- A song folder can carry its structure in a `sections.def` next to `set.def` — named sections
  in `.dtx` bar numbers, each loop starting one bar early for a run-up. Ranges you save
  yourself live in `PracticeRanges.ini` next to the game, so re-downloading a song folder does
  not wipe them. Format, bar-numbering convention and a worked example (a 6/4 song with 3/4
  bars and six tempo changes inside one bar) are in `docs/practice-mode.md`.
- The smoke test drives the new panel from song select and asserts the section's bar range
  resolves to the right milliseconds and that the loop actually wraps.

## 1.5.0-beta.16 — 2026-09-14

- Page mode: the playhead no longer runs past a line's closing bar line. Since the bar lines
  moved back before beat 1, the playhead now moves to the next line the moment it reaches the
  closing bar line, appears at that line's first bar line, and travels the gap to beat 1 on time.
  No note can fall in that gap.
- High score lists (song select and result screen) show the letter grade (SS, S, A ... E) of each
  run between the score and the achievement rate.

## 1.5.0-beta.15 — 2026-09-14

- Page mode: bar lines are drawn 34 px before their beat 1 (about a staff space plus a note head,
  as engraved), and notes, beat ticks and the playhead are placed exactly where time puts them
  again. This replaces beta.14's note map, which moved the notes instead: the playhead had to
  jump the gap at every bar line, and that made the first note of a bar feel late (judgement was
  never affected, only where the playhead was). When a bar ends with a note closer than that to
  the next downbeat (a 16th pickup), that bar line is put half way between the note and beat 1,
  so a note is never on the wrong side of a bar line.
- Page mode: the staff of a line begins at its first bar line instead of at the label gutter. The
  stub of staff before the first bar line looked like a sliver of the previous bar.

## 1.5.0-beta.14 — 2026-09-14

- Page mode: notes are placed as engraved, not as timed. Beat 1 no longer sits on the bar line:
  each bar's notes are mapped into the room between its two bar lines less a gap after the
  opening line (14 px) and a smaller one before the closing line (6 px), so a note is never on or
  outside its bar's lines. Bar lines, beat ticks and the staff length are unchanged. The playhead
  follows the notes, so it skips the gap at every bar line instead of drifting away from them.

## 1.5.0-beta.13 — 2026-09-13

- Page mode: every bar is drawn on exactly one staff. The pre-roll is gone: a line's notes ran on
  into the start of the next line so the playhead could leave early, which meant a note you had
  just played was also sitting, already passed, at the start of the line you were entering. The
  playhead now stays on a line right up to its closing bar line and jumps to the first bar line
  of the next one. `DrumsNotationPrerollMs` is no longer read.
- Page mode: the playhead strictly alternates upper, lower, upper, lower. The staff it leaves
  keeps its line for the first half of the first bar of the line now playing, fades it out over
  half a bar, then fades the line after next in over half a bar, and is done at least 300 ms
  before that line is due. A short line squeezes the schedule; one shorter than that swaps at
  once. The old 300 ms crossfade on the moment of the jump is gone.
- Page mode: lines still hold whole bars only, packed by where each bar ends, and the staff of a
  line ends at its last bar.

## 1.5.0-beta.12 — 2026-09-13

- Page mode: the staff lines of a line now stop at its closing bar line. A line that holds fewer
  bars ends early and the rest of the row is empty, so what is staff is a bar.
- Page mode: beat ticks now really follow each bar's length. beta.11 claimed this but computed
  them from chip positions, which are always 384 per bar in DTXMania whatever the bar length, so
  every bar was still quartered. The ticks now come from the loader's own beat-line chips: none in
  a one-beat pickup, two in a 3/4 bar.
- Page mode: bar numbers now match the DTX file. DTXMania puts an empty bar in front of every
  chart, so the bar the file calls 1 was shown as 2 and the lead-in bar 0 as 1. The empty bar and
  the lead-in are unnumbered and the first real bar is 1, as in the sheet music.
- Page mode: every staff change is written to `DTXManiaLog.txt` with the song time and the line
  numbers, and flagged if the staff the playhead is entering changes at that moment (which should
  not happen). The page layout (bar times, lines) is logged too.
- CI smoke chart: bar lengths are reset after the pickup and the 3/4 bar (a DTX bar length stays
  in force until it is set again), so the smoke frames show the intended structure.

## 1.5.0-beta.11 — 2026-09-12

- Page mode: a bar is placed on a line only if the whole bar fits, so lines end early instead of
  clipping a bar at the edge and no bar is ever skipped between lines. Beat ticks follow each
  bar's real length (a pickup bar gets none, a 3/4 bar two). The lead-in bar is not numbered.
  The hit flash is a short trail behind the playhead.
- All nine lane labels are always drawn, whether or not the chart uses the lane.
- Song-select leaderboard: it was hidden under the header banner and only refreshed on song
  change. It now sits just below the banner, reloads whenever the song or the difficulty changes,
  and its header names the difficulty and level the list belongs to (scores are kept per song and
  per difficulty).

## 1.5.0-beta.10 — 2026-09-12

- Page mode lays bars out by time, so the playhead moves at one constant speed for the whole song
  and bars of different length or tempo get different widths (lines hold as many whole bars as
  fit). Each line starts with a short pre-roll (`DrumsNotationPrerollMs`, default 600) so the
  playhead is already on the new line before its first note.

## 1.5.0-beta.9 — 2026-09-12

### Drums notation view (new)
`Config > Drums > NotationView`: **Off / Scroll / Page** (`DrumsNotationView=0|1|2`).
Replaces the vertical lanes with a five-line staff drawn like drum sheet music; judgement, timing
and scoring are unchanged (notes are placed from the same timing the lanes use).

- **Scroll**: one large staff across the top two thirds of the screen, notes scroll right to
  left into a fixed red playhead just after the lane labels. About two bars of 4/4 are visible
  ahead at SPEED 2.0; the in-game SPEED setting scales it.
- **Page**: two static staves of `DrumsNotationBarsPerLine` bars each (Config.ini only, default
  4, a target density). The playhead plays the top staff, then the bottom, then the top again; the staff that is
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
