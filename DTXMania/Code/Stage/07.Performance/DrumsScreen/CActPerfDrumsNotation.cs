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
    /// drives it: tDrawStaff() once per frame before the chip loop, then tDrawChip / tDrawBarLine /
    /// tDrawBeatLine from the chip loop.  Enabled by Config.ini DrumsNotationView=1.
    /// </summary>
    internal class CActPerfDrumsNotation : CActivity
    {
        // Layout (1280x720 screen)
        public const int PLAYHEAD_X = 360;                          // notes are hit when they reach this x
        public const int STAFF_SPACE = 16;                          // px between two staff lines
        public const int STAFF_STEP = STAFF_SPACE / 2;              // one staff position (line -> space)
        public const int STAFF_BOTTOM_Y = 420;                      // y of the bottom line
        public const int STAFF_TOP_Y = STAFF_BOTTOM_Y - 4 * STAFF_SPACE;
        public const int JUDGE_Y = STAFF_TOP_Y - 120;               // judgement text baseline
        public const double X_SCALE = 1.4;                          // horizontal px per vertical-lane px

        private const int CELL = 32;                                // sprite sheet cell size
        private const int CELL_WHITE = 0, CELL_HEAD = 1, CELL_X = 2, CELL_CIRCLE_X = 3, CELL_DIAMOND = 4, CELL_RED = 5, CELL_DARK = 6;

        private CTexture tx;

        private struct STNote
        {
            public int nPos;        // staff position: 0 = bottom line, 1 = first space, ... 8 = top line, 9 = space above
            public int nCell;       // notehead sprite
            public bool bStemUp;    // hands up, feet down
            public STNote(int pos, int cell, bool up) { nPos = pos; nCell = cell; bStemUp = up; }
        }

        // Standard drum-set notation on a percussion staff
        private static readonly Dictionary<EChannel, STNote> mapNotes = new Dictionary<EChannel, STNote>
        {
            { EChannel.HiHatClose,   new STNote( 9, CELL_X,        true ) },
            { EChannel.Snare,        new STNote( 5, CELL_HEAD,     true ) },
            { EChannel.BassDrum,     new STNote( 1, CELL_HEAD,     false) },
            { EChannel.HighTom,      new STNote( 7, CELL_HEAD,     true ) },
            { EChannel.LowTom,       new STNote( 6, CELL_HEAD,     true ) },
            { EChannel.Cymbal,       new STNote(10, CELL_X,        true ) },
            { EChannel.FloorTom,     new STNote( 3, CELL_HEAD,     true ) },
            { EChannel.HiHatOpen,    new STNote( 9, CELL_CIRCLE_X, true ) },
            { EChannel.RideCymbal,   new STNote( 8, CELL_X,        true ) },
            { EChannel.LeftCymbal,   new STNote(10, CELL_X,        true ) },
            { EChannel.LeftPedal,    new STNote(-1, CELL_X,        false) },
            { EChannel.LeftBassDrum, new STNote( 1, CELL_HEAD,     false) },
        };

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
            if (this.tx == null) return;
            tDrawCell(CELL_DARK, 0, STAFF_TOP_Y - 96, 1280, 4 * STAFF_SPACE + 192, 170);
            for (int i = 0; i < 5; i++)
                tDrawCell(CELL_WHITE, 0, STAFF_BOTTOM_Y - i * STAFF_SPACE - 1, 1280, 2, 230);
            tDrawCell(CELL_RED, PLAYHEAD_X - 2, STAFF_TOP_Y - 56, 4, 4 * STAFF_SPACE + 112, 255);
        }

        public void tDrawChip(CChip pChip)
        {
            if (this.tx == null) return;
            STNote note;
            if (!mapNotes.TryGetValue(pChip.nChannelNumber, out note)) return;
            int x = nX(pChip.nDistanceFromBar.Drums);
            if (x < -CELL || x > 1280 + CELL) return;
            int y = STAFF_BOTTOM_Y - note.nPos * STAFF_STEP;
            int alpha = pChip.bHit ? 70 : 255;
            if (note.nPos >= 10)    // first ledger line above the staff (crashes)
                tDrawCell(CELL_WHITE, x - 14, STAFF_BOTTOM_Y - 10 * STAFF_STEP - 1, 28, 2, alpha);
            if (note.bStemUp)
                tDrawCell(CELL_WHITE, x + 9, y - 44, 2, 44, alpha);
            else
                tDrawCell(CELL_WHITE, x - 11, y, 2, 44, alpha);
            tDrawCell(note.nCell, x - CELL / 2, y - CELL / 2, CELL, CELL, alpha);
        }

        public void tDrawBarLine(CChip pChip, int nBarNumber)
        {
            if (this.tx == null) return;
            int x = nX(pChip.nDistanceFromBar.Drums);
            if (x < -4 || x > 1284) return;
            tDrawCell(CELL_WHITE, x - 1, STAFF_TOP_Y, 2, 4 * STAFF_SPACE + 1, 255);
            CDTXMania.actDisplayString.tPrint(x + 4, STAFF_TOP_Y - 22, CCharacterConsole.EFontType.White, nBarNumber.ToString());
        }

        public void tDrawBeatLine(CChip pChip)
        {
            if (this.tx == null) return;
            int x = nX(pChip.nDistanceFromBar.Drums);
            if (x < -2 || x > 1282) return;
            tDrawCell(CELL_WHITE, x, STAFF_TOP_Y, 1, 4 * STAFF_SPACE + 1, 90);
        }

        public void tDrawLoopLine(int nDistanceFromBar, bool bIsEnd)
        {
            if (this.tx == null) return;
            int x = nX(nDistanceFromBar);
            if (x < -4 || x > 1284) return;
            tDrawCell(CELL_RED, x - 1, STAFF_TOP_Y - 30, 2, 4 * STAFF_SPACE + 60, 200);
            CDTXMania.actDisplayString.tPrint(x + 4, STAFF_BOTTOM_Y + 12, CCharacterConsole.EFontType.White, bIsEnd ? "End loop" : "Begin loop");
        }

        /// <summary>Draw one 32x32 sprite cell stretched to w x h at (x, y).</summary>
        private void tDrawCell(int nCell, int x, int y, int w, int h, int nAlpha)
        {
            this.tx.nTransparency = nAlpha;
            this.tx.vcScaleRatio = new Vector3(w / (float)CELL, h / (float)CELL, 1f);
            this.tx.tDraw2D(CDTXMania.app.Device, x, y, new Rectangle(nCell * CELL, 0, CELL, CELL));
            this.tx.vcScaleRatio = new Vector3(1f, 1f, 1f);
        }
    }
}
