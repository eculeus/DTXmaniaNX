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
        public const int PLAYHEAD_X = 320;                          // notes are hit when they reach this x
        public const int STAFF_SPACE = 22;                          // px between two staff lines
        public const int STAFF_STEP = STAFF_SPACE / 2;              // one staff position (line -> space)
        public const int STAFF_BOTTOM_Y = 188;                      // y of the bottom line
        public const int STAFF_TOP_Y = STAFF_BOTTOM_Y - 4 * STAFF_SPACE;    // 100
        public const int BAND_TOP_Y = 12;                           // dark backing strip
        public const int BAND_BOTTOM_Y = 250;                       // ...238 px tall
        public const int STEM_TOP_Y = STAFF_BOTTOM_Y - 132;         // up-stems end here (3.5 spaces over the snare),
                                                                    // and the beams sit on that line
        public const int STEM_BOTTOM_Y = STAFF_BOTTOM_Y + 55;       // every down-stem ends here
        public const int BAR_NUMBER_Y = 14;                         // bar number text, inside the band top
        public const double X_SCALE = 1.55;                         // horizontal px per vertical-lane px
                                                                    // (chips stop being fed to us past 600 px,
                                                                    //  320 + 600*1.55 = 1250, i.e. just off-screen)

        // Where the rest of the drums HUD goes while the notation band owns the top of the screen.
        // All of these are only used when ConfigIni.bDrumsNotationView is on.
        public const int PROGRESS_X = 0;                            // song progress bar, laid out horizontally
        public const int PROGRESS_Y = BAND_BOTTOM_Y + 4;            // directly under the band
        public const int PROGRESS_W = 1280;
        public const int PROGRESS_H = 16;
        public const int PANEL_Y = 276;                             // status panel (x is left alone; 439 px tall)
        public const int SCORE_X = 300;                             // score, to the right of the status panel
        public const int SCORE_Y = 276;
        public const int PLAYSPEED_X = 640;                         // "Play Speed" text
        public const int PLAYSPEED_Y = 276;
        public const int SKIP_X = 640;                              // SKIP toast
        public const int SKIP_Y = 320;
        public const int COMBO_X = 850;                             // combo digits (right edge), left of the movie
        public const int COMBO_Y = 400;                             // ...and above the Excite Gauge at y=626
        public const int MOVIE_X = 854;                             // picture-in-picture movie (CActPerfAVI window
        public const int MOVIE_Y = 270;                             //  mode, 416x234): top right, just under the band
        public const bool MOVIE_HIDE_JACKET = true;                 // the tilted jacket card lives in the same
                                                                    // corner, so drop it rather than cover the movie

        // The judgement popup is drawn in the band, centred on the playhead just above the staff.
        // The big animated image (JudgeAnimeType<2 with JudgeFrames>1, the default) is
        // JudgeWidgh x JudgeHeight = 250x170, far too tall for the 76 px of clear band, so it is
        // scaled and hung from its bottom edge; the small classic sprite only needs a centre.
        public const int JUDGE_X = PLAYHEAD_X;                      // judgement popup centre x
        public const int JUDGE_Y = 60;                              // centre y of the small judgement sprite
        public const int JUDGE_BOTTOM_Y = 88;                       // bottom edge of the large animated judgement
        public const float JUDGE_SCALE = 0.45f;                     // ...scaled down to fit over the top ledger line

        // Sizes derived from the staff spacing.
        private const int HEAD_W = 34;              // round notehead cell -> ~27 x 20 px head (1.2 x 0.9 spaces)
        private const int XHEAD_W = 28;             // x notehead cell     -> ~24 px wide
        private const int CIRCLEX_W = 30;           // circled x (open hi-hat)
        private const int STEM_W = 3;
        private const int STEM_BITE = 3;            // the stem is set this far inside the head's right edge so it
                                                    // always crosses an arm of an x head instead of floating beside it
        private const int LEDGER_W = 36;
        private const int LEDGER_H = 3;
        private const int LINE_H = 2;               // staff line thickness
        private const int BAR_LINE_LEAD = 13;       // bar / beat lines are drawn this far left of the
                                                    // notes on that beat, so they never run through a head
        private const int HEAD_CLEAR = 22;          // a note sitting above STEM_TOP_Y (the left crash on its
                                                    // second ledger line) pushes its own stem up by this much
        private const int BEAM_H = 4;
        private const int BEAM_GAP = 7;
        private const bool BEAMS = true;            // join notes of the same beat with a beam

        // Lane legend down the left edge of the band. Two columns, because neighbouring lanes are
        // only one staff step (11 px) apart and a label is taller than that.
        private const int LABEL_COL_X0 = 4;
        private const int LABEL_COL_X1 = 62;
        private const int LABEL_GUTTER_W = 120;     // darker strip the legend sits on
        private const int LABEL_FONT_SIZE = 11;

        // Hit feedback: the lane the player just hit lights up across the part of the band that has
        // already gone by, plus a glow at the playhead, fading out over LANE_FLASH_MS.
        private const int LANE_FLASH_MS = 150;
        private const int LANE_FLASH_ALPHA = 120;
        private const int LANE_GLOW_ALPHA = 210;
        private const int LANE_GLOW_W = 30;
        private const int POS_MIN = -1;             // lowest staff position any voice uses (left pedal)
        private const int POS_MAX = 12;             // highest (left crash on its second ledger line)

        // Sprite sheet: 5 shape columns x 13 colour rows of 32x32 cells.
        private const int CELL = 32;
        private const int SHAPE_SOLID = 0, SHAPE_HEAD = 1, SHAPE_X = 2, SHAPE_CIRCLE_X = 3, SHAPE_DIAMOND = 4;
        private const int C_WHITE = 0, C_PURPLE = 1, C_YELLOW = 2, C_GREEN = 3, C_RED = 4, C_ORANGE = 5,
                          C_BLUE = 6, C_DEEPBLUE = 7, C_CYAN = 8, C_MAGENTA = 9, C_PINK = 10,
                          C_DARK = 11, C_PLAYHEAD = 12;

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

        // Standard drum-set notation on a percussion staff, coloured like the vertical lanes
        // (sampled from Graphics\7_chips_drums.png: BD/LBD purple, HH blue, SD yellow, HT green,
        //  LT red, FT orange, CY blue, RD pale cyan, LC magenta, LP pink).
        private static readonly Dictionary<EChannel, STNote> mapNotes = new Dictionary<EChannel, STNote>
        {
            { EChannel.HiHatClose,   new STNote( 9, SHAPE_X,        C_BLUE,     true ) },
            { EChannel.Snare,        new STNote( 5, SHAPE_HEAD,     C_YELLOW,   true ) },
            { EChannel.BassDrum,     new STNote( 1, SHAPE_HEAD,     C_PURPLE,   false) },
            { EChannel.HighTom,      new STNote( 7, SHAPE_HEAD,     C_GREEN,    true ) },
            { EChannel.LowTom,       new STNote( 6, SHAPE_HEAD,     C_RED,      true ) },
            { EChannel.Cymbal,       new STNote(10, SHAPE_X,        C_DEEPBLUE, true ) },
            { EChannel.FloorTom,     new STNote( 3, SHAPE_HEAD,     C_ORANGE,   true ) },
            { EChannel.HiHatOpen,    new STNote( 9, SHAPE_CIRCLE_X, C_BLUE,     true ) },
            { EChannel.RideCymbal,   new STNote( 8, SHAPE_X,        C_CYAN,     true ) },
            { EChannel.LeftCymbal,   new STNote(12, SHAPE_X,        C_MAGENTA,  true ) },
            { EChannel.LeftPedal,    new STNote(-1, SHAPE_X,        C_PINK,     false) },
            { EChannel.LeftBassDrum, new STNote( 1, SHAPE_HEAD,     C_PURPLE,   false) },
        };

        /// <summary>One entry of the legend down the left edge, indexed by ELane (0..9).</summary>
        private struct STLaneLabel
        {
            public int nPos;
            public int nColour;
            public string strText;
            public int nColumn;     // 0 = outer column, 1 = inner; neighbouring lanes alternate
            public STLaneLabel(int pos, int colour, string text, int column) { nPos = pos; nColour = colour; strText = text; nColumn = column; }
        }

        // Indexed exactly like CStagePerfCommonScreen.nチャンネル0Atoレーン07 / ELane.
        private static readonly STLaneLabel[] stLaneLabels = new STLaneLabel[]
        {
            new STLaneLabel(12, C_MAGENTA,  "L.CRASH", 0),  // 0  LC
            new STLaneLabel( 9, C_BLUE,     "HI-HAT",  1),  // 1  HH (and open hi-hat)
            new STLaneLabel( 5, C_YELLOW,   "SNARE",   1),  // 2  SD
            new STLaneLabel( 1, C_PURPLE,   "KICK",    0),  // 3  BD
            new STLaneLabel( 7, C_GREEN,    "HI TOM",  1),  // 4  HT
            new STLaneLabel( 6, C_RED,      "LO TOM",  0),  // 5  LT
            new STLaneLabel( 3, C_ORANGE,   "FLOOR",   1),  // 6  FT
            new STLaneLabel(10, C_DEEPBLUE, "CRASH",   0),  // 7  CY
            new STLaneLabel(-1, C_PINK,     "L.PEDAL", 1),  // 8  LP (and left bass drum)
            new STLaneLabel( 8, C_CYAN,     "RIDE",    0),  // 9  RD
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
                case C_PURPLE:   return Color.FromArgb(173, 125, 255);
                case C_YELLOW:   return Color.FromArgb(255, 226,  77);
                case C_GREEN:    return Color.FromArgb( 92, 224, 116);
                case C_RED:      return Color.FromArgb(255,  72,  72);
                case C_ORANGE:   return Color.FromArgb(255, 153,  41);
                case C_BLUE:     return Color.FromArgb( 77, 179, 255);
                case C_DEEPBLUE: return Color.FromArgb( 77, 134, 255);
                case C_CYAN:     return Color.FromArgb(168, 223, 255);
                case C_MAGENTA:  return Color.FromArgb(255,  61, 140);
                case C_PINK:     return Color.FromArgb(255, 134, 200);
                default:         return Color.White;
            }
        }

        public override int OnUpdateAndDraw()
        {
            return 0;   // drawing is driven by the stage so it interleaves with the chip loop
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
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, LABEL_GUTTER_W, BAND_BOTTOM_Y - BAND_TOP_Y, 150);
            tDrawLaneFlashes();
            for (int i = 0; i < 5; i++)
                tDrawCell(SHAPE_SOLID, C_WHITE, 0, STAFF_BOTTOM_Y - i * STAFF_SPACE - LINE_H / 2, 1280, LINE_H, 235);
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
            if (this.txLaneLabel == null) return;
            for (int i = 0; i < this.txLaneLabel.Length; i++)
            {
                CTexture txLabel = this.txLaneLabel[i];
                if (txLabel == null) continue;
                int y = STAFF_BOTTOM_Y - stLaneLabels[i].nPos * STAFF_STEP;
                txLabel.nTransparency = 255;
                txLabel.tDraw2D(CDTXMania.app.Device,
                                stLaneLabels[i].nColumn == 0 ? LABEL_COL_X0 : LABEL_COL_X1,
                                y - txLabel.szImageSize.Height / 2);
            }
        }

        /// <summary>Remember one chip; the heads and stems are drawn together in tDrawNotes().</summary>
        public void tDrawChip(CChip pChip)
        {
            if (this.tx == null) return;
            STNote note;
            if (!mapNotes.TryGetValue(pChip.nChannelNumber, out note)) return;
            int x = nX(pChip.nDistanceFromBar.Drums);
            if (x < -CELL || x > 1280 + CELL) return;

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
                int nUpTop = int.MaxValue, nUpBottom = int.MinValue, nUpAlpha = 0, nUpHalf = 0;
                int nDownTop = int.MaxValue, nDownBottom = int.MinValue, nDownAlpha = 0, nDownHalf = 0;

                for (int i = nFrom; i <= nTo; i++)
                {
                    STNote note = this.listNotes[i].note;
                    int y = STAFF_BOTTOM_Y - note.nPos * STAFF_STEP;
                    if (note.bStemUp)
                    {
                        if (y < nUpTop) nUpTop = y;
                        if (y > nUpBottom) nUpBottom = y;
                        if (this.listNotes[i].nAlpha > nUpAlpha) nUpAlpha = this.listNotes[i].nAlpha;
                        if (nHeadHalfWidth(note.nShape) > nUpHalf) nUpHalf = nHeadHalfWidth(note.nShape);
                    }
                    else
                    {
                        if (y < nDownTop) nDownTop = y;
                        if (y > nDownBottom) nDownBottom = y;
                        if (this.listNotes[i].nAlpha > nDownAlpha) nDownAlpha = this.listNotes[i].nAlpha;
                        if (nHeadHalfWidth(note.nShape) > nDownHalf) nDownHalf = nHeadHalfWidth(note.nShape);
                    }

                    // ledger lines above the staff (first for the crashes, second for the left crash)
                    for (int nLedger = 10; nLedger <= note.nPos; nLedger += 2)
                    {
                        int nLedgerY = STAFF_BOTTOM_Y - nLedger * STAFF_STEP;
                        tDrawCell(SHAPE_SOLID, C_WHITE, x - LEDGER_W / 2, nLedgerY - LEDGER_H / 2,
                                  LEDGER_W, LEDGER_H, this.listNotes[i].nAlpha);
                    }
                }

                // one stem up for the hands, one stem down for the feet. The stem is set STEM_BITE
                // inside the widest head of the chord, so an x head's arm always meets it.
                int nStemX = x + nUpHalf - STEM_BITE;
                if (nUpBottom != int.MinValue)
                {
                    int nStemTop = Math.Min(STEM_TOP_Y, nUpTop - HEAD_CLEAR);
                    tDrawCell(SHAPE_SOLID, C_WHITE, nStemX, nStemTop, STEM_W, nUpBottom - nStemTop, nUpAlpha);
                }
                if (nDownTop != int.MaxValue)
                    tDrawCell(SHAPE_SOLID, C_WHITE, x - nDownHalf + STEM_BITE - STEM_W, nDownTop, STEM_W, STEM_BOTTOM_Y - nDownTop, nDownAlpha);

                #region [ beams: join the up-stems of one beat (384 ticks per bar, 96 per beat) ]
                if (BEAMS && nUpBottom != int.MinValue)
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
            return HEAD_W;
        }

        /// <summary>
        /// Half the width of the ink actually drawn for a head. The sprite cells do not fill their
        /// 32 px box: the ellipse and diamond span 25/32, the x 27.8/32 and the circled x 28.8/32.
        /// </summary>
        private static int nHeadHalfWidth(int nShape)
        {
            int nCell = nHeadCell(nShape);
            switch (nShape)
            {
                case SHAPE_X:        return nCell * 278 / 320 / 2;   // 28 -> 12
                case SHAPE_CIRCLE_X: return nCell * 288 / 320 / 2;   // 30 -> 13
                default:             return nCell * 252 / 320 / 2;   // 34 -> 13  (ellipse / diamond)
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
            if (x < -4 || x > 1284) return;
            tDrawCell(SHAPE_SOLID, C_WHITE, x - 1, STAFF_TOP_Y, 2, 4 * STAFF_SPACE + 1, 230);
            CDTXMania.actDisplayString.tPrint(x + 4, BAR_NUMBER_Y, CCharacterConsole.EFontType.White, nBarNumber.ToString());
        }

        public void tDrawBeatLine(CChip pChip)
        {
            if (this.tx == null) return;
            int x = nX(pChip.nDistanceFromBar.Drums) - BAR_LINE_LEAD;
            if (x < -2 || x > 1282) return;
            tDrawCell(SHAPE_SOLID, C_WHITE, x, STAFF_TOP_Y, 1, 4 * STAFF_SPACE + 1, 70);
        }

        public void tDrawLoopLine(int nDistanceFromBar, bool bIsEnd)
        {
            if (this.tx == null) return;
            int x = nX(nDistanceFromBar) - BAR_LINE_LEAD;
            if (x < -4 || x > 1284) return;
            tDrawCell(SHAPE_SOLID, C_PLAYHEAD, x - 1, BAND_TOP_Y + 8, 2, BAND_BOTTOM_Y - BAND_TOP_Y - 16, 200);
            CDTXMania.actDisplayString.tPrint(x + 4, BAND_BOTTOM_Y - 18, CCharacterConsole.EFontType.White, bIsEnd ? "End loop" : "Begin loop");
        }

        /// <summary>Draw one 32x32 sprite cell (shape column, colour row) stretched to w x h at (x, y).</summary>
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
