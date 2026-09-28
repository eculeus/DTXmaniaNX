# Drum timing calibration

`Config > Drums > Calibrate...` measures how early or late you hit against what you hear, and
suggests the `InputAdjust` (`InputAdjustTimeDrums` in `Config.ini`) that centres your hits —
Melodics-style, instead of guessing a value and playing a song to see.

Nothing changes unless you choose **Apply**.

---

## 1. Using it

1. `Config` → `Drums` → `Calibrate...` → Enter (or a cymbal pad).
2. A click plays at 120 BPM and four dots show the beat. The first **4 clicks are a count-in**
   and are not counted; after that, play along on **any drum pad** (or its keyboard key).
   The screen stops by itself after **24 counted hits**.
3. While you play it shows the last hit (`+` late, `-` early, in ms), the running median, the
   spread (standard deviation), and every hit as a tick on a ±150 ms line. The blue band is the
   Perfect window as it stands with your current `InputAdjust`.
4. At the end: median, spread, how many hits were used, and the **suggested InputAdjust**, with
   a dashed box showing where the Perfect window would sit with it.
   - **Apply** — sets `InputAdjust` to the suggestion (the menu item changes too). `Config.ini`
     is written with everything else when you leave CONFIG.
   - **Retry** — runs it again.
   - **Cancel** (or Esc) — closes; nothing changes.

   Left/Right (or the HT/LT pads) choose, Enter (or a cymbal) confirms, Esc (or LC) cancels. For
   the first 0.8 s after the results appear these are ignored, so a late stroke on the crash does
   not pick something for you. Esc during the run cancels at once.

## 2. What it measures

The same thing the judgement sees. Each hit's time is the input event's own timestamp — the one
the performance screen judges with, on `CSoundManager.rcPerformanceTimer` — not the frame it was
noticed in. The click is one WAV played through the game's normal sound output and pinned to that
same timer the way the BGM is (started, then moved to "now − start"). So the offset includes:

- the **audio output latency** (buffer, driver, interface) — you hear the click late, so you hit late;
- the **input latency** (MIDI / USB / module processing);
- **your own habit** of playing ahead of or behind the beat.

That sum is exactly what `InputAdjust` corrects. It does **not** measure **display latency**:
the dots on screen are only a guide. If you play to the screen rather than the sound, the number
will be off; listen.

The per-song and global BGM offsets (`#BGMADJUST`, `BGMAdjustTime`) move the music against the
chart, not your hits against the chart, and are not involved. If you use a non-zero
`BGMAdjustTime`, calibrate with it at 0 first.

## 3. The arithmetic

- Each hit is paired with its **nearest click** (±250 ms at 120 BPM). Hits nearest a count-in
  click, or after the last click, are ignored.
- A hit more than **±150 ms** from its click is not counted ("too far off").
- When the run ends, hits further than **3 × MAD** (median absolute deviation, floored at 2 ms)
  from the median are dropped as strays; the median and standard deviation are of the rest.
- At least **8** hits must remain, otherwise there is no suggestion (Apply is disabled).
- The judgement computes `lag = hit + InputAdjust − note`. The measured offsets are raw
  (`hit − click`, with no `InputAdjust` applied), so the value that puts the median on 0 is
  **`−median`**, rounded (halves away from zero) and clamped to −99..99. It is an absolute value,
  not a change to the current one, so running it again after Apply suggests about the same number.

The spread line compares your spread with the Perfect window: about 95% of hits fall within
±2 SD of your average. If that is wider than `PerfectRange`, centring alone will not make them
Perfect; widening `PerfectRange` (same menu) is the other knob.

## 4. Where the code is

| What | File |
|---|---|
| Pairing, statistics, suggestion, click WAV (game-free, checked in CI by `Tests/Calibration`) | `DTXMania/Code/Stage/04.Config/CCalibrationMath.cs` |
| The screen: click playback, input, drawing, Apply/Retry/Cancel | `DTXMania/Code/Stage/04.Config/CActConfigCalibration.cs` |
| Menu item, Apply → `InputAdjust` item | `CActConfigList.cs` (`iDrumsCalibrate`, `tSetDrumsInputAdjust`) |
| Hand-over of input while it is open | `CStageConfig.cs` (`EItemPanelMode.Calibration`) |

Each run writes to `DTXManiaLog.txt`: the settings it started with, every paired offset, and the
summary line with the suggestion (and whether it was applied).
