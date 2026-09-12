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
        public const int PLAYHEAD_X = 300;                          // notes are hit when they reach this x
                                                                    // (just past the legend gutter)
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
                                                                    // 940/(0.80*0.3575) = 3290 ms ahead, about 1.65
                                                                    // bars of 4/4 at 120 BPM, with eighths 72 px
                                                                    // apart. The in-game SPEED setting still scales
                                                                    // it: SPEED 1.0 shows twice as much, 4.0 half.
        public const int LOOKAHEAD_PX = 1500;                       // the chip loop normally stops feeding us chips
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
        public const int JUDGE_X = PLAYHEAD_X;                      // judgement popup centre x
        public const int JUDGE_RISE = 26;                           // its bottom edge sits this far over the head
        public const float JUDGE_SCALE = 0.5f;

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
        private const int BAR_LINE_LEAD = 20;       // bar / beat lines are drawn this far left of the
                                                    // notes on that beat, so they never run through a head
        private const int MIN_STEM_LEN = 8;         // a note sitting at the beam line (the left crash on its
                                                    // second ledger line) keeps a short nub of a stem rather
                                                    // than pushing a lone spike above the beams
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

        // Sprite sheet: 6 shape columns x 8 colour rows of 64x64 cells.
        private const int CELL = 64;
        private const int SHAPE_SOLID = 0, SHAPE_HEAD = 1, SHAPE_X = 2, SHAPE_CIRCLE_X = 3, SHAPE_DIAMOND = 4,
                          SHAPE_BOLD_X = 5;
        // ----------------------------------------------------------------- //
        //  Palette: notes are coloured by LIMB, the way Melodics does it, not //
        //  by instrument. The row and the notehead shape say which drum it is;//
        //  the colour says which hand or foot plays it. Toms whose sticking   //
        //  alternate keep their own hues so they stay easy to tell apart.     //
        // ----------------------------------------------------------------- //
        private const int C_WHITE = 0,      // staff lines, ledger lines, stems, beams
                          C_RIGHT = 1,      // 255,205,40  right hand / right foot
                          C_LEFT  = 2,      // 60,170,255  left hand / left foot
                          C_GREEN = 3,      // 92,224,116  hi tom
                          C_RED   = 4,      // 255,72,72   lo tom
                          C_ORANGE = 5,     // 255,153,41  floor tom
                          C_DARK  = 6,      // band background
                          C_PLAYHEAD = 7;

        private CTexture tx;

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
        // crash, ellipse = drum); the colour names the limb that plays it.
        private static readonly Dictionary<EChannel, STNote> mapNotes = new Dictionary<EChannel, STNote>
        {
            { EChannel.HiHatClose,   new STNote( 9, SHAPE_X,        C_RIGHT, true ) },
            { EChannel.HiHatOpen,    new STNote( 9, SHAPE_CIRCLE_X, C_RIGHT, true ) },
            { EChannel.RideCymbal,   new STNote( 8, SHAPE_DIAMOND,  C_RIGHT, true ) },
            { EChannel.Cymbal,       new STNote(10, SHAPE_BOLD_X,   C_RIGHT, true ) },
            { EChannel.FloorTom,     new STNote( 3, SHAPE_HEAD,     C_ORANGE,true ) },
            { EChannel.BassDrum,     new STNote( 1, SHAPE_HEAD,     C_RIGHT, false) },
            { EChannel.Snare,        new STNote( 5, SHAPE_HEAD,     C_LEFT,  true ) },
            { EChannel.LeftCymbal,   new STNote(12, SHAPE_X,        C_LEFT,  true ) },
            { EChannel.LeftPedal,    new STNote(-1, SHAPE_X,        C_LEFT,  false) },
            { EChannel.LeftBassDrum, new STNote( 1, SHAPE_HEAD,     C_LEFT,  false) },
            { EChannel.HighTom,      new STNote( 7, SHAPE_HEAD,     C_GREEN, true ) },
            { EChannel.LowTom,       new STNote( 6, SHAPE_HEAD,     C_RED,   true ) },
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
            new STLaneLabel(12, C_LEFT,  "L.CRASH"),        // 0  LC
            new STLaneLabel( 9, C_RIGHT, "HI-HAT" ),        // 1  HH (and open hi-hat)
            new STLaneLabel( 5, C_LEFT,  "SNARE"  ),        // 2  SD
            new STLaneLabel( 1, C_RIGHT, "KICK"   ),        // 3  BD
            new STLaneLabel( 7, C_GREEN, "HI TOM" ),        // 4  HT
            new STLaneLabel( 6, C_RED,   "LO TOM" ),        // 5  LT
            new STLaneLabel( 3, C_ORANGE,"FLOOR"  ),        // 6  FT
            new STLaneLabel(10, C_RIGHT, "CRASH"  ),        // 7  CY
            new STLaneLabel(-1, C_LEFT,  "L.PEDAL"),        // 8  LP (and left bass drum)
            new STLaneLabel( 8, C_RIGHT, "RIDE"   ),        // 9  RD
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
                if (!bLaneIsUsed(i)) continue;
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
                case C_RIGHT:  return Color.FromArgb(255, 205,  40);
                case C_LEFT:   return Color.FromArgb( 60, 170, 255);
                case C_GREEN:  return Color.FromArgb( 92, 224, 116);
                case C_RED:    return Color.FromArgb(255,  72,  72);
                case C_ORANGE: return Color.FromArgb(255, 153,  41);
                default:       return Color.White;
            }
        }

        public override int OnUpdateAndDraw()
        {
            return 0;   // drawing is driven by the stage so it interleaves with the chip loop
        }

        /// <summary>Staff y of a lane (ELane 0..9), i.e. the height its noteheads are drawn at.</summary>
        public static int nLaneY(int nLane)
        {
            if (nLane < 0 || nLane >= stLaneLabels.Length) return STAFF_BOTTOM_Y - 5 * STAFF_STEP;
            return STAFF_BOTTOM_Y - stLaneLabels[nLane].nPos * STAFF_STEP;
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
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, 1280, BAND_BOTTOM_Y - BAND_TOP_Y, 200);
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, LABEL_GUTTER_W, BAND_BOTTOM_Y - BAND_TOP_Y, 190);
            tDrawLaneFlashes();
            // the lines start after the legend gutter, or they strike through the labels that sit on a line
            for (int i = 0; i < 5; i++)
                tDrawCell(SHAPE_SOLID, C_WHITE, LABEL_GUTTER_W, STAFF_BOTTOM_Y - i * STAFF_SPACE - LINE_H / 2,
                          1280 - LABEL_GUTTER_W, LINE_H, 235);
            tDrawCell(SHAPE_SOLID, C_PLAYHEAD, PLAYHEAD_X - 2, BAND_TOP_Y + 8, 4, BAND_BOTTOM_Y - BAND_TOP_Y - 16, 255);
        }

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
                int y = STAFF_BOTTOM_Y - (i + POS_MIN) * STAFF_STEP;
                int nColour = this.nLaneFlashColour[i];
                tDrawCell(SHAPE_SOLID, nColour, LABEL_GUTTER_W, y - STAFF_STEP,
                          PLAYHEAD_X - LABEL_GUTTER_W, STAFF_SPACE, (int)(LANE_FLASH_ALPHA * dbFade));
                tDrawCell(SHAPE_HEAD, nColour, PLAYHEAD_X - LANE_GLOW_W / 2, y - LANE_GLOW_W / 2,
                          LANE_GLOW_W, LANE_GLOW_W, (int)(LANE_GLOW_ALPHA * dbFade));
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
                int y = STAFF_BOTTOM_Y - stLaneLabels[i].nPos * STAFF_STEP;
                txLabel.nTransparency = 255;
                int nLabelX = LABEL_GUTTER_W - LABEL_RIGHT_PAD - txLabel.szImageSize.Width;
                if (nLabelX < 2) nLabelX = 2;
                txLabel.tDraw2D(CDTXMania.app.Device, nLabelX, y - txLabel.szImageSize.Height / 2);
            }
        }

        /// <summary>Remember one chip; the heads and stems are drawn together in tDrawNotes().</summary>
        public void tDrawChip(CChip pChip)
        {
            if (this.tx == null) return;
            STNote note;
            if (!mapNotes.TryGetValue(pChip.nChannelNumber, out note)) return;
            int x = nX(pChip.nDistanceFromBar.Drums);
            if (x < LABEL_GUTTER_W || x > 1280 + CELL) return;   // played notes slide away behind the legend

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
            if (this.tx == null) return;
            if (this.listNotes.Count == 0) { tDrawLaneLabels(); return; }

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
                    int y = STAFF_BOTTOM_Y - note.nPos * STAFF_STEP;
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
                        int nLedgerY = STAFF_BOTTOM_Y - nLedger * STAFF_STEP;
                        int nLedgerX = x - LEDGER_W / 2;
                        int nLedgerW = LEDGER_W;
                        if (nLedgerX < LABEL_GUTTER_W) { nLedgerW -= LABEL_GUTTER_W - nLedgerX; nLedgerX = LABEL_GUTTER_W; }
                        tDrawCell(SHAPE_SOLID, C_WHITE, nLedgerX, nLedgerY - LEDGER_H / 2,
                                  nLedgerW, LEDGER_H, this.listNotes[i].nAlpha);
                    }
                }

                // One stem up for the hands, one stem down for the feet. A round head is met at its
                // right edge at the head's own height; an x head has no ink out there, so its stem
                // starts at the tip of the upper-right arm (+arm, -arm) and visibly continues it.
                bool bUpEndIsX = (nUpEndShape == SHAPE_X || nUpEndShape == SHAPE_BOLD_X);
                int nUpArm = bUpEndIsX ? nHeadHalfWidth(nUpEndShape) : nUpHalf;
                int nStemX = x + nUpArm - (bUpEndIsX ? STEM_W / 2 : STEM_BITE);
                if (bStems && nUpBottom != int.MinValue)
                {
                    int nStemBottom = bUpEndIsX ? nUpBottom - nUpArm + STEM_W : nUpBottom;
                    int nStemTop = Math.Min(STEM_TOP_Y, nStemBottom - MIN_STEM_LEN);
                    tDrawCell(SHAPE_SOLID, C_WHITE, nStemX, nStemTop, STEM_W, nStemBottom - nStemTop, nUpAlpha);
                }
                if (bStems && nDownTop != int.MaxValue)
                {
                    bool bDownEndIsX = (nDownEndShape == SHAPE_X || nDownEndShape == SHAPE_BOLD_X);
                    int nDownArm = bDownEndIsX ? nHeadHalfWidth(nDownEndShape) : nDownHalf;
                    int nDownStemX = x - nDownArm + (bDownEndIsX ? STEM_W / 2 : STEM_BITE) - STEM_W;
                    int nDownStemTop = bDownEndIsX ? nDownTop + nDownArm - STEM_W : nDownTop;
                    tDrawCell(SHAPE_SOLID, C_WHITE, nDownStemX, nDownStemTop, STEM_W, STEM_BOTTOM_Y - nDownStemTop, nDownAlpha);
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
                            tDrawCell(SHAPE_SOLID, C_WHITE, nBeamStartX, STEM_TOP_Y + i * BEAM_GAP,
                                      nStemX - nBeamStartX + STEM_W, BEAM_H, Math.Min(nBeamAlpha, nUpAlpha));
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
                int y = STAFF_BOTTOM_Y - note.nPos * STAFF_STEP;
                int w = nHeadCell(note.nShape);
                tDrawCell(note.nShape, note.nColour, this.listNotes[i].x - w / 2, y - w / 2, w, w, this.listNotes[i].nAlpha);
            }

            tDrawLaneLabels();
        }

        /// <summary>Cell size each notehead shape is stretched to.</summary>
        private static int nHeadCell(int nShape)
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
        private static int nHeadHalfWidth(int nShape)
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
            if (this.tx == null) return;
            int x = nX(pChip.nDistanceFromBar.Drums) - BAR_LINE_LEAD;
            if (x < LABEL_GUTTER_W || x > 1284) return;     // never into the legend gutter
            tDrawCell(SHAPE_SOLID, C_WHITE, x - 1, STAFF_TOP_Y, 2, 4 * STAFF_SPACE + 1, 230);
            CDTXMania.actDisplayString.tPrint(x + 4, BAR_NUMBER_Y, CCharacterConsole.EFontType.White, nBarNumber.ToString());
        }

        public void tDrawBeatLine(CChip pChip)
        {
            if (this.tx == null) return;
            int x = nX(pChip.nDistanceFromBar.Drums) - BAR_LINE_LEAD;
            if (x < LABEL_GUTTER_W || x > 1282) return;
            tDrawCell(SHAPE_SOLID, C_WHITE, x, STAFF_TOP_Y, 1, 4 * STAFF_SPACE + 1, 70);
        }

        public void tDrawLoopLine(int nDistanceFromBar, bool bIsEnd)
        {
            if (this.tx == null) return;
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
