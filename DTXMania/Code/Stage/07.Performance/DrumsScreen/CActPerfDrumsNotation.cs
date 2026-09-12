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
        public const int PAGE_NOTE_INSET = 14;                      // gap between a bar line and its downbeat

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

        private int[] nBarTimeMs;                   // page view tempo map: bar-line times ...
        private int[] nBarPos;                      // ... and their playback positions
        private int nBarSearchHint;

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
                this.nBarTimeMs = null;         // rebuilt for this song on the first page-mode frame
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
                if (stLaneLabels[i].strText.Length == 0 || !bLaneIsUsed(i)) continue;
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

        /// <summary>Does this chart ever use the lane? (The always-present ones just answer true.)</summary>
        private static bool bLaneIsUsed(int nLane)
        {
            switch ((ELane)nLane)
            {
                case ELane.LC: return CDTXMania.DTX.bチップがある.LeftCymbal;
                case ELane.LP: return CDTXMania.DTX.bチップがある.LP || CDTXMania.DTX.bチップがある.LBD;
                case ELane.RD: return CDTXMania.DTX.bチップがある.Ride;
                case ELane.FT: return CDTXMania.DTX.bチップがある.FT;
                default: return true;
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

        #region [ page view: two fixed staves, a playhead sweeping across the upper one ]

        /// <summary>
        /// Where the song is now, in playback-position units (384 per bar). Interpolated between
        /// the bar-line chips, which carry both a time and a position, so a tempo change simply
        /// changes how fast the playhead crosses that bar.
        /// </summary>
        private double dbPlaybackPosition()
        {
            if (this.nBarTimeMs == null || this.nBarTimeMs.Length < 2) return 0.0;

            long nNow = CSoundManager.rcPerformanceTimer.nCurrentTime;
            int n = this.nBarSearchHint;
            if (n < 0 || n > this.nBarTimeMs.Length - 2) n = 0;
            while (n > 0 && nNow < this.nBarTimeMs[n]) n--;
            while (n < this.nBarTimeMs.Length - 2 && nNow >= this.nBarTimeMs[n + 1]) n++;
            this.nBarSearchHint = n;

            long nSpan = this.nBarTimeMs[n + 1] - this.nBarTimeMs[n];
            if (nSpan <= 0) return this.nBarPos[n];
            double dbFraction = (double)(nNow - this.nBarTimeMs[n]) / nSpan;     // may be <0 or >1 at the ends
            return this.nBarPos[n] + dbFraction * (this.nBarPos[n + 1] - this.nBarPos[n]);
        }

        /// <summary>Bar line chips carry both a time and a 384-per-bar position: that is our tempo map.</summary>
        private void tBuildBarMap()
        {
            List<int> listMs = new List<int>(256);
            List<int> listPos = new List<int>(256);
            if (CDTXMania.DTX != null && CDTXMania.DTX.listChip != null)
            {
                foreach (CChip chip in CDTXMania.DTX.listChip)
                {
                    if (chip.nChannelNumber != EChannel.BarLine) continue;
                    if (listPos.Count > 0 && listPos[listPos.Count - 1] == chip.nPlaybackPosition) continue;
                    listMs.Add(chip.nPlaybackTimeMs);
                    listPos.Add(chip.nPlaybackPosition);
                }
            }
            this.nBarTimeMs = listMs.ToArray();
            this.nBarPos = listPos.ToArray();
            this.nBarSearchHint = 0;
        }

        /// <summary>x of a position inside the current line. Notes are inset so a downbeat does not sit on the bar line.</summary>
        private static int nPageX(double dbTicksIntoLine, int nTicksPerLine)
        {
            int nWidth = 1280 - LABEL_GUTTER_W - PAGE_RIGHT_MARGIN - PAGE_NOTE_INSET;
            return LABEL_GUTTER_W + PAGE_NOTE_INSET + (int)(dbTicksIntoLine * nWidth / nTicksPerLine);
        }

        /// <summary>The whole page: band, two systems, their bars and notes, the legend and the playhead.</summary>
        private void tDrawPage()
        {
            if (this.nBarTimeMs == null) tBuildBarMap();

            int nBars = CDTXMania.ConfigIni.nDrumsNotationBarsPerLine;
            if (nBars < 1 || nBars > 8) nBars = 4;
            int nTicksPerLine = nBars * 384;

            double dbPos = dbPlaybackPosition();
            if (dbPos < 0.0) dbPos = 0.0;
            int nLine = (int)(dbPos / nTicksPerLine);
            this.nHeadX = nPageX(dbPos - (double)nLine * nTicksPerLine, nTicksPerLine);

            nActiveBaseY = PAGE_BOTTOM_0;
            nActiveStep = PAGE_SPACE / 2;
            nActiveJudgeX = this.nHeadX;

            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, 1280, BAND_BOTTOM_Y - BAND_TOP_Y, 200);
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, LABEL_GUTTER_W, BAND_BOTTOM_Y - BAND_TOP_Y, 190);

            for (int k = 0; k < 2; k++)
            {
                int nBottom = PAGE_BOTTOM_0 + k * PAGE_SYSTEM_DY;
                tSetSystem(nBottom, PAGE_SPACE, nBottom + PAGE_STEM_TOP_DY, nBottom + PAGE_STEM_BOTTOM_DY);
                if (k == 0) tDrawLaneFlashes();             // the flash belongs to the line being played
                for (int i = 0; i < 5; i++)
                    tDrawCell(SHAPE_SOLID, C_WHITE, LABEL_GUTTER_W, nBottom - i * PAGE_SPACE - nSc(LINE_H) / 2,
                              1280 - LABEL_GUTTER_W - PAGE_RIGHT_MARGIN, nSc(LINE_H), 235);
                tDrawPageLine(nLine + k, nBars, nTicksPerLine);
                tDrawLaneLabels();
            }

            // the playhead sweeps the upper system only
            tDrawCell(SHAPE_SOLID, C_PLAYHEAD, this.nHeadX - 2, PAGE_BOTTOM_0 + PAGE_STEM_TOP_DY - 12, 4,
                      PAGE_STEM_BOTTOM_DY - PAGE_STEM_TOP_DY + 12, 255);
        }

        /// <summary>One staff line of the page: its bars, beat ticks, bar numbers and notes.</summary>
        private void tDrawPageLine(int nLineIndex, int nBars, int nTicksPerLine)
        {
            if (nLineIndex < 0) return;
            int nStartPos = nLineIndex * nTicksPerLine;
            int nStaffH = 4 * this.nSpace + 1;
            int nStaffTop = this.nBaseY - 4 * this.nSpace;

            for (int b = 0; b <= nBars; b++)
            {
                int x = nPageX(b * 384, nTicksPerLine) - PAGE_NOTE_INSET;
                tDrawCell(SHAPE_SOLID, C_WHITE, x - 1, nStaffTop, 2, nStaffH, 230);
                if (b == nBars) break;
                CDTXMania.actDisplayString.tPrint(x + 4, this.nBaseY + PAGE_BAR_NUMBER_DY,
                                                  CCharacterConsole.EFontType.White, (nLineIndex * nBars + b).ToString());
                for (int q = 1; q < 4; q++)
                    tDrawCell(SHAPE_SOLID, C_WHITE, nPageX(b * 384 + q * 96, nTicksPerLine) - PAGE_NOTE_INSET,
                              nStaffTop, 1, nStaffH, 70);
            }

            // the page is laid out by bar, so the chips come straight from the chart, not from the
            // engine's time-based feed
            this.listNotes.Clear();
            List<CChip> listChip = (CDTXMania.DTX != null) ? CDTXMania.DTX.listChip : null;
            if (listChip == null) return;
            int nEndPos = nStartPos + nTicksPerLine;
            for (int i = 0; i < listChip.Count; i++)
            {
                CChip chip = listChip[i];
                if (chip.nPlaybackPosition >= nEndPos) break;        // listChip is sorted by position
                if (chip.nPlaybackPosition < nStartPos) continue;
                STNote note;
                if (!mapNotes.TryGetValue(chip.nChannelNumber, out note)) continue;
                if (bAlreadyOnThisBeat(chip.nPlaybackPosition, note.nPos)) continue;

                STPendingNote pending;
                pending.x = nPageX(chip.nPlaybackPosition - nStartPos, nTicksPerLine);
                pending.nPlaybackPosition = chip.nPlaybackPosition;
                pending.nAlpha = chip.bHit ? PAGE_HIT_ALPHA : 255;
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
                tDrawCell(SHAPE_SOLID, nColour, LABEL_GUTTER_W, y - nStep,
                          nHeadX - LABEL_GUTTER_W, nSpace, (int)(LANE_FLASH_ALPHA * dbFade));
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
