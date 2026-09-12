using System;
using System.Collections.Generic;
using System.Drawing;
using SharpDX;
using FDK;

using Rectangle = System.Drawing.Rectangle;

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
        public const int JUDGE_X = 620;                             // judgement string centre
        public const int JUDGE_Y = 430;
        public const int COMBO_X = 1245;                            // combo digits (right edge)
        public const int COMBO_Y = 330;

        // Sizes derived from the staff spacing.
        private const int HEAD_W = 34;              // round notehead cell -> ~27 x 20 px head (1.2 x 0.9 spaces)
        private const int XHEAD_W = 28;             // x notehead cell     -> ~24 px wide
        private const int CIRCLEX_W = 30;           // circled x (open hi-hat)
        private const int STEM_W = 3;
        private const int STEM_DX = 12;             // stem sits just off the right edge of the head
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

        /// <summary>A chip the stage has handed us this frame, ready to be laid out.</summary>
        private struct STPendingNote
        {
            public int x;
            public int nPlaybackPosition;
            public int nAlpha;
            public STNote note;
        }

        private readonly List<STPendingNote> listNotes = new List<STPendingNote>(256);

        public CActPerfDrumsNotation()
        {
            base.bNotActivated = true;
        }

        public override void OnManagedCreateResources()
        {
            if (!base.bNotActivated)
            {
                this.tx = CDTXMania.tGenerateTexture(CSkin.Path(@"Graphics\7_notation.png"));
                base.OnManagedCreateResources();
            }
        }

        public override void OnManagedReleaseResources()
        {
            if (!base.bNotActivated)
            {
                CDTXMania.tReleaseTexture(ref this.tx);
                base.OnManagedReleaseResources();
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

        /// <summary>Dark band, five staff lines and the playhead. Call before the chip loop.</summary>
        public void tDrawStaff()
        {
            this.listNotes.Clear();
            if (this.tx == null) return;
            tDrawCell(SHAPE_SOLID, C_DARK, 0, BAND_TOP_Y, 1280, BAND_BOTTOM_Y - BAND_TOP_Y, 200);
            for (int i = 0; i < 5; i++)
                tDrawCell(SHAPE_SOLID, C_WHITE, 0, STAFF_BOTTOM_Y - i * STAFF_SPACE - LINE_H / 2, 1280, LINE_H, 235);
            tDrawCell(SHAPE_SOLID, C_PLAYHEAD, PLAYHEAD_X - 2, BAND_TOP_Y + 8, 4, BAND_BOTTOM_Y - BAND_TOP_Y - 16, 255);
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
            if (this.tx == null || this.listNotes.Count == 0) return;

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
                int nUpTop = int.MaxValue, nUpBottom = int.MinValue, nUpAlpha = 0;
                int nDownTop = int.MaxValue, nDownBottom = int.MinValue, nDownAlpha = 0;

                for (int i = nFrom; i <= nTo; i++)
                {
                    STNote note = this.listNotes[i].note;
                    int y = STAFF_BOTTOM_Y - note.nPos * STAFF_STEP;
                    if (note.bStemUp)
                    {
                        if (y < nUpTop) nUpTop = y;
                        if (y > nUpBottom) nUpBottom = y;
                        if (this.listNotes[i].nAlpha > nUpAlpha) nUpAlpha = this.listNotes[i].nAlpha;
                    }
                    else
                    {
                        if (y < nDownTop) nDownTop = y;
                        if (y > nDownBottom) nDownBottom = y;
                        if (this.listNotes[i].nAlpha > nDownAlpha) nDownAlpha = this.listNotes[i].nAlpha;
                    }

                    // ledger lines above the staff (first for the crashes, second for the left crash)
                    for (int nLedger = 10; nLedger <= note.nPos; nLedger += 2)
                    {
                        int nLedgerY = STAFF_BOTTOM_Y - nLedger * STAFF_STEP;
                        tDrawCell(SHAPE_SOLID, C_WHITE, x - LEDGER_W / 2, nLedgerY - LEDGER_H / 2,
                                  LEDGER_W, LEDGER_H, this.listNotes[i].nAlpha);
                    }
                }

                // one stem up for the hands, one stem down for the feet, never crossing a head
                if (nUpBottom != int.MinValue)
                {
                    int nStemTop = Math.Min(STEM_TOP_Y, nUpTop - HEAD_CLEAR);
                    tDrawCell(SHAPE_SOLID, C_WHITE, x + STEM_DX, nStemTop, STEM_W, nUpBottom - nStemTop, nUpAlpha);
                }
                if (nDownTop != int.MaxValue)
                    tDrawCell(SHAPE_SOLID, C_WHITE, x - STEM_DX - STEM_W, nDownTop, STEM_W, STEM_BOTTOM_Y - nDownTop, nDownAlpha);

                #region [ beams: join the up-stems of one beat (384 ticks per bar, 96 per beat) ]
                if (BEAMS && nUpBottom != int.MinValue)
                {
                    int nBeat = nPlaybackPosition / 96;
                    if (nBeat == nBeamBeat && nBeamStartX >= 0)
                    {
                        // still inside the same beat: extend the beam to here
                        int nBeams = tBeamCount(nPlaybackPosition - nBeamPos);
                        for (int i = 0; i < nBeams; i++)
                            tDrawCell(SHAPE_SOLID, C_WHITE, nBeamStartX + STEM_DX, STEM_TOP_Y + i * BEAM_GAP,
                                      x - nBeamStartX + STEM_W, BEAM_H, Math.Min(nBeamAlpha, nUpAlpha));
                    }
                    else
                    {
                        nBeamBeat = nBeat;
                    }
                    nBeamStartX = x;
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
                int w = (note.nShape == SHAPE_X) ? XHEAD_W : ((note.nShape == SHAPE_CIRCLE_X) ? CIRCLEX_W : HEAD_W);
                tDrawCell(note.nShape, note.nColour, this.listNotes[i].x - w / 2, y - w / 2, w, w, this.listNotes[i].nAlpha);
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
