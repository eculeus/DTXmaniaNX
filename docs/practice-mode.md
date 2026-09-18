# Practice loop mode

Pick a section of a song before you play it, and the performance screen plays only that
section, over and over, without ever leaving the screen. Nothing is scored.

Off by default. With `PracticeMode=0` (the default) every screen behaves exactly as it did
before: song select has no extra key, the performance screen has no extra state, and the
in-play A/B loop that DTXManiaNX already had is untouched.

---

## 1. What the player does

1. `Config > Drums > PracticeMode` → **ON** (`PracticeMode=1` in `[PlayOption]`).
2. In song select, **Shift+F2** opens the PRACTICE panel for the highlighted chart.
3. The panel lists, in order:
   - `OFF - play the whole song`
   - every section from the song folder's `sections.def` (if there is one)
   - every range you have saved yourself, marked with a `*`
   - `New range...`
4. Enter picks one. The panel closes and song select shows a green
   `PRACTICE  <name>  [start - end (-lead)]` badge in the top-left corner.
5. Start the song as usual. It begins at the start of the range (one bar early, for
   sections), loops back there when it reaches the end, and keeps going until you press
   Escape.

`New range...` gives three fields — START bar, END bar, LEAD bars — edited with Left/Right
or by typing digits. `USE NOW` uses the range without saving it; `NAME & SAVE` asks for a
name and writes it to `PracticeRanges.ini`, so it is in the list next time. `Delete` on a
saved row removes it.

### In play

| Key | Practice loop ON | Otherwise |
|---|---|---|
| `=` (Restart) | rewinds to the start of the range, stays on the performance screen | leaves to the song-loading screen and replays from the top |
| Skip forward / backward | seeks, **clamped inside the range** | seeks freely |
| Skip (in-play skip) | same, clamped | seeks freely, skipped notes count as MISS |
| Speed up / down | works; the range is rescaled with the chart | works |
| Loop delete | **clears the practice range too**: the rest of the song plays out and seeking is free again | clears an A/B loop |
| Escape | back to song select | back to song select |

**Seeking clamps rather than escaping the range.** You asked for that section; silently
sliding out of it while practising would be worse than hitting a wall. Seeking forward past
the end lands exactly on the end, which the loop check then wraps on the next frame — so a
forward seek near the end simply takes you round again. The escape hatch is the Loop delete
key, which drops the range entirely.

### What a loop restart resets

Both the wrap at the end of the range and `=` call the same code:

- **Judgement counters** (Perfect / Great / Good / Poor / Miss) → 0
- **Early / late counters** → 0
- **Combo**, current and highest → 0 (highest is zeroed first, so the status panel's
  "max combo" is refreshed by the current-value setter rather than left stale)
- **True score** → 0
- **Gauge** → back to its starting value, via the existing `CActPerfCommonGauge.Init()`.
  Only in practice mode; the pre-existing in-play A/B loop keeps its old behaviour of
  carrying the gauge across a wrap. You cannot fail out either way, because practice sets
  training mode and `STAGE FAILED` is already gated on that.
- **Chips** are un-hit by `tJumpInSong`'s existing backward-seek branch, so the notes are
  playable again.
- **The notation view** needs nothing: both the scrolling staff and the page view derive
  everything from the performance timer and the chip list every frame, and `tJumpInSong`
  rewinds both. No practice code touches `CActPerfDrumsNotation.cs`.

So the numbers on screen always describe the lap you are on, which is what you want when
you are drilling eight bars.

### Nothing is scored

Applying a practice range sets `bIsTrainingMode = true` on the performance screen — the
same flag the existing speed-change and seek keys set. That one flag already gates
everything downstream:

- `score.ini` is not written (`CStageResult.cs:93`, `CDTXMania.cs:3145`)
- the rank and results are not calculated at all
- the fork's **named high scores** are suppressed: `bこの演奏は記録される` is false, so
  `<chart>.dtx.scores.ini` is not written and the result screen never asks for a name
  (`CStageResult.cs:925`)
- the result screen draws its "not saved" disclaimer
- the skill meter and the in-play score readout are blank

No new "don't save" flag was invented. If a future change adds another save path, wiring it
to `bIsTrainingMode` covers practice mode automatically.

---

## 2. The section file

### Location and name

A plain text file in the song folder, next to `set.def`:

```
<song folder>/sections.def                  ← per song folder (normal case)
<song folder>/<chart file>.sections.def     ← per chart, overrides the folder file
```

Per-folder is the normal case: the four difficulty charts of a song are generated from one
transcription, so they share a bar grid. The per-chart form exists for community rips whose
difficulties do not line up; if it is present the folder file is ignored for that chart.

It travels with the song folder, so it survives a Google Drive round trip, and the chart
workshop can generate it alongside the `.dtx` files.

### Encoding

**Shift_JIS**, like `.dtx` and `set.def` — with a BOM honoured if there is one, so a
UTF-8-with-BOM file also reads correctly. Section names are usually ASCII
(`Verse 1`, `Chorus`), which is identical in both, so a generator can safely emit plain
ASCII and not think about it. Emit UTF-8 **with** a BOM if you need non-ASCII names.

### Grammar

One directive per line. `;` starts a comment, to end of line. Blank lines are ignored.
Anything that is not a `#KEY: value` line is ignored, and unknown keys are ignored, so
older readers survive newer files.

```
#VERSION: 1                  ; format version. Ignored by the reader today; emit 1.
#BARBASE: DTX                ; how to read the bar numbers below. Only DTX is defined.
#LEADBARS: 1                 ; default prep lead, in bars, for every #SECTION below.

#SECTION: <start>,<end>,<name>[,<leadbars>]
```

- `#LEADBARS` may appear more than once; it applies to the `#SECTION` lines after it.
- `#SECTION` takes 2, 3 or 4 comma-separated fields. The name may contain spaces but not a
  comma (the name field is everything up to the third comma).
- A 4th field overrides `#LEADBARS` for that section only. Use `0` for sections that
  should start exactly where they are (a one-bar turnaround, say).

### Positions

A position is one of:

| Form | Meaning |
|---|---|
| `014` | bar 14, beat 1 |
| `030:4` | bar 30, beat 4 |
| `030:3.5` | bar 30, half way between beats 3 and 4 |
| `@12345` | 12345 ms from the start of the song |

Bars are the primary unit and the one to generate. **Times are a fallback** for material
with no usable bar grid; they are taken literally and are not adjusted for mixed meter,
tempo changes or the lead (a `@` start ignores `leadbars`).

**Beats are 1-based and meter-aware.** Beat 1 is the downbeat, and a bar has
`4 × <bar length>` beats: a 4/4 bar (`#nnn02` unset or `1.0`) has beats 1–4, a 6/4 bar
(`1.5`) has 1–6, a 3/4 bar (`0.75`) has 1–3. Fractions are allowed. This is safer than
seconds for exactly the reason bar lengths and tempo change: the same bar+beat means the
same musical place whatever the tempo map does.

### Bar numbering — read this once

**Bar numbers in the file are `.dtx` FILE bar numbers.** They are the `nnn` you see in
`#nnn13: 0013...`, counted from `000`.

DTXMania inserts an empty bar in front of every chart while loading
(`CDTX.cs`, `n小節番号++; // 先頭に空の1小節を設ける。`), so the game's internal bar
numbering is one higher than the file's. The conversion happens in exactly one place, in
`CPracticeTimeMap.nTickAt()`:

```csharp
int nInternalBar = nFileBar + 1;
...
return ( nInternalBar * 384 ) + (int) Math.Round( dbInBar * 384.0 );
```

Nothing else in the practice code knows about the offset. A generator only ever emits file
bar numbers — the same numbers it is already writing into the `.dtx`.

Note that the inserted bar takes real time: on a 120 BPM 4/4 chart, file bar `000` starts
at 2000 ms, not at 0. That falls out of the conversion automatically.

### The end is exclusive

`#SECTION: 009,014,Verse 1` covers bars 9 through 13. Bar 14 is where the loop jumps back.

That makes consecutive sections chain — section *n*'s end is section *n+1*'s start — which
is what a generator naturally produces, and it removes the "is the last bar included?"
argument. It also means the end position is literally "the moment the loop wraps", which is
how the engine uses it.

### The lead

The file stores the **true** section start. The one-bar run-up is applied when the range is
turned into a loop, from `#LEADBARS` or the per-section override, so it stays visible in
the file and in the panel (shown as `(-1)`) and can be edited rather than being baked into
the numbers.

The lead subtracts whole bars from the bar number and keeps the beat:
`030:3` with `#LEADBARS: 1` starts at `029:3`. It is clamped at bar `000`. A lead of `0`
starts exactly at the section.

### Invalid ranges

Validation happens once, when the performance screen activates and the chart is loaded —
song select cannot check it because the chart is not parsed yet.

- A start before the chart is clamped to 0.
- An end past the last chip is clamped to the last chip's time.
- If, after clamping, the range is shorter than 500 ms (which covers `end <= start`, both
  ends past the chart, and nonsense like `#SECTION: 050,050`), **the range is dropped**: a
  warning goes to `DTXManiaLog.txt` and the song plays normally from the top. Practice mode
  never leaves you stuck in a 0 ms loop or silently playing the wrong thing.
- Lines that do not parse are skipped with a warning; the rest of the file still loads.
- A missing `sections.def` is not an error. The panel just offers `OFF` and `New range...`.

### Worked example — "His Mercy Is More"

The debug fixture lives at [`../Tests/SectionFiles/his_mercy_is_more.sections.def`](../Tests/SectionFiles/his_mercy_is_more.sections.def).
This chart is the hard case on purpose: a 1/4 pickup in bar `001`, 6/4 (`#nnn02: 1.500000`)
for the body, single 3/4 turnaround bars at `030`, `051` and `087`, and a ritardando that
puts **six `#09508` BPM chips inside bar 095**.

```
#VERSION: 1
#BARBASE: DTX
#LEADBARS: 1

#SECTION: 000,009,Intro (no drums)
#SECTION: 009,014,Verse 1
#SECTION: 014,020,Verse 2 (hats)
#SECTION: 020,030,Chorus 1
#SECTION: 030,031,Turnaround 1 (3/4),0
#SECTION: 031,039,Verse 3
#SECTION: 039,042,Verse 4 (hats)
#SECTION: 042,051,Chorus 2
#SECTION: 051,052,Turnaround 2 (3/4),0
#SECTION: 052,060,Chorus 3
#SECTION: 060,063,Breakdown (drums out)
#SECTION: 063,069,Bridge build
#SECTION: 069,078,Chorus 4
#SECTION: 078,081,Quiet
#SECTION: 081,088,Build
#SECTION: 088,096,Final chorus
#SECTION: 096,099,Outro (rit.)
```

Reading that file:

- `Chorus 1` is bars 020–029 inclusive, and with the default one-bar lead the loop actually
  starts at bar 019 — a whole 6/4 bar of run-up, six beats, not four, because the lead is
  measured in bars.
- The two turnarounds carry an explicit `,0`: they are one bar long, so a one-bar lead would
  double them.
- `Outro (rit.)` ends at `099`, one past the chart's last bar `098`. That is the correct way
  to say "to the end": the end is exclusive, and the clamp trims it to the last chip.
- Nothing in the file mentions tempo. Bar 095's six BPM chips are handled because the
  bar → ms conversion never computes a tempo itself (see below).

### Emitting one from Python

A generator needs: the list of section boundaries as `.dtx` file bar numbers, in order, with
the end of each equal to the start of the next; a name per section; `0` in the 4th field for
sections one bar long; and the file written next to `set.def` as `sections.def` in
Shift_JIS-compatible ASCII (or UTF-8 with a BOM).

```python
def write_sections(path, sections, lead_bars=1):
    """sections: [(start_bar, end_bar, name), ...] in .dtx file bar numbers, end exclusive."""
    lines = [
        "; Practice sections. Bar numbers are .dtx file bars; end is exclusive.",
        "#VERSION: 1",
        "#BARBASE: DTX",
        "#LEADBARS: %d" % lead_bars,
        "",
    ]
    for start, end, name in sections:
        name = name.replace(",", " ").strip()
        if end - start <= 1:                 # a one-bar turnaround gets no run-up
            lines.append("#SECTION: %03d,%03d,%s,0" % (start, end, name))
        else:
            lines.append("#SECTION: %03d,%03d,%s" % (start, end, name))
    with open(path, "w", encoding="shift_jis", newline="\r\n") as f:
        f.write("\n".join(lines) + "\n")
```

---

## 3. Where the user's own ranges live

**Not in the song folder.** `<game folder>/PracticeRanges.ini`, next to `Config.ini`.

The fork's own per-song user data (`<chart>.dtx.scores.ini`, and upstream's
`<chart>.dtx.score.ini`) does sit in the song folder, and this deliberately does not follow
that. Song folders here are downloaded from Google Drive and replaced wholesale when a
chart is re-uploaded; a high score lost that way is annoying, but a set of hand-entered
practice ranges lost that way is the feature being useless. Ranges therefore live with the
game, which is upgraded by unzipping over the top and never deletes `Config.ini`.

UTF-8, INI-shaped, one section per song **folder**:

```ini
; DTXManiaNX - practice ranges you saved yourself.
; One section per song folder; "name=start,end,leadbars" per range.
; Bar numbers are .dtx file bar numbers. See docs/practice-mode.md.

[dtxfiles.worship/his mercy is more]
CHORUS TAG=042,051,0
THE FILL=068,070,1
```

The section key is the song folder's **parent and leaf folder names**, lower-cased and
joined with `/`. Not the full path: that keeps the ranges when the drive letter changes or
the whole `DTXFiles` tree moves, which is the normal way this library gets handled. Two
different folders with the same parent+leaf names would share ranges; that is the accepted
cost, and it is the same shape of collision `set.def` already lives with.

Keying on the folder (not the chart file) means one saved range covers BASIC through MASTER
of the same song, which is right when the four charts come from one transcription. Shipped
sections work the same way by default.

Values are the same three fields as a `#SECTION`, in the same syntax, so the same parser
reads both. A name is stored as written, with `=` and `,` replaced, and is limited to 20
characters by the panel.

---

## 4. How it works inside

DTXManiaNX already had an in-play A/B loop — `LoopBeginMs` / `LoopEndMs` on
`CStagePerfCommonScreen`, set with the `LoopCreate` / `LoopDelete` keys (both unbound by
default). Practice mode does not replace it: it **pre-seeds the same two fields** from song
select. Everything that already worked with an A/B loop — the wrap, the on-screen loop
markers, the rescale when you change speed — works for a practice range for free.

| Piece | File |
|---|---|
| Range model, section-file reader, user-range store, bar↔ms map | `DTXMania/Code/Score,Song/CPracticeSections.cs` (new) |
| Song select panel and the `PRACTICE` badge | `DTXMania/Code/Stage/05.SongSelection/CActSelectPracticePanel.cs` (new) |
| Shift+F2, modal gate, clearing the range when the song changes | `DTXMania/Code/Stage/05.SongSelection/CStageSongSelection.cs` |
| The chosen range, handed to the performance screen | `CDTXMania.rPracticeRange` |
| Setup, start jump, rewind, seek clamp, counter reset, wrap | `DTXMania/Code/Stage/07.Performance/CStagePerfCommonScreen.cs` |
| Calling the start jump and the wrap in the right order | `CStagePerfDrumsScreen.cs`, `CStagePerfGuitarScreen.cs` |
| `PracticeMode` option | `CConfigIni.cs`, `CActConfigList.cs` |

### bar + beat → ms

`CPracticeTimeMap` is built once, when the performance screen activates, from the loaded
chart. It does two things:

1. **bar + beat → tick.** Ticks are 384 per bar regardless of bar length. Bar length comes
   from the `EChannel.BarLength` chips, walked in order with the value **persisting** until
   the next one — reading `#nnn02` per bar instead would silently corrupt every 3/4 and 6/4
   song. `nInternalBar = nFileBar + 1` is applied here and nowhere else.

2. **tick → ms** by looking up the game's own `CChip.nPlaybackTimeMs` and interpolating
   linearly between the two chips that bracket the tick.

Step 2 is the important one. It never computes a tempo. The loader has already placed a
`BarLine` chip at every bar and a `BeatLine` chip at every beat and stamped all of them with
the time it calculated, tempo map and all — so interpolating between adjacent chips is
exact, because no BPM chip and no bar-length change can sit strictly between two adjacent
chips without being a chip itself. That is what makes bar 095 of "His Mercy Is More", with
six tempo changes inside one bar, come out right without any special case.

### Lifetime

```
song select:  Shift+F2 → panel → CDTXMania.rPracticeRange = <range>   (bars)
              Decide  → tSelectSong() clears it if PracticeMode is off
song loading: chart is parsed, chip times computed
performance:  OnActivate  → tPracticeLoop_Setup()
                            bars → ms, validate, LoopBeginMs/LoopEndMs, bIsTrainingMode = true
              first frame → tPracticeLoop_OnPlayStart() → tJumpInSong(LoopBeginMs)
              every frame → tCheckLoopWrap()  (before the STAGE CLEAR test)
              '='         → tPracticeLoop_Rewind()
              speed keys  → tChangePlaySpeed() rescales the chart AND the loop bounds
result:       bIsTrainingMode → nothing saved
```

Two ordering details that are easy to get wrong and are commented in the source:

- **The wrap is tested before the stage-clear test.** It used to sit near the bottom of
  `OnUpdateAndDraw`, after the clear check. A range that ends at the end of the song would
  run `nCurrentTopChip` past the end of the chip list, trip `bIsFinishedPlaying`, and the
  song would end instead of looping.
- **The start jump happens in the `bJustStartedUpdate` block**, after the two timers have
  been reset — the same place DTX Viewer mode does its `tJumpInSongToBar`. Seeking before
  the reset would be undone by it.

### Known limits

- The panel takes the same pad input as the sort and quick-config popups (HT/LT or R/G to
  move, Decide or RD to pick, LC or Cancel to back out) as well as the arrows, Enter, Escape
  and Delete. Typing a range or a name is keyboard-only, and the name field ignores the pads
  entirely — LC is bound to A and Z by default, so a pad would eat the letters.
- Shift+F2 is a fixed key, not a bindable `[SystemKeyAssign]` entry. It reuses the slot the
  upstream source has kept free for "some other use in the future" since 2011.
- Section names are drawn with the song-list font; non-Latin names should work but have not
  been looked at.
- A range is remembered while you stay on the same chart in song select — including after
  you come back from playing it, so you can go straight round again — and is dropped when
  you move to another song.
