# Drum hit log

A CSV of everything that happened on the kit during one drums play: every pad hit with its
timing and judgement, every note that went by unhit, every hit the game ignored and why, and
MIDI notes the kit sent that no pad is bound to. It answers three questions the result screen
cannot:

- **Is my timing consistently offset?** — mean/median `lag_ms` per pad and overall.
- **Does the chart drift?** — mean lag per 16-bar window. A steady slide from start to end is
  the chart's tempo map (or the audio offset) disagreeing with the record, not you.
- **Why didn't that hit register?** — the `outcome` and `detail` of the row at that moment.

Off by default. With `DrumHitLog=0` no log object exists and every hook in the performance
screen is a single null check, so play is exactly as before.

---

## 1. Turning it on

- `Config > Drums > HitLog` → **ON**, or
- `DrumHitLog=1` in the `[Log]` section of `Config.ini`.

Then play any song on drums. Nothing changes on screen.

## 2. Where the file goes

```
<game folder>\HitLogs\<yyyyMMdd-HHmmss>_<title>_<difficulty>.csv
```

- The folder is created next to `DTXManiaNX.exe` the first time.
- The time is when the performance screen opened. The title is the chart's `#TITLE` and the
  difficulty is the set.def label (`MASTER`, ...); in compact mode (a .dtx given on the
  command line) it is the chart file name instead. Characters Windows does not allow in file
  names become `-`.
- **One file per play.** It is written whenever the performance screen is left: stage clear,
  stage failed, Escape back to song select, `=` restart (the restarted play gets its own file).
  A practice-loop session is one play: every lap is in the same file, as a new `segment`.
- The file is written once, at the end, on a separate thread. During play the log only adds
  small records to a list in memory — no disk access, no formatting.
- UTF-8 with a BOM, CRLF, so Excel opens it with the columns split and non-ASCII titles intact.
- If the game is killed (Task Manager, a crash) the play's file is not written.

## 3. The file

### Header

Lines starting with `#` are not data. The first block records the settings the numbers
depend on:

| Key | Meaning |
|---|---|
| `game` | version string and the exe's build time |
| `chart`, `title`, `difficulty`, `dlevel` | what was played |
| `play_speed` | `PlaySpeed/20`; at anything but 1.000 all times are on the sped-up clock |
| `input_adjust_ms` | `InputAdjustTimeDrums` when the play started (the per-row column has the value in effect for that row; it can be changed in play with the arrow keys) |
| `bgm_adjust_ms` | the chart's BGM adjust and the common `BGMAdjust` |
| `hit_range_ms`, `pedal_hit_range_ms` | the Perfect/Great/Good/Poor half-widths in force (pedals BD/LP/LBD have their own) |
| `velocity_min` | `VelocityMin` per pad: a hit at or below it is dropped |
| `groups` | `HHGroup`, `FTGroup`, `CYGroup`, `BDGroup` as their Config.ini numbers |
| `tight`, `buffered_input`, `vsync`, `sound_device` | other settings that affect timing |
| `autoplay_lanes` | lanes on auto (their chips are not logged unless you hit them) |
| `midi_in` | every MIDI input device, with the number used in `device` |
| `midi_binding` | every MIDI note bound to a drum pad in `KeyAssign` |

### Columns

| Column | Meaning |
|---|---|
| `song_ms` | Performance-timer time of the event, in ms. For an input it is exactly what the judgement uses: the event's timestamp minus the timer's last reset. For a `MISSED_CHIP` it is when the game gave up on the note (about the Poor window after it). |
| `segment` | 0 at the start; +1 at every `SEEK` row (practice-loop lap, `=` rewind, seek keys, Skip). Rows are sorted by segment, then time. |
| `input_id` | One number per input event. Two rows with the same id are one stroke that took two chips at the same position (a chord on grouped lanes). Empty for chip-only rows. |
| `pad` | The pad the input arrived on (`HH SD BD HT LT FT CY HHO RD LC LP LBD`). For a missed chip, the pad that lane belongs to. |
| `device` | `MIDI<n>` (see the `midi_in` header), `Keyboard`, `Joypad<n>` or `Mouse`. |
| `note_or_key` | MIDI note number, or the DirectInput key code for the keyboard (the number after `K` in `KeyAssign`). |
| `velocity` | MIDI velocity 1-127 (keyboard hits carry a fixed value). |
| `outcome` | See below. |
| `judgement` | `Perfect Great Good Poor` for a hit, `Miss` for a missed/skipped chip, `Auto` if you hit an auto-played lane's chip. |
| `chip_lane` | The chip's DTX channel in hex: `11` HH closed, `18` HH open, `1B` left pedal, `12` snare, `13` kick, `14` hi tom, `15` low tom, `17` floor tom, `16` crash, `1A` left crash, `19` ride, `1C` left bass drum. |
| `chip_ms` | The chip's time on the same clock as `song_ms`. |
| `lag_ms` | `song_ms + input_adjust_ms - chip_ms`, the judgement's own arithmetic (it is read back from the chip after the game computed it). **Positive = late, negative = early.** Empty for missed chips. |
| `bar` | The chip's bar as the `.dtx` file numbers it (`#nnn`). DTXMania inserts an empty bar in front of every chart; that is already taken off. |
| `beat` | Beat within the bar, 1-based, meter-aware: a 3/4 bar has beats 1-3.99, a 6/4 bar 1-6.99 (`#nnn02` bar lengths persist until changed, as the game reads them). |
| `input_adjust_ms` | The input adjust applied to this row (0 on an auto-played lane). |
| `detail` | Plain-English reason, for everything that is not a clean hit. |

### Outcomes

| Outcome | What happened |
|---|---|
| `HIT` | The input was matched to a chip and judged. The chip columns are that chip. |
| `IGNORED_VELOCITY` | Velocity at or below the pad's `VelocityMin`: the game dropped the hit before looking for a chip (this is meant to swallow crosstalk). The chip columns show the nearest chip on that pad's lanes, for context. |
| `NO_CHIP_IN_RANGE` | A real hit that took no chip. The chip columns are the nearest chip on the pad's lanes within 1 s, and `detail` says which case it is: the nearest chip was **already hit** (double trigger, flam, or an earlier stroke took it); it was **outside the Poor window**; it was in range but on a lane this pad does not reach with the current group settings; or there was no chip at all. `lag_ms` is then the distance to that nearest chip, not a judgement. |
| `MISSED_CHIP` | A note on a lane you play passed the bar unhit. |
| `SKIPPED_CHIP` | A note jumped over with the in-play Skip key (counted as a miss by the game). |
| `UNASSIGNED_NOTE` | A MIDI note-on that no drum pad is bound to. The game ignores these silently; they are usually a pad zone (rim, bell, choke, hi-hat pedal) sending a note that `KeyAssign` does not list. |
| `SEEK` | Song time jumps; `detail` gives from/to. Starts a new segment. |

Not logged: note-ons with velocity 0, and non-note MIDI messages (hi-hat pedal position CC and
the like). The MIDI input layer drops those before anything else sees them, and the log does not
change that layer.

### Summary

At the end, `#` lines that Excel still splits into columns:

- `# by_pad` — per pad and `ALL`: inputs, chips hit, Perfect/Great/Good/Poor, ignored for
  velocity, no chip in range, and the mean, median, standard deviation and mean absolute value
  of `lag_ms` over the hits.
- `# by_lane` — per chip lane: chips judged, the four judgements, missed, skipped, the same lag
  statistics.
- `# drift` — per 16 file bars: hits, misses, lag statistics. Compare the windows.
- `# unassigned_midi_notes` — how many, and which device:note sent them.

## 4. Reading it

- **A constant offset** shows as `by_pad ALL mean_lag` well away from 0 with most pads agreeing.
  Move `InputAdjustTimeDrums` by that amount (the sign is the same as the judgement's: a mean
  lag of +15 means you are judged 15 ms late; setting the adjust to -15 centres you). If only
  the pedals disagree, that is `PedalLagTime` territory.
- **Drift** shows as the `drift` windows' mean lag moving steadily one way through the song
  while your per-window spread stays the same. A jump at one window and flat either side is a
  tempo-map or bar-count problem at that point in the chart.
- **"I hit it and nothing happened"**: find the time, read `outcome` and `detail`. Filter the
  sheet on `outcome` to see all of one kind.

## 5. How it works inside

| Piece | File |
|---|---|
| The log: rows, the nearest-chip explanation, the writer and summary | `DTXMania/Code/Stage/07.Performance/DrumsScreen/CDrumHitLog.cs` |
| Created in `OnActivate`, written in `OnDeactivate`; begin/end of each input event; unassigned-MIDI scan | `CStagePerfDrumsScreen.cs` |
| Chip judged (hits and misses) in `tProcessChipHit`; `SEEK` in `tJumpInSong` | `CStagePerfCommonScreen.cs` |
| `DrumHitLog` option | `CConfigIni.cs`, `CActConfigList.cs` |

The drums input handler is a long switch with a `continue` on every path, so the log does not
try to follow it. Instead, each pressed event is made "current" before the switch; any chip the
game then judges while it is current is that event's hit (this is the single place every drum
judgement goes through), and whatever is still unmatched when the next event starts or the
frame's input ends is explained afterwards. A chip judged with no event current is a note that
went by (or was skipped). Nothing in the handler's decisions reads anything the log writes.

The CI smoke job has a run for it: compact mode with everything on auto except the snare, the
snare tapped from the keyboard for ~18 s, then Escape; it checks that exactly one CSV appeared,
with the BOM, header, rows for the taps, `MISSED_CHIP` rows, the lag arithmetic on every `HIT`
row and the summary.
