using System;
using System.Collections.Generic;
using System.Drawing;
using SharpDX;
using FDK;

using Rectangle = System.Drawing.Rectangle;
using Color = System.Drawing.Color;

namespace DTXMania
{
    /// <summary>
    /// Notation view for the drums screen: a five-line percussion staff that scrolls from right to
    /// left past a fixed playhead, with standard drum-notation noteheads instead of vertical lanes.
    /// Gameplay, judgement and scroll speed are untouched; only the drawing changes. The stage
    /// drives it: tDrawStaff() once per frame before the chip loop, then tDrawBarLine / tDrawBeatLine
    /// from the bar-line loop and tDrawChip from the chip loop (which only collects the chips), and
    /// finally tDrawNotes() after the chip loop, which draws ledger lines, chord stems and heads.
    /// Enabled by Config.ini DrumsNotationView=1.
    /// </summary>
    internal class CActPerfDrumsNotation : CActivity
    {
        // ------------------------------------------------------------------ //
        //  Band geometry (1280x720 screen).                                   //
        //  The band sits above the status panel (which starts at y=250 in     //
        //  vertical-lane mode and is pushed to PANEL_Y here), so nothing      //
        //  overlaps the staff.  Tune these from a screenshot if needed.       //
        // ------------------------------------------------------------------ //
        public const int PLAYHEAD_X = 150;                          // notes are hit when they reach this x:
                                                                    // right after the legend, so nearly the whole
                                                                    // band is upcoming music
        public const int STAFF_SPACE = 36;                          // px between two staff lines
        public const int STAFF_STEP = STAFF_SPACE / 2;              // one staff position (line -> space) = 18
        public const int STAFF_BOTTOM_Y = 380;                      // y of the bottom line
        public const int STAFF_TOP_Y = STAFF_BOTTOM_Y - 4 * STAFF_SPACE;    // 236, five lines spanning 144 px
        public const int BAND_TOP_Y = 0;                            // dark backing strip: the top 65% of the screen
        public const int BAND_BOTTOM_Y = 470;
        public const int STEM_TOP_Y = STAFF_BOTTOM_Y - 242;         // 138: up-stems end here, just over the second
                                                                    // ledger line, and the beams sit on that line
        public const int STEM_BOTTOM_Y = STAFF_BOTTOM_Y + 80;       // 460: every down-stem ends here
        public const int BAR_NUMBER_Y = 4;                          // bar number text, along the band top
        public const double X_SCALE = 0.80;                         // horizontal px per vertical-lane px.
                                                                    // The engine gives us (SPEED * 0.3575) lane px
                                                                    // per ms, so at the SPEED 2.0 setting this shows
                                                                    // 1130/(0.80*0.3575) = 3950 ms ahead, about two
                                                                    // bars of 4/4 at 120 BPM, with eighths 72 px
                                                                    // apart. The in-game SPEED setting still scales
                                                                    // it: SPEED 1.0 shows twice as much, 4.0 half.
        public const int LOOKAHEAD_PX = 1600;                       // the chip loop normally stops feeding us chips
                                                                    // past 600 lane px, which at this X_SCALE would
                                                                    // leave the right half of the band empty

        // Where the rest of the drums HUD goes while the notation band owns the top 65% of the screen.
        // All of these are only used when ConfigIni.bDrumsNotationView is on.
        public const int PROGRESS_X = 0;                            // song progress bar, laid out horizontally
        public const int PROGRESS_Y = BAND_BOTTOM_Y + 2;            // directly under the band
        public const int PROGRESS_W = 1280;
        public const int PROGRESS_H = 18;                           // as thick as the stock vertical bar is wide
        public const int PROGRESS_MARKER_W = 3;                     // opaque white tick at the current position
        public const int PANEL_X = 6;                               // status panel, scaled down, bottom left
        public const int PANEL_Y = 494;
        public const float PANEL_SCALE = 0.51f;                     // 257x439 -> 131x224, so it fits in 494..718
        public const int SCORE_X = 160;                             // score, to the right of the panel
        public const int SCORE_Y = 500;
        public const int PLAYSPEED_X = 160;                         // "Play Speed" text
        public const int PLAYSPEED_Y = 588;
        public const int SKIP_X = 160;                              // SKIP toast
        public const int SKIP_Y = 624;
        public const int COMBO_X = 940;                             // combo digits (right edge); the "COMBO" caption
        public const int COMBO_Y = 510;                             // is dropped here, there is no room for it
        public const int GAUGE_Y = 672;                             // Excite Gauge + SPEED badge, along the bottom
        public const int MOVIE_X = 956;                             // picture-in-picture movie (CActPerfAVI window
        public const int MOVIE_Y = 494;                             // mode), bottom right
        public const float MOVIE_SCALE = 0.70f;                     // its 416x234 box -> 291x164
        public const int TITLE_X = 956;                             // song title / artist, under the movie
        public const int TITLE_Y = 662;
        public const bool MOVIE_HIDE_JACKET = true;                 // the tilted jacket card lives in the same
                                                                    // corner, so drop it rather than cover the movie

        // The judgement popup is drawn at the playhead, at the staff height of the lane it judges, so
        // it reads as belonging to that note. Small, and lifted clear of the head it belongs to.
        // ------------------------------------------------------------------ //
        //  Page view (DrumsNotationView=2): two fixed staves inside the same  //
        //  band. Notes do not move; a playhead sweeps across the top one, and //
        //  when it reaches the end the lower line becomes the upper one.      //
        // ------------------------------------------------------------------ //
        public const int PAGE_SPACE = 24;                           // staff spacing in page mode; two systems of
                                                                    // 225 px have to fit in the 470 px band
        public const int PAGE_SYSTEM_DY = 225;                      // bottom line of one system to the next
        public const int PAGE_BOTTOM_0 = 185;                       // bottom line of the upper system
        public const int PAGE_STEM_TOP_DY = -167;                   // beam line, relative to a system's bottom line
        public const int PAGE_STEM_BOTTOM_DY = 40;
        public const int PAGE_BAR_NUMBER_DY = -179;
        public const int PAGE_HIT_ALPHA = 102;                      // 40%: played notes stay readable on the page
        public const int PAGE_RIGHT_MARGIN = 10;
        public const int PAGE_FLASH_W = 160;                        // the hit flash is a short trail behind the
                                                                    // playhead here, not the whole played line
        public const int PAGE_LEFT_PAD = 24;                        // px between the gutter and a line's first bar
                                                                    // line, so the first note's head is not clipped
        public const int PAGE_BAR_PAD_L = 34;                       // engraving gap after a bar line to the centre
                                                                    // of a note on beat 1 (about a staff space
                                                                    // plus a note head), and ...
        public const int PAGE_BAR_PAD_R = 10;                       // ... before the closing bar line: no note
                                                                    // ever sits on, or outside, its bar lines
        public const int PAGE_SWAP_LEAD_MS = 300;                   // a staff has finished fading its next line in
                                                                    // at least this long before that line is played

        public const int JUDGE_X = PLAYHEAD_X;                      // judgement popup anchor in scroll mode
        public const int JUDGE_GAP = 12;                            // its right edge sits this far left of the
                                                                    // playhead, in the region already played
        public const int JUDGE_RISE = 26;                           // its bottom edge sits this far over the head
        public const float JUDGE_SCALE = 0.9f;                      // nearly stock size; it is clamped to x >= 2,
                                                                    // so it may overlap the legend gutter

        // Sizes derived from the staff spacing (36 px).
        private const int HEAD_W = 48;              // round notehead cell -> 38 x 29 px head (1.05 x 0.8 spaces)
        private const int XHEAD_W = 40;             // hi-hat x cell       -> 36 px = one space, 4.5 px strokes
        private const int CIRCLEX_W = 42;           // circled x (open hi-hat) -> 37 px ring, matching the x
        private const int BOLDX_W = 50;             // crash x cell        -> 45 px = 1.25 spaces, 8 px strokes
        private const int DIAMOND_W = 48;           // ride diamond cell   -> 37 px wide
        private const int STEM_W = 4;
        private const int STEM_BITE = 4;            // round heads: the stem is set this far inside the right edge
        private const int LEDGER_W = 54;
        private const int LEDGER_H = 4;
        private const int LINE_H = 3;               // staff line thickness
        private const int BAR_LINE_LEAD = 30;       // bar / beat lines are drawn this far left of the notes
                                                    // on that beat, so the downbeat head has clear space
                                                    // after the line instead of sitting against it
        private const int BEAM_H = 5;
        private const int BEAM_GAP = 10;
        /// <summary>Stems and beams are off by default: most players read the heads faster without them.</summary>
        private static bool bStems { get { return CDTXMania.ConfigIni.bDrumsNotationStems; } }

        // Lane legend down the left edge of the band: one column, one label per lane. At 36 px
        // spacing the lanes are 18 px apart, so a ~17 px glyph fits without stacking.
        private const int LABEL_RIGHT_PAD = 10;     // labels are right-aligned and end this far before the staff
        private const int LABEL_GUTTER_W = 110;     // darker strip the legend sits on; the staff lines and the
                                                    // playhead region start after it so nothing strikes the text
        private const int LABEL_FONT_SIZE = 15;     // points -> about 14 px of capital, which still clears
                                                    // the 18 px between two lane positions

        // Hit feedback: the lane the player just hit lights up across the part of the band that has
        // already gone by, plus a glow at the playhead, fading out over LANE_FLASH_MS.
        private const int LANE_FLASH_MS = 200;
        private const int LANE_FLASH_ALPHA = 120;
        private const int LANE_GLOW_ALPHA = 210;
        private const int LANE_GLOW_W = 40;
        private const int POS_MIN = -1;             // lowest staff position any voice uses (left pedal)
        private const int POS_MAX = 12;             // highest (left crash on its second ledger line)

        // Sprite sheet: 6 shape columns x 11 colour rows of 64x64 cells.
        private const int CELL = 64;
        private const int SHAPE_SOLID = 0, SHAPE_HEAD = 1, SHAPE_X = 2, SHAPE_CIRCLE_X = 3, SHAPE_DIAMOND = 4,
                          SHAPE_BOLD_X = 5;
        // ----------------------------------------------------------------- //
        //  Palette: one hue per instrument, so a glance at the colour names   //
        //  the drum even before the row and the notehead shape do. The staff  //
        //  furniture (lines, ledgers, stems, beams) stays plain white.        //
        // ----------------------------------------------------------------- //
        private const int C_WHITE = 0,      // staff lines, ledger lines, stems, beams
                          C_HAT   = 1,      // 60,170,255  hi-hat closed and open, and the hat foot
                          C_SNARE = 2,      // 255,205,40  snare
                          C_HITOM = 3,      // 92,224,116  hi tom
                          C_LOTOM = 4,      // 255,72,72   lo tom
                          C_FLOOR = 5,      // 255,150,30  floor tom
                          C_DARK  = 6,      // band background
                          C_PLAYHEAD = 7,
                          C_KICK  = 8,      // 240,240,240 kick and left bass drum
                          C_CRASH = 9,      // 255,61,140  crash: both crashes share one row
                          C_RIDE  = 10;     // 168,223,255 ride

        private CTexture tx;

        // Geometry of the system currently being laid out. Scroll mode has one system and these
        // hold the scroll-mode constants, so that path is unchanged; page mode sets them per staff.
        private int nBaseY = STAFF_BOTTOM_Y;        // y of this system's bottom staff line
        private int nSpace = STAFF_SPACE;           // staff spacing
        private int nStep = STAFF_STEP;             // half of it, one staff position
        private int nStemTopY = STEM_TOP_Y;         // beam line
        private int nStemBotY = STEM_BOTTOM_Y;
        private int nHeadX = PLAYHEAD_X;            // where the playhead is right now

        // The judgement string and the lane flash need these from outside, once per frame.
        private static int nActiveBaseY = STAFF_BOTTOM_Y;
        private static int nActiveStep = STAFF_STEP;
        private static int nActiveJudgeX = JUDGE_X;

        /// <summary>Point the layout at one staff system.</summary>
        private void tSetSystem(int nBottomY, int nSpacing, int nStemTop, int nStemBottom)
        {
            this.nBaseY = nBottomY;
            this.nSpace = nSpacing;
            this.nStep = nSpacing / 2;
            this.nStemTopY = nStemTop;
            this.nStemBotY = nStemBottom;
        }

        /// <summary>Scale a scroll-mode pixel size to the spacing of the current system.</summary>
        private int nSc(int nValue)
        {
            int n = nValue * this.nSpace / STAFF_SPACE;
            return (n < 1) ? 1 : n;
        }

        private static bool bPage { get { return CDTXMania.ConfigIni.bDrumsNotationPage; } }

        private int[] nBarMs;                       // page view: bar start times ...
        private int[] nBarNumber;                   // ... and the bar number printed at each one
        private int[] nBeatMs;                      // the engine's own beat lines (they already follow
                                                    // each bar's length: none in a 1-beat pickup, two in 3/4)
        private int nPageLastLine = -1;             // the line the playhead was on last frame (log only)
        private int[] nLineMs;                      // the time each staff line starts at ...
        private int[] nLineFirstBar;                // ... and its first bar's index in nBarMs
        private double dbPxPerMs;                   // one scale for the whole song, so the playhead is linear
        private int nSongEndMs;
        private int nBarSearchHint, nLineSearchHint;

        // What each staff shows, and the swap it has been given: the line it will show next and the
        // schedule for getting there (hold, fade the old line out until mid, fade the new one in
        // until end). nSystemNextLine < 0 means no swap is pending.
        private readonly int[] nSystemLine = new int[] { -1, -1 };
        private readonly int[] nSystemNextLine = new int[] { -1, -1 };
        private readonly long[] nSwapStartMs = new long[] { 0, 0 };
        private readonly long[] nSwapMidMs = new long[] { 0, 0 };
        private readonly long[] nSwapEndMs = new long[] { 0, 0 };

        /// <summary>One drum voice: where it sits on the staff, how it is drawn and in which lane colour.</summary>
        private struct STNote
        {
            public int nPos;        // staff position: 0 = bottom line, 1 = first space, ... 8 = top line,
                                    // 10 = first ledger line above, 12 = second ledger line above
            public int nShape;      // notehead sprite column
            public int nColour;     // sprite row; the hue the vertical lane uses for that chip
            public bool bStemUp;    // hands up, feet down
            public STNote(int pos, int shape, int colour, bool up) { nPos = pos; nShape = shape; nColour = colour; bStemUp = up; }
        }

        // Standard drum-set notation on a percussion staff. The staff position and the notehead
        // shape name the instrument (x = hi-hat, circled x = open hi-hat, diamond = ride, bold x =
        // crash, ellipse = drum), and so does the colour.
        private static readonly Dictionary<EChannel, STNote> mapNotes = new Dictionary<EChannel, STNote>
        {
            { EChannel.HiHatClose,   new STNote( 9, SHAPE_X,        C_HAT   , true ) },
            { EChannel.HiHatOpen,    new STNote( 9, SHAPE_CIRCLE_X, C_HAT   , true ) },
            { EChannel.LeftPedal,    new STNote(-1, SHAPE_X,        C_HAT   , false) },
            { EChannel.RideCymbal,   new STNote( 8, SHAPE_DIAMOND,  C_RIDE  , true ) },
            { EChannel.Cymbal,       new STNote(10, SHAPE_BOLD_X,   C_CRASH , true ) },
            { EChannel.LeftCymbal,   new STNote(10, SHAPE_BOLD_X,   C_CRASH , true ) },   // one crash row, like Melodics
            { EChannel.Snare,        new STNote( 5, SHAPE_HEAD,     C_SNARE , true ) },
            { EChannel.HighTom,      new STNote( 7, SHAPE_HEAD,     C_HITOM , true ) },
            { EChannel.LowTom,       new STNote( 6, SHAPE_HEAD,     C_LOTOM , true ) },
            { EChannel.FloorTom,     new STNote( 3, SHAPE_HEAD,     C_FLOOR , true ) },
            { EChannel.BassDrum,     new STNote( 1, SHAPE_HEAD,     C_KICK  , false) },
            { EChannel.LeftBassDrum, new STNote( 1, SHAPE_HEAD,     C_KICK  , false) },
        };
        /// <summary>One entry of the legend down the left edge, indexed by ELane (0..9).</summary>
        private struct STLaneLabel
        {
            public int nPos;
            public int nColour;
            public string strText;
            public STLaneLabel(int pos, int colour, string text) { nPos = pos; nColour = colour; strText = text; }
        }

        // Indexed exactly like CStagePerfCommonScreen.nチャンネル0Atoレーン07 / ELane.
        private static readonly STLaneLabel[] stLaneLabels = new STLaneLabel[]
        {
            new STLaneLabel(10, C_CRASH, ""       ),        // 0  LC, merged into the CRASH row
            new STLaneLabel( 9, C_HAT,   "HI-HAT" ),        // 1  HH (and open hi-hat)
            new STLaneLabel( 5, C_SNARE, "SNARE"  ),        // 2  SD
            new STLaneLabel( 1, C_KICK,  "KICK"   ),        // 3  BD
            new STLaneLabel( 7, C_HITOM, "HI TOM" ),        // 4  HT
            new STLaneLabel( 6, C_LOTOM, "LO TOM" ),        // 5  LT
            new STLaneLabel( 3, C_FLOOR, "FLOOR"  ),        // 6  FT
            new STLaneLabel(10, C_CRASH, "CRASH"  ),        // 7  CY
            new STLaneLabel(-1, C_HAT,   "L.PEDAL"),        // 8  LP (and left bass drum)
            new STLaneLabel( 8, C_RIDE,  "RIDE"   ),        // 9  RD
        };
        /// <summary>A chip the stage has handed us this frame, ready to be laid out.</summary>
        private struct STPendingNote
        {
            public int x;
            public int nPlaybackPosition;
            public int nAlpha;
            public STNote note;
        }

        private readonly List<STPendingNote> listNotes = new List<STPendingNote>(256);

        private CTexture[] txLaneLabel;                                     // one per ELane, null when not shown
        private CPrivateFastFont pfLabel;
        // hit feedback, indexed by staff position + 1 so the left pedal (-1) fits
        private readonly CCounter[] ctLaneFlash = new CCounter[POS_MAX - POS_MIN + 1];
        private readonly int[] nLaneFlashColour = new int[POS_MAX - POS_MIN + 1];

        public CActPerfDrumsNotation()
        {
            base.bNotActivated = true;
        }

        public override void OnManagedCreateResources()
        {
            if (!base.bNotActivated)
            {
                this.tx = CDTXMania.tGenerateTexture(CSkin.Path(@"Graphics\7_notation.png"));
                this.nLineMs = null;            // rebuilt for this song on the first page-mode frame
                tCreateLaneLabels();
                base.OnManagedCreateResources();
            }
        }

        public override void OnManagedReleaseResources()
        {
            if (!base.bNotActivated)
            {
                CDTXMania.tReleaseTexture(ref this.tx);
                if (this.txLaneLabel != null)
                {
                    for (int i = 0; i < this.txLaneLabel.Length; i++)
                        CDTXMania.tReleaseTexture(ref this.txLaneLabel[i]);
                    this.txLaneLabel = null;
                }
                CDTXMania.t安全にDisposeする(ref this.pfLabel);
                base.OnManagedReleaseResources();
            }
        }

        /// <summary>
        /// One small coloured caption per lane, drawn once into a texture. Lanes the chart never
        /// uses (left crash, left pedal, ride, floor tom) are left out so the legend stays short.
        /// </summary>
        private void tCreateLaneLabels()
        {
            if (!CDTXMania.ConfigIni.bDrumsNotationView) return;    // nothing to draw in vertical-lane mode
            this.txLaneLabel = new CTexture[stLaneLabels.Length];
            try
            {
                this.pfLabel = new CPrivateFastFont(new FontFamily(CDTXMania.ConfigIni.str曲名表示フォント), LABEL_FONT_SIZE, FontStyle.Bold);
            }
            catch (Exception)
            {
                this.pfLabel = null;
                return;
            }
            for (int i = 0; i < stLaneLabels.Length; i++)
            {
                // every row is labelled whether or not this chart uses it: a missing RIDE or FLOOR
                // caption reads as a bug, and the legend is a key to the staff, not to the song
                if (stLaneLabels[i].strText.Length == 0) continue;
                try
                {
                    using (Bitmap bmp = this.pfLabel.DrawPrivateFont(stLaneLabels[i].strText, colLane(stLaneLabels[i].nColour), Color.Black))
                    {
                        this.txLaneLabel[i] = CDTXMania.tGenerateTexture(bmp, false);
                    }
                }
                catch (Exception)
                {
                    this.txLaneLabel[i] = null;
                }
            }
        }

        /// <summary>The sprite sheet colour rows again, as System.Drawing colours for the label font.</summary>
        private static Color colLane(int nColour)
        {
            switch (nColour)
            {
                case C_HAT:   return Color.FromArgb( 60, 170, 255);
                case C_SNARE: return Color.FromArgb(255, 205,  40);
                case C_HITOM: return Color.FromArgb( 92, 224, 116);
                case C_LOTOM: return Color.FromArgb(255,  72,  72);
                case C_FLOOR: return Color.FromArgb(255, 150,  30);
                case C_KICK:  return Color.FromArgb(240, 240, 240);
                case C_CRASH: return Color.FromArgb(255,  61, 140);
                case C_RIDE:  return Color.FromArgb(168, 223, 255);
                default:      return Color.White;
            }
        }

        public override int OnUpdateAndDraw()
        {
            return 0;   // drawing is driven by the stage so it interleaves with the chip loop
        }

        /// <summary>Staff y of a lane (ELane 0..9), i.e. the height its noteheads are drawn at.</summary>
        /// <summary>Where the judgement popup is anchored: the playhead, wherever it is this frame.</summary>
        public static int nJudgeX { get { return nActiveJudgeX; } }

        public static int nLaneY(int nLane)
        {
            if (nLane < 0 || nLane >= stLaneLabels.Length) return nActiveBaseY - 5 * nActiveStep;
            return nActiveBaseY - stLaneLabels[nLane].nPos * nActiveStep;
        }

        /// <summary>Screen x for a chip given its (vertical-lane) distance from the judgement line.</summary>
        public int nX(int nDistanceFromBar)
        {
            return PLAYHEAD_X + (int)(nDistanceFromBar * X_SCALE);
        }

        /// <summary>Dark band, hit flashes, five staff lines and the playhead. Call before the chip loop.</summary>
        public void tDrawStaff()
        {
            this.listNotes.Clear();
            if (this.tx == null) return;
            if (bPage) { tDrawPage(); return; }
            tSetSystem(STAFF_BOTTOM_Y, STAFF_SPACE, STEM_TOP_Y, STEM_BOTTOM_Y);
            this.nHeadX = PLAYHEAD_X;
            nActiveBaseY = STAFF_BOTTOM_Y;
            nActiveStep = STAFF_STEP;
            nActiveJudgeX = JUDGE_X;
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, 1280, BAND_BOTTOM_Y - BAND_TOP_Y, 200);
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, LABEL_GUTTER_W, BAND_BOTTOM_Y - BAND_TOP_Y, 190);
            tDrawLaneFlashes();
            // the lines start after the legend gutter, or they strike through the labels that sit on a line
            for (int i = 0; i < 5; i++)
                tDrawCell(SHAPE_SOLID, C_WHITE, LABEL_GUTTER_W, STAFF_BOTTOM_Y - i * STAFF_SPACE - LINE_H / 2,
                          1280 - LABEL_GUTTER_W, LINE_H, 235);
            tDrawCell(SHAPE_SOLID, C_PLAYHEAD, PLAYHEAD_X - 2, BAND_TOP_Y + 8, 4, BAND_BOTTOM_Y - BAND_TOP_Y - 16, 255);
        }

        #region [ page view: two fixed staves, the playhead sweeping them in turn ]

        /// <summary>
        /// Work out the whole page layout for this song: one pixels-per-millisecond scale, and the
        /// bars packed into lines. The scale is constant for the song, so the playhead moves at a
        /// steady speed and it is the bars that come out narrow or wide when the tempo or the bar
        /// length changes - which is what a live recording with a per-bar tempo map needs.
        /// </summary>
        private void tBuildPageLayout()
        {
            List<int> listBarMs = new List<int>(256);
            List<int> listBarNo = new List<int>(256);
            List<int> listBeatMs = new List<int>(1024);
            int nLastChipMs = 0;
            if (CDTXMania.DTX != null && CDTXMania.DTX.listChip != null)
            {
                foreach (CChip chip in CDTXMania.DTX.listChip)
                {
                    if (chip.nPlaybackTimeMs > nLastChipMs) nLastChipMs = chip.nPlaybackTimeMs;
                    if (chip.nChannelNumber == EChannel.BeatLine) { listBeatMs.Add(chip.nPlaybackTimeMs); continue; }
                    if (chip.nChannelNumber != EChannel.BarLine) continue;
                    if (listBarNo.Count > 0 && listBarNo[listBarNo.Count - 1] == chip.nPlaybackPosition / 384) continue;
                    listBarMs.Add(chip.nPlaybackTimeMs);
                    listBarNo.Add(chip.nPlaybackPosition / 384);
                }
            }
            this.nBarMs = listBarMs.ToArray();
            this.nBarNumber = listBarNo.ToArray();
            this.nBeatMs = listBeatMs.ToArray();
            Array.Sort(this.nBeatMs);
            this.nPageLastLine = -1;
            for (int k = 0; k < 2; k++) { this.nSystemLine[k] = -1; this.nSystemNextLine[k] = -1; }
            this.nBarSearchHint = 0;
            this.nLineSearchHint = 0;

            int nBarsWanted = CDTXMania.ConfigIni.nDrumsNotationBarsPerLine;
            if (nBarsWanted < 1 || nBarsWanted > 8) nBarsWanted = 4;

            // the last bar runs to the last chip, so it has a width like any other
            this.nSongEndMs = (this.nBarMs.Length > 0) ? Math.Max(nLastChipMs + 1, this.nBarMs[this.nBarMs.Length - 1] + 1) : 1;

            // bar 0 is the DTX lead-in and is often an odd length, so it does not get a say in how
            // many bars a line should hold
            double dbAverageBarMs = 2000.0;     // a 4/4 bar at 120 BPM, if the chart has no bar lines
            int nFirstCounted = (this.nBarMs.Length > 2 && this.nBarNumber[0] == 0) ? 1 : 0;
            if (this.nBarMs.Length - nFirstCounted >= 2)
                dbAverageBarMs = (double)(this.nBarMs[this.nBarMs.Length - 1] - this.nBarMs[nFirstCounted])
                                 / (this.nBarMs.Length - 1 - nFirstCounted);
            if (dbAverageBarMs < 100.0) dbAverageBarMs = 100.0;

            int nBarRoom = 1280 - LABEL_GUTTER_W - PAGE_LEFT_PAD - PAGE_RIGHT_MARGIN;   // room for the bars
            this.dbPxPerMs = nBarRoom / (nBarsWanted * dbAverageBarMs);

            // greedy packing: whole bars, as many as fit; one that cannot fit at all gets its own line
            List<int> listLineMs = new List<int>(64);
            List<int> listLineBar = new List<int>(64);
            for (int i = 0; i < this.nBarMs.Length; )
            {
                listLineMs.Add(this.nBarMs[i]);
                listLineBar.Add(i);
                // A bar joins the line only if its END lands inside the room, so a bar is never
                // drawn past the right edge and lines simply come out uneven. The one exception is
                // a bar longer than a whole line: it gets a line to itself and is clipped.
                int j = i;
                while (j < this.nBarMs.Length && (nBarEndMs(j) - this.nBarMs[i]) * this.dbPxPerMs <= nBarRoom)
                    j++;
                if (j == i) j = i + 1;
                i = j;
            }
            this.nLineMs = listLineMs.ToArray();
            this.nLineFirstBar = listLineBar.ToArray();

            // every bar must belong to exactly one line: a gap here is a bar the player never sees
            for (int k = 0; k + 1 < this.nLineFirstBar.Length; k++)
            {
                int nNextExpected = this.nLineFirstBar[k + 1];
                if (nNextExpected <= this.nLineFirstBar[k])
                    System.Diagnostics.Trace.TraceWarning("Notation page: line {0} does not advance (bar {1}).", k, nNextExpected);
            }
            if (this.nLineFirstBar.Length > 0 && this.nLineFirstBar[0] != 0)
                System.Diagnostics.Trace.TraceWarning("Notation page: the first line starts at bar index {0}, not 0.", this.nLineFirstBar[0]);

            // the layout in the log, so a photo of a wrong page can be checked against the numbers
            System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
            sb.AppendFormat("Notation page: {0} bars, {1} beat lines, {2} lines, {3:F4} px/ms, song end {4} ms; bars:",
                            this.nBarMs.Length, this.nBeatMs.Length, this.nLineMs.Length, this.dbPxPerMs, this.nSongEndMs);
            for (int b = 0; b < this.nBarMs.Length && b < 40; b++)
                sb.AppendFormat(" {0}@{1}", this.nBarNumber[b], this.nBarMs[b]);
            sb.Append("; lines:");
            for (int k = 0; k < this.nLineMs.Length; k++)
                sb.AppendFormat(" {0}:bar{1}@{2}", k, this.nBarNumber[this.nLineFirstBar[k]], this.nLineMs[k]);
            System.Diagnostics.Trace.TraceInformation(sb.ToString());
        }

        /// <summary>When a line's last bar ends: the next line's first bar line, or the end of the song.</summary>
        private long nLineEndMs(int nLineIndex)
        {
            if (this.nLineMs == null || nLineIndex < 0 || nLineIndex >= this.nLineMs.Length) return 0;
            return (nLineIndex + 1 < this.nLineMs.Length) ? this.nLineMs[nLineIndex + 1] : this.nSongEndMs;
        }

        /// <summary>
        /// How far the staff of a line reaches, in pixels from the gutter: to its closing bar line,
        /// so a line that holds fewer bars ends early instead of running on as empty staff. 0 when
        /// there is no such line (the staff after the last line of the song).
        /// </summary>
        private int nLineStaffWidth(int nLineIndex)
        {
            if (this.nLineMs == null || nLineIndex < 0 || nLineIndex >= this.nLineMs.Length) return 0;
            int x = nPageX(nLineEndMs(nLineIndex), nLineOriginMs(nLineIndex)) + 1;   // the bar line is 2 px wide, at x-1
            if (x > 1280 - PAGE_RIGHT_MARGIN) x = 1280 - PAGE_RIGHT_MARGIN;
            return Math.Max(0, x - LABEL_GUTTER_W);
        }

        /// <summary>The five staff lines of one system between two x offsets from the gutter.</summary>
        private void tDrawPageStaffLines(int nBottom, int x0, int x1, int nAlpha)
        {
            if (x1 <= x0 || nAlpha <= 0) return;
            for (int i = 0; i < 5; i++)
                tDrawCell(SHAPE_SOLID, C_WHITE, LABEL_GUTTER_W + x0, nBottom - i * PAGE_SPACE - nSc(LINE_H) / 2,
                          x1 - x0, nSc(LINE_H), nAlpha);
        }

        /// <summary>When a bar ends: the next bar line, or the end of the song for the last one.</summary>
        private long nBarEndMs(int nBar)
        {
            if (this.nBarMs == null || nBar < 0) return 0;
            return (nBar + 1 < this.nBarMs.Length) ? this.nBarMs[nBar + 1] : this.nSongEndMs;
        }

        /// <summary>
        /// Which line the playhead is on: a line owns the time from its first bar line up to the
        /// next line's first bar line, so the playhead stays on a line right up to its closing bar
        /// line and then jumps to the start of the next one. Nothing overlaps.
        /// </summary>
        private int nLineAt(long nNowMs)
        {
            if (this.nLineMs == null || this.nLineMs.Length == 0) return 0;
            int n = this.nLineSearchHint;
            if (n < 0 || n >= this.nLineMs.Length) n = 0;
            while (n > 0 && nNowMs < this.nLineMs[n]) n--;
            while (n < this.nLineMs.Length - 1 && nNowMs >= this.nLineMs[n + 1]) n++;
            this.nLineSearchHint = n;
            return n;
        }

        /// <summary>Where a line begins on the clock: its first bar line.</summary>
        private long nLineOriginMs(int nLineIndex)
        {
            if (this.nLineMs == null || this.nLineMs.Length == 0) return 0;
            if (nLineIndex < 0) nLineIndex = 0;
            if (nLineIndex >= this.nLineMs.Length) nLineIndex = this.nLineMs.Length - 1;
            return this.nLineMs[nLineIndex];
        }

        /// <summary>
        /// x of a moment in time on the line that starts at nOriginMs, strictly linear: where the
        /// bar lines go, and where the staff ends.
        /// </summary>
        private int nPageX(double dbMs, long nOriginMs)
        {
            return LABEL_GUTTER_W + PAGE_LEFT_PAD + (int)((dbMs - nOriginMs) * this.dbPxPerMs);
        }

        /// <summary>
        /// x of a note (or the playhead) on that line: as engraved, not as timed. The bar the moment
        /// falls in is mapped into the room between its two bar lines less a gap after the opening
        /// one and a smaller one before the closing one, so beat 1 stands clear of the bar line and
        /// nothing is ever drawn on or beyond a bar's lines. The playhead follows the same map, so
        /// it skips the gap at every bar line instead of drifting from the notes.
        /// </summary>
        private int nPageNoteX(double dbMs, long nOriginMs)
        {
            if (this.nBarMs == null || this.nBarMs.Length == 0) return nPageX(dbMs, nOriginMs);
            // the bar this moment belongs to: the last bar line at or before it
            int b = this.nBarSearchHint;
            if (b < 0 || b >= this.nBarMs.Length) b = 0;
            while (b > 0 && dbMs < this.nBarMs[b]) b--;
            while (b + 1 < this.nBarMs.Length && dbMs >= this.nBarMs[b + 1]) b++;
            this.nBarSearchHint = b;
            long nT0 = this.nBarMs[b];
            long nT1 = nBarEndMs(b);
            if (dbMs < nT0 || nT1 <= nT0) return nPageX(dbMs, nOriginMs);      // before the first bar line
            int x0 = nPageX(nT0, nOriginMs);
            int x1 = nPageX(nT1, nOriginMs);
            int nRoom = x1 - x0;
            int nPadL = Math.Min(PAGE_BAR_PAD_L, nRoom / 4);                    // a very narrow bar keeps its
            int nPadR = Math.Min(PAGE_BAR_PAD_R, nRoom / 8);                    // proportions rather than its gaps
            return x0 + nPadL + (int)((dbMs - nT0) * (nRoom - nPadL - nPadR) / (double)(nT1 - nT0));
        }

        /// <summary>
        /// Give a staff its next line, timed against the line the playhead is on now (nLine): the
        /// old line stays for the first half of that line's first bar, fades out over half a bar,
        /// then the new one fades in over half a bar - about a bar of change in all - and it is
        /// all over PAGE_SWAP_LEAD_MS before the new line has to be played. A line too short for
        /// that squeezes the schedule; one shorter than the lead swaps at once.
        /// </summary>
        private void tScheduleSwap(int k, int nNextLine, int nLine)
        {
            long nT0 = this.nLineMs[nLine];
            long nBar = nBarEndMs(this.nLineFirstBar[nLine]) - nT0;
            if (nBar <= 0) nBar = 1000;
            long nDeadline = nLineEndMs(nLine) - PAGE_SWAP_LEAD_MS;
            long nStart = nT0 + nBar / 2;
            long nEnd = nStart + nBar;
            if (nEnd > nDeadline)
            {
                nEnd = nDeadline;
                nStart = Math.Max(nT0, nEnd - nBar);
            }
            if (nEnd <= nStart)
            {
                this.nSystemLine[k] = nNextLine;        // no room to fade at all
                this.nSystemNextLine[k] = -1;
                return;
            }
            this.nSystemNextLine[k] = nNextLine;
            this.nSwapStartMs[k] = nStart;
            this.nSwapMidMs[k] = (nStart + nEnd) / 2;
            this.nSwapEndMs[k] = nEnd;
        }

        /// <summary>The whole page: band, two systems, their bars and notes, the legend and the playhead.</summary>
        private void tDrawPage()
        {
            if (this.nLineMs == null) tBuildPageLayout();

            long nNow = CSoundManager.rcPerformanceTimer.nCurrentTime;
            int nLine = nLineAt(nNow);
            this.nHeadX = nPageNoteX(nNow, nLineOriginMs(nLine));

            // The playhead alternates strictly: even lines play on the upper staff, odd lines on
            // the lower one, so it always goes upper, lower, upper, lower. The staff it is not on
            // holds the line that comes next; when the playhead moves on, the staff it just left
            // keeps its line for a moment, then fades it out and the line after next in, all before
            // that line is due. Nothing else ever changes, and no bar is drawn on more than one staff.
            int nPlaying = nLine & 1;
            int nIdle = 1 - nPlaying;
            if (this.nSystemLine[nPlaying] != nLine || this.nSystemNextLine[nPlaying] >= 0)
            {
                // the staff being played must show its line, complete, now: this is a snap (song
                // start or a skip), never a fade
                System.Diagnostics.Trace.TraceInformation("Notation page: t={0} line {1}->{2}: staff {3} snaps {4}->{5}{6}",
                    nNow, this.nPageLastLine, nLine, nPlaying, this.nSystemLine[nPlaying], nLine,
                    (this.nPageLastLine >= 0 && nLine == this.nPageLastLine + 1 && this.nSystemLine[nPlaying] != nLine)
                        ? " (UNEXPECTED: the staff being entered did not hold its line)" : "");
                this.nSystemLine[nPlaying] = nLine;
                this.nSystemNextLine[nPlaying] = -1;
            }
            if (this.nSystemLine[nIdle] != nLine + 1 && this.nSystemNextLine[nIdle] != nLine + 1)
            {
                if (this.nPageLastLine >= 0 && nLine == this.nPageLastLine + 1 && this.nSystemLine[nIdle] == nLine - 1)
                {
                    tScheduleSwap(nIdle, nLine + 1, nLine);      // the playhead just left it: hold, then fade
                    System.Diagnostics.Trace.TraceInformation("Notation page: t={0} line {1}->{2}: staff {3} {4}->{5} scheduled, out {6}-{7}, in {7}-{8}",
                        nNow, this.nPageLastLine, nLine, nIdle, this.nSystemLine[nIdle], nLine + 1,
                        this.nSwapStartMs[nIdle], this.nSwapMidMs[nIdle], this.nSwapEndMs[nIdle]);
                }
                else
                {
                    System.Diagnostics.Trace.TraceInformation("Notation page: t={0} line {1}->{2}: staff {3} snaps {4}->{5}",
                        nNow, this.nPageLastLine, nLine, nIdle, this.nSystemLine[nIdle], nLine + 1);
                    this.nSystemLine[nIdle] = nLine + 1;         // song start, or a skip
                    this.nSystemNextLine[nIdle] = -1;
                }
            }
            this.nPageLastLine = nLine;

            int nPlayBottom = PAGE_BOTTOM_0 + nPlaying * PAGE_SYSTEM_DY;
            nActiveBaseY = nPlayBottom;
            nActiveStep = PAGE_SPACE / 2;
            nActiveJudgeX = this.nHeadX;

            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, 1280, BAND_BOTTOM_Y - BAND_TOP_Y, 200);
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, LABEL_GUTTER_W, BAND_BOTTOM_Y - BAND_TOP_Y, 190);

            for (int k = 0; k < 2; k++)
            {
                int nBottom = PAGE_BOTTOM_0 + k * PAGE_SYSTEM_DY;
                tSetSystem(nBottom, PAGE_SPACE, nBottom + PAGE_STEM_TOP_DY, nBottom + PAGE_STEM_BOTTOM_DY);
                if (k == nPlaying) tDrawLaneFlashes();      // the flash belongs to the line being played

                // which line this staff shows right now, and how strongly: a pending swap holds the
                // old line, fades it out to nothing at the midpoint, then fades the new one in
                int nShow = this.nSystemLine[k];
                int nAlpha = 255;
                if (this.nSystemNextLine[k] >= 0)
                {
                    if (nNow >= this.nSwapEndMs[k])
                    {
                        this.nSystemLine[k] = this.nSystemNextLine[k];
                        this.nSystemNextLine[k] = -1;
                        nShow = this.nSystemLine[k];
                    }
                    else if (nNow >= this.nSwapMidMs[k])
                    {
                        nShow = this.nSystemNextLine[k];
                        nAlpha = (int)(255L * (nNow - this.nSwapMidMs[k]) / Math.Max(1L, this.nSwapEndMs[k] - this.nSwapMidMs[k]));
                    }
                    else if (nNow >= this.nSwapStartMs[k])
                    {
                        nAlpha = (int)(255L * (this.nSwapMidMs[k] - nNow) / Math.Max(1L, this.nSwapMidMs[k] - this.nSwapStartMs[k]));
                    }
                }
                if (nAlpha > 255) nAlpha = 255;
                if (nAlpha < 0) nAlpha = 0;

                // The staff runs from the gutter to the line's closing bar line only, so where a
                // line holds fewer bars the staff stops there and the rest of the row is empty:
                // what is staff is a bar. It fades with the line it belongs to.
                // the staff begins at the line's first bar line, not at the gutter: a stub of staff
                // before it reads as a sliver of the previous bar
                int nWidth = nLineStaffWidth(nShow);
                tDrawPageStaffLines(nBottom, PAGE_LEFT_PAD, nWidth, 235 * nAlpha / 255);
                tDrawPageLine(nShow, nAlpha, true);
                if (nWidth > 0 && nAlpha > 0) tDrawLaneLabels();   // no legend for a staff that is not there
            }

            // the playhead sweeps whichever staff is being played
            tDrawCell(SHAPE_SOLID, C_PLAYHEAD, this.nHeadX - 2, nPlayBottom + PAGE_STEM_TOP_DY - 12, 4,
                      PAGE_STEM_BOTTOM_DY - PAGE_STEM_TOP_DY + 12, 255);
        }

        /// <summary>One staff line of the page: its bars, beat ticks, numbers and notes.</summary>
        private void tDrawPageLine(int nLineIndex, int nAlphaScale, bool bDrawGrid)
        {
            if (nLineIndex < 0 || nAlphaScale <= 0) return;
            if (this.nLineMs == null || nLineIndex >= this.nLineMs.Length) return;

            long nOrigin = nLineOriginMs(nLineIndex);
            // from this line's first bar line up to (not including) the next line's: every note of
            // the song is drawn on exactly one line
            long nEndMs = nLineEndMs(nLineIndex);
            int nStaffH = 4 * this.nSpace + 1;
            int nStaffTop = this.nBaseY - 4 * this.nSpace;
            int nRight = 1280 - PAGE_RIGHT_MARGIN;

            #region [ bar lines, their numbers and the beat ticks ]
            int nFirst = this.nLineFirstBar[nLineIndex];
            int nLast = (nLineIndex + 1 < this.nLineFirstBar.Length) ? this.nLineFirstBar[nLineIndex + 1] : this.nBarMs.Length;
            for (int b = nFirst; b <= nLast && b < this.nBarMs.Length; b++)
            {
                int x = nPageX(this.nBarMs[b], nOrigin);
                if (x > nRight) break;
                if (bDrawGrid) tDrawCell(SHAPE_SOLID, C_WHITE, x - 1, nStaffTop, 2, nStaffH, 230);
                if (b == nLast) break;                      // the next line's first bar closes this one
                // The loader puts an empty bar in front of the chart (CDTX: n小節番号++), so internal
                // bar k is bar k-1 of the DTX file. Numbers are printed as the file counts them, so
                // they match the chart and the sheet music it came from; the loader's empty bar and
                // the file's bar 0 (the lead-in) get no number. The console font has no alpha, so
                // the numbers change over at the half way point of a crossfade.
                if (nAlphaScale >= 128 && this.nBarNumber[b] >= 2)
                    CDTXMania.actDisplayString.tPrint(x + 4, this.nBaseY + PAGE_BAR_NUMBER_DY,
                                                      CCharacterConsole.EFontType.White, (this.nBarNumber[b] - 1).ToString());
            }
            // Beat ticks come from the engine's own beat-line chips. Chip positions are always 384
            // per bar whatever the bar's length, so they cannot tell a 1-beat pickup from a 4/4 bar;
            // the loader's beat lines are placed with the bar length applied, so a pickup gets none
            // and a 3/4 bar gets two.
            if (bDrawGrid && this.nBeatMs != null && this.nBeatMs.Length > 0)
            {
                int i0 = Array.BinarySearch(this.nBeatMs, (int)this.nLineMs[nLineIndex]);
                if (i0 < 0) i0 = ~i0;
                for (int i = i0; i < this.nBeatMs.Length && this.nBeatMs[i] < nEndMs; i++)
                {
                    int xq = nPageNoteX(this.nBeatMs[i], nOrigin);
                    if (xq > nRight) break;
                    tDrawCell(SHAPE_SOLID, C_WHITE, xq, nStaffTop, 1, nStaffH, 70);
                }
            }
            #endregion

            // the page is laid out on the clock, so the chips come straight from the chart, not from
            // the engine's time-based feed
            this.listNotes.Clear();
            List<CChip> listChip = (CDTXMania.DTX != null) ? CDTXMania.DTX.listChip : null;
            if (listChip == null) return;
            for (int i = 0; i < listChip.Count; i++)
            {
                CChip chip = listChip[i];
                if (chip.nPlaybackTimeMs >= nEndMs) break;              // listChip is in time order
                if (chip.nPlaybackTimeMs < nOrigin) continue;
                STNote note;
                if (!mapNotes.TryGetValue(chip.nChannelNumber, out note)) continue;
                if (bAlreadyOnThisBeat(chip.nPlaybackPosition, note.nPos)) continue;

                int x = nPageNoteX(chip.nPlaybackTimeMs, nOrigin);
                if (x > nRight) break;

                STPendingNote pending;
                pending.x = x;
                pending.nPlaybackPosition = chip.nPlaybackPosition;
                pending.nAlpha = (chip.bHit ? PAGE_HIT_ALPHA : 255) * nAlphaScale / 255;
                pending.note = note;
                this.listNotes.Add(pending);
            }
            tLayoutNotes();
            this.listNotes.Clear();
        }

        #endregion

        /// <summary>
        /// The stage calls this from the same place the vertical view starts its lane flush, so the
        /// lane the player just hit lights up on the staff. Keyed by the chip's channel.
        /// </summary>
        public void tLaneHit(EChannel nChannel)
        {
            STNote note;
            if (mapNotes.TryGetValue(nChannel, out note))
                tLaneHit(note.nPos, note.nColour);
        }

        /// <summary>Same, for a hit that did not land on a chip: we only know the lane (ELane).</summary>
        public void tLaneHitByLane(int nLane)
        {
            if (nLane < 0 || nLane >= stLaneLabels.Length) return;
            tLaneHit(stLaneLabels[nLane].nPos, stLaneLabels[nLane].nColour);
        }

        private void tLaneHit(int nPos, int nColour)
        {
            int i = nPos - POS_MIN;
            if (i < 0 || i >= this.ctLaneFlash.Length) return;
            this.nLaneFlashColour[i] = nColour;
            if (this.ctLaneFlash[i] == null)
                this.ctLaneFlash[i] = new CCounter(0, LANE_FLASH_MS, 1, CDTXMania.Timer);
            else
                this.ctLaneFlash[i].tStart(0, LANE_FLASH_MS, 1, CDTXMania.Timer);
        }

        private void tDrawLaneFlashes()
        {
            for (int i = 0; i < this.ctLaneFlash.Length; i++)
            {
                CCounter ct = this.ctLaneFlash[i];
                if (ct == null || ct.b停止中) continue;
                ct.tUpdate();
                if (ct.bReachedEndValue) { ct.tStop(); continue; }

                double dbFade = 1.0 - (double)ct.nCurrentValue / LANE_FLASH_MS;
                int y = nBaseY - (i + POS_MIN) * nStep;
                int nColour = this.nLaneFlashColour[i];
                int nGlowW = nSc(LANE_GLOW_W);
                // scroll mode lights the whole played region; on a page that would be most of the
                // line, so it is a short trail behind the playhead instead
                int nFlashLeft = bPage ? Math.Max(LABEL_GUTTER_W + PAGE_LEFT_PAD, nHeadX - PAGE_FLASH_W) : LABEL_GUTTER_W;
                tDrawCell(SHAPE_SOLID, nColour, nFlashLeft, y - nStep,
                          nHeadX - nFlashLeft, nSpace, (int)(LANE_FLASH_ALPHA * dbFade));
                tDrawCell(SHAPE_HEAD, nColour, nHeadX - nGlowW / 2, y - nGlowW / 2,
                          nGlowW, nGlowW, (int)(LANE_GLOW_ALPHA * dbFade));
            }
        }

        /// <summary>The lane legend down the left edge. Drawn last so notes never sit on top of it.</summary>
        private void tDrawLaneLabels()
        {
            // built lazily as well as at activation, so switching NotationView on between songs
            // still gets a legend even if the actor was not re-activated in between
            if (this.txLaneLabel == null) tCreateLaneLabels();
            if (this.txLaneLabel == null) return;
            for (int i = 0; i < this.txLaneLabel.Length; i++)
            {
                CTexture txLabel = this.txLaneLabel[i];
                if (txLabel == null) continue;
                int y = nBaseY - stLaneLabels[i].nPos * nStep;
                // the labels are rendered once at scroll size; page mode packs the lanes closer, so
                // they are drawn scaled to that system's spacing or consecutive rows would collide
                float fLabel = this.nSpace / (float)STAFF_SPACE;
                int nLabelW = (int)(txLabel.szImageSize.Width * fLabel);
                int nLabelH = (int)(txLabel.szImageSize.Height * fLabel);
                txLabel.nTransparency = 255;
                txLabel.vcScaleRatio = new Vector3(fLabel, fLabel, 1f);
                int nLabelX = LABEL_GUTTER_W - LABEL_RIGHT_PAD - nLabelW;
                if (nLabelX < 2) nLabelX = 2;
                txLabel.tDraw2D(CDTXMania.app.Device, nLabelX, y - nLabelH / 2);
                txLabel.vcScaleRatio = new Vector3(1f, 1f, 1f);
            }
        }

        /// <summary>Remember one chip; the heads and stems are drawn together in tDrawNotes().</summary>
        public void tDrawChip(CChip pChip)
        {
            if (this.tx == null || bPage) return;
            STNote note;
            if (!mapNotes.TryGetValue(pChip.nChannelNumber, out note)) return;
            int x = nX(pChip.nDistanceFromBar.Drums);
            if (x < LABEL_GUTTER_W || x > 1280 + CELL) return;   // played notes slide away behind the legend

            if (bAlreadyOnThisBeat(pChip.nPlaybackPosition, note.nPos)) return;

            STPendingNote pending;
            pending.x = x;
            pending.nPlaybackPosition = pChip.nPlaybackPosition;
            pending.nAlpha = pChip.bHit ? 70 : 255;
            pending.note = note;
            this.listNotes.Add(pending);
        }

        /// <summary>
        /// Lay out everything that belongs to a single instant: ledger lines, one shared up-stem for
        /// the hands, one shared down-stem for the feet, the beams, then the heads on top.
        /// Call once, right after the chip loop.
        /// </summary>
        public void tDrawNotes()
        {
            if (this.tx == null || bPage) return;   // page mode draws everything from tDrawStaff()
            tLayoutNotes();
            tDrawLaneLabels();
        }

        /// <summary>
        /// Lay out whatever is in listNotes on the current system: ledger lines, one shared up-stem
        /// for the hands, one shared down-stem for the feet, the beams, then the heads on top.
        /// </summary>
        private void tLayoutNotes()
        {
            if (this.listNotes.Count == 0) return;

            int nFrom = 0;
            int nBeamStartX = -1, nBeamBeat = -1, nBeamPos = -1, nBeamAlpha = 255;

            while (nFrom < this.listNotes.Count)
            {
                // chips arrive in playback order, so one instant is a run of equal x
                int x = this.listNotes[nFrom].x;
                int nTo = nFrom;
                while (nTo + 1 < this.listNotes.Count && Math.Abs(this.listNotes[nTo + 1].x - x) <= 2)
                    nTo++;

                int nPlaybackPosition = this.listNotes[nFrom].nPlaybackPosition;
                int nUpTop = int.MaxValue, nUpBottom = int.MinValue, nUpAlpha = 0, nUpHalf = 0, nUpEndShape = SHAPE_HEAD;
                int nDownTop = int.MaxValue, nDownBottom = int.MinValue, nDownAlpha = 0, nDownHalf = 0, nDownEndShape = SHAPE_HEAD;

                for (int i = nFrom; i <= nTo; i++)
                {
                    STNote note = this.listNotes[i].note;
                    int y = nBaseY - note.nPos * nStep;
                    if (note.bStemUp)
                    {
                        if (y < nUpTop) nUpTop = y;
                        if (y > nUpBottom) { nUpBottom = y; nUpEndShape = note.nShape; }   // the head the stem ends on
                        if (this.listNotes[i].nAlpha > nUpAlpha) nUpAlpha = this.listNotes[i].nAlpha;
                        if (nHeadHalfWidth(note.nShape) > nUpHalf) nUpHalf = nHeadHalfWidth(note.nShape);
                    }
                    else
                    {
                        if (y < nDownTop) { nDownTop = y; nDownEndShape = note.nShape; }
                        if (y > nDownBottom) nDownBottom = y;
                        if (this.listNotes[i].nAlpha > nDownAlpha) nDownAlpha = this.listNotes[i].nAlpha;
                        if (nHeadHalfWidth(note.nShape) > nDownHalf) nDownHalf = nHeadHalfWidth(note.nShape);
                    }

                    // ledger lines above the staff (first for the crashes, second for the left crash)
                    for (int nLedger = 10; nLedger <= note.nPos; nLedger += 2)
                    {
                        int nLedgerY = nBaseY - nLedger * nStep;
                        int nLedgerX = x - nSc(LEDGER_W) / 2;
                        int nLedgerW = nSc(LEDGER_W);
                        if (nLedgerX < LABEL_GUTTER_W) { nLedgerW -= LABEL_GUTTER_W - nLedgerX; nLedgerX = LABEL_GUTTER_W; }
                        tDrawCell(SHAPE_SOLID, C_WHITE, nLedgerX, nLedgerY - nSc(LEDGER_H) / 2,
                                  nLedgerW, nSc(LEDGER_H), this.listNotes[i].nAlpha);
                    }
                }

                // One stem up for the hands, one stem down for the feet. A round head is met at its
                // right edge at the head's own height; an x head has no ink out there, so its stem
                // starts at the tip of the upper-right arm (+arm, -arm) and visibly continues it.
                bool bUpEndIsX = (nUpEndShape == SHAPE_X || nUpEndShape == SHAPE_BOLD_X);
                int nUpArm = bUpEndIsX ? nHeadHalfWidth(nUpEndShape) : nUpHalf;
                int nStemW = nSc(STEM_W);
                int nStemX = x + nUpArm - (bUpEndIsX ? nStemW / 2 : nSc(STEM_BITE));
                if (bStems && nUpBottom != int.MinValue)
                {
                    // The beam line is fixed, and a crash on its ledger lines sits above it, so the
                    // stem runs from the head to the line whichever side of it the head is on.
                    int nStemEnd = bUpEndIsX ? nUpBottom - nUpArm + nStemW : nUpBottom;
                    int nStemTop = Math.Min(nStemTopY, nStemEnd);
                    int nStemBot = Math.Max(nStemTopY, nStemEnd);
                    tDrawCell(SHAPE_SOLID, C_WHITE, nStemX, nStemTop, nStemW, nStemBot - nStemTop, nUpAlpha);
                }
                if (bStems && nDownTop != int.MaxValue)
                {
                    bool bDownEndIsX = (nDownEndShape == SHAPE_X || nDownEndShape == SHAPE_BOLD_X);
                    int nDownArm = bDownEndIsX ? nHeadHalfWidth(nDownEndShape) : nDownHalf;
                    int nDownStemX = x - nDownArm + (bDownEndIsX ? nStemW / 2 : nSc(STEM_BITE)) - nStemW;
                    int nDownStemTop = bDownEndIsX ? nDownTop + nDownArm - nStemW : nDownTop;
                    tDrawCell(SHAPE_SOLID, C_WHITE, nDownStemX, nDownStemTop, nStemW, nStemBotY - nDownStemTop, nDownAlpha);
                }

                #region [ beams: join the up-stems of one beat (384 ticks per bar, 96 per beat) ]
                if (bStems && nUpBottom != int.MinValue)
                {
                    int nBeat = nPlaybackPosition / 96;
                    if (nBeat == nBeamBeat && nBeamStartX >= 0)
                    {
                        // still inside the same beat: extend the beam to here
                        int nBeams = tBeamCount(nPlaybackPosition - nBeamPos);
                        for (int i = 0; i < nBeams; i++)
                            tDrawCell(SHAPE_SOLID, C_WHITE, nBeamStartX, nStemTopY + i * nSc(BEAM_GAP),
                                      nStemX - nBeamStartX + nStemW, nSc(BEAM_H), Math.Min(nBeamAlpha, nUpAlpha));
                    }
                    else
                    {
                        nBeamBeat = nBeat;
                    }
                    nBeamStartX = nStemX;
                    nBeamPos = nPlaybackPosition;
                    nBeamAlpha = nUpAlpha;
                }
                #endregion

                nFrom = nTo + 1;
            }

            // heads last, so nothing is drawn across them
            for (int i = 0; i < this.listNotes.Count; i++)
            {
                STNote note = this.listNotes[i].note;
                int y = nBaseY - note.nPos * nStep;
                int w = nHeadCell(note.nShape);
                tDrawCell(note.nShape, note.nColour, this.listNotes[i].x - w / 2, y - w / 2, w, w, this.listNotes[i].nAlpha);
            }
        }

        /// <summary>Cell size each notehead shape is stretched to.</summary>
        private int nHeadCell(int nShape)
        {
            return nSc(nHeadCellBase(nShape));
        }

        private static int nHeadCellBase(int nShape)
        {
            if (nShape == SHAPE_X) return XHEAD_W;
            if (nShape == SHAPE_CIRCLE_X) return CIRCLEX_W;
            if (nShape == SHAPE_BOLD_X) return BOLDX_W;
            if (nShape == SHAPE_DIAMOND) return DIAMOND_W;
            return HEAD_W;
        }

        /// <summary>
        /// Where a stem meets this head, as a distance from the head centre. The sprite cells do not
        /// fill their 64 px box: the ellipse and diamond reach 25.2/32 of the half cell, the circled
        /// x's ring 28.2/32, and an x head's arm *tips* sit at 25.2/32 diagonally (the x has no ink
        /// at the head's own height, which is why its stem attaches at the arm tip instead).
        /// </summary>
        private int nHeadHalfWidth(int nShape)
        {
            int nCell = nHeadCell(nShape);
            switch (nShape)
            {
                case SHAPE_X:
                case SHAPE_BOLD_X:   return nCell * 252 / 640;   // 40 -> 15, 50 -> 19  (arm tip, x and y)
                case SHAPE_CIRCLE_X: return nCell * 282 / 640;   // 42 -> 18  (ring)
                case SHAPE_DIAMOND:  return nCell * 244 / 640;   // 48 -> 18  (right vertex)
                default:             return nCell * 252 / 640;   // 48 -> 18  (ellipse)
            }
        }

        /// <summary>
        /// Is this staff position already taken at this instant? Both crashes live on one row, so a
        /// chart that has them together would otherwise stack two heads in the same place.
        /// </summary>
        private bool bAlreadyOnThisBeat(int nPlaybackPosition, int nPos)
        {
            for (int i = this.listNotes.Count - 1; i >= 0; i--)
            {
                if (this.listNotes[i].nPlaybackPosition != nPlaybackPosition) return false;
                if (this.listNotes[i].note.nPos == nPos) return true;
            }
            return false;
        }

        /// <summary>Beams per stem for a gap in playback ticks: 48 = eighths, 24 = sixteenths, 12 = 32nds.</summary>
        private static int tBeamCount(int nTickGap)
        {
            if (nTickGap <= 0 || nTickGap > 48) return 0;   // quarter notes and slower are not beamed
            if (nTickGap > 24) return 1;
            if (nTickGap > 12) return 2;
            return 3;
        }

        public void tDrawBarLine(CChip pChip, int nBarNumber)
        {
            if (this.tx == null || bPage) return;
            int x = nX(pChip.nDistanceFromBar.Drums) - BAR_LINE_LEAD;
            if (x < LABEL_GUTTER_W || x > 1284) return;     // never into the legend gutter
            tDrawCell(SHAPE_SOLID, C_WHITE, x - 1, STAFF_TOP_Y, 2, 4 * STAFF_SPACE + 1, 230);
            CDTXMania.actDisplayString.tPrint(x + 4, BAR_NUMBER_Y, CCharacterConsole.EFontType.White, nBarNumber.ToString());
        }

        public void tDrawBeatLine(CChip pChip)
        {
            if (this.tx == null || bPage) return;
            int x = nX(pChip.nDistanceFromBar.Drums) - BAR_LINE_LEAD;
            if (x < LABEL_GUTTER_W || x > 1282) return;
            tDrawCell(SHAPE_SOLID, C_WHITE, x, STAFF_TOP_Y, 1, 4 * STAFF_SPACE + 1, 70);
        }

        public void tDrawLoopLine(int nDistanceFromBar, bool bIsEnd)
        {
            if (this.tx == null || bPage) return;
            int x = nX(nDistanceFromBar) - BAR_LINE_LEAD;
            if (x < LABEL_GUTTER_W || x > 1284) return;
            tDrawCell(SHAPE_SOLID, C_PLAYHEAD, x - 1, BAND_TOP_Y + 8, 2, BAND_BOTTOM_Y - BAND_TOP_Y - 16, 200);
            CDTXMania.actDisplayString.tPrint(x + 4, BAND_BOTTOM_Y - 18, CCharacterConsole.EFontType.White, bIsEnd ? "End loop" : "Begin loop");
        }

        /// <summary>Draw one 64x64 sprite cell (shape column, colour row) stretched to w x h at (x, y).</summary>
        private void tDrawCell(int nShape, int nColour, int x, int y, int w, int h, int nAlpha)
        {
            if (w <= 0 || h <= 0) return;
            this.tx.nTransparency = nAlpha;
            this.tx.vcScaleRatio = new Vector3(w / (float)CELL, h / (float)CELL, 1f);
            this.tx.tDraw2D(CDTXMania.app.Device, x, y, new Rectangle(nShape * CELL, nColour * CELL, CELL, CELL));
            this.tx.vcScaleRatio = new Vector3(1f, 1f, 1f);
        }
    }
}
