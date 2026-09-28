using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using FDK;

using SlimDXKey = SlimDX.DirectInput.Key;

namespace DTXMania
{
	/// <summary>
	/// Config > Drums > Calibrate: play along with a click and get the InputAdjust that centres your hits.
	/// </summary>
	/// <remarks>
	/// Timing uses exactly what the judgement uses. The click track is one WAV played through the
	/// game's normal sound path and pinned to CSoundManager.rcPerformanceTimer the way the BGM is
	/// (start, then seek to "now - start"), so the device's output latency is in the measurement.
	/// Each hit's time is its STInputEvent.nTimeStamp, which the input layer stamps on that same
	/// timer; frame time is never used. The offsets are raw (hit - click, no InputAdjust applied);
	/// see CCalibrationMath for why the suggestion is then simply -median.
	/// Nothing is changed unless Apply is chosen; Apply sets ConfigIni.nInputAdjustTimeMs.Drums and
	/// the InputAdjust menu item, and Config.ini is written with everything else when CONFIG closes.
	/// </remarks>
	internal class CActConfigCalibration : CActivity
	{
		// click track: 120 BPM, 4 count-in clicks and room for 24 hits with some to spare
		private const int nPeriodMs = 500;
		private const int nClicks = CCalibrationMath.nCountInClicks + 36;
		private const int nLeadMs = 1000;
		private const int nTailMs = 500;
		private const int nSampleRate = 44100;
		private const int nDoubleTriggerMs = 40;		// a second input this close to the last is the same hit
		private const int nResultInputGuardMs = 800;	// ignore decide/cancel this long after the results appear

		private static readonly EPad[] padsDrums = { EPad.HH, EPad.SD, EPad.BD, EPad.HT, EPad.LT, EPad.FT, EPad.CY, EPad.HHO, EPad.RD, EPad.LC, EPad.LP, EPad.LBD };

		private enum EPhase { Idle, Running, Results }
		private EPhase ePhase = EPhase.Idle;

		private CSound soundClick;
		private bool bSoundTried;
		private long nStartMs;			// rcPerformanceTimer system time the click WAV started at
		private long nFirstClickMs;		// ... and its first click
		private long nLastInputMs;
		private long nResultsShownMs;
		private readonly List<int> listAccepted = new List<int>();
		private readonly List<int> listAll = new List<int>();		// every paired hit incl. rejected, for the log
		private int nRejected;
		private int nLastOffset;
		private bool bLastAccepted;
		private CCalibrationMath.CResult result;
		private int nResultCursor;		// 0 Apply, 1 Retry, 2 Cancel
		private int nInputAdjustAtStart;
		private int nPerfectMs;
		private bool bDirty = true;
		private long nLastDrawnClick = long.MinValue;

		private CTexture txPanel;
		private CTexture txBeatOn;
		private CTexture txBeatOff;
		private Font ftTitle, ftBody, ftSmall;
		private const int PANEL_X = 420, PANEL_Y = 96, PANEL_W = 820, PANEL_H = 540;

		public bool bIsRunning
		{
			get { return this.ePhase != EPhase.Idle; }
		}

		public void tStart()
		{
			CDTXMania.Skin.bgmコンフィグ画面.t停止する();
			this.listAccepted.Clear();
			this.listAll.Clear();
			this.nRejected = 0;
			this.result = null;
			this.nLastInputMs = long.MinValue;
			this.nInputAdjustAtStart = CDTXMania.ConfigIni.nInputAdjustTimeMs.Drums;
			this.nPerfectMs = CDTXMania.ConfigIni.stDrumHitRanges.nPerfectSizeMs;
			this.tPrepareSound();

			this.nStartMs = CSoundManager.rcPerformanceTimer.nシステム時刻;
			this.nFirstClickMs = this.nStartMs + nLeadMs;
			if (this.soundClick != null)
			{
				try
				{
					this.soundClick.dbPlaySpeed = 1.0;
					this.soundClick.nVolume = 100;
					this.soundClick.nPosition = 0;
					this.soundClick.tStartPlaying();
					// as CDTX.tAutoCorrectWavPlaybackPosition does for the BGM: put the stream where the timer says it should be
					long nElapsed = CSoundManager.rcPerformanceTimer.nシステム時刻 - this.nStartMs;
					if (nElapsed > 0)
						this.soundClick.tChangePlaybackPosition(nElapsed);
				}
				catch (Exception e)
				{
					Trace.TraceWarning("Calibration: the click could not be played ({0}); the beat is shown on screen only.", e.Message);
				}
			}
			this.ePhase = EPhase.Running;
			this.bDirty = true;
			Trace.TraceInformation("Calibration: started. {0} BPM, {1} count-in clicks, target {2} hits, InputAdjustTimeDrums={3}, DrumPerfect={4}, click sound {5}.",
				60000 / nPeriodMs, CCalibrationMath.nCountInClicks, CCalibrationMath.nTargetHits, this.nInputAdjustAtStart, this.nPerfectMs,
				(this.soundClick != null) ? "on" : "unavailable");
		}

		private void tPrepareSound()
		{
			if (this.bSoundTried)
				return;
			this.bSoundTried = true;
			try
			{
				string path = Path.Combine(Path.GetTempPath(), "DTXManiaNX_calibration_click.wav");
				File.WriteAllBytes(path, CCalibrationMath.tGenerateClickWav(nSampleRate, nLeadMs, nPeriodMs, nClicks, nTailMs));
				this.soundClick = CDTXMania.SoundManager.tGenerateSound(path);
			}
			catch (Exception e)
			{
				this.soundClick = null;
				Trace.TraceWarning("Calibration: no click sound ({0}).", e.Message);
			}
		}

		private void tStopClick()
		{
			if (this.soundClick != null)
			{
				try { this.soundClick.tStopPlayback(); } catch { }
			}
		}

		private void tClose()
		{
			this.tStopClick();
			this.ePhase = EPhase.Idle;
			CDTXMania.stageConfig.tNotifyCalibrationComplete();
		}

		private void tFinish()
		{
			this.tStopClick();
			this.result = CCalibrationMath.tSummarize(this.listAccepted);
			this.nResultCursor = this.result.bEnough ? 0 : 1;
			this.nResultsShownMs = CSoundManager.rcPerformanceTimer.nシステム時刻;
			this.ePhase = EPhase.Results;
			this.bDirty = true;
			Trace.TraceInformation("Calibration: offsets (ms, + late) [{0}]", string.Join(",", this.listAll));
			Trace.TraceInformation("Calibration: accepted {0}, rejected {1}, used {2}, median {3:0.0} ms, MAD {4:0.0} ms, SD {5:0.0} ms, suggested InputAdjustTimeDrums={6}{7}",
				this.result.nAccepted, this.nRejected, this.result.nUsed, this.result.dbMedianMs, this.result.dbMadMs, this.result.dbStdDevMs,
				this.result.nSuggestedInputAdjust, this.result.bEnough ? "" : " (not enough hits, no suggestion)");
		}

		private static int nVelocityMin(EPad pad)
		{
			var v = CDTXMania.ConfigIni.nVelocityMin;
			switch (pad)
			{
				case EPad.HH: case EPad.HHO: return v.HH;
				case EPad.SD: return v.SD;
				case EPad.BD: return v.BD;
				case EPad.HT: return v.HT;
				case EPad.LT: return v.LT;
				case EPad.FT: return v.FT;
				case EPad.CY: return v.CY;
				case EPad.RD: return v.RD;
				case EPad.LC: return v.LC;
				case EPad.LP: return v.LP;
				case EPad.LBD: return v.LBD;
			}
			return 0;
		}

		private void tHandleRunning()
		{
			if (CDTXMania.InputManager.Keyboard.bKeyPressed((int)SlimDXKey.Escape))
			{
				CDTXMania.Skin.soundCancel.tPlay();
				Trace.TraceInformation("Calibration: cancelled.");
				this.tClose();
				return;
			}

			// every drum pad input this frame, in time order
			var times = new List<long>();
			foreach (EPad pad in padsDrums)
			{
				List<STInputEvent> events = CDTXMania.Pad.GetEvents(EInstrumentPart.DRUMS, pad);
				if (events == null)
					continue;
				foreach (STInputEvent ev in events)
				{
					if (ev.b押された && ev.nVelocity > nVelocityMin(pad))
						times.Add(ev.nTimeStamp);
				}
			}
			times.Sort();
			foreach (long t in times)
			{
				if (this.nLastInputMs != long.MinValue && t - this.nLastInputMs < nDoubleTriggerMs)
					continue;	// the same stroke reported twice (a key bound to two pads, a double trigger)
				this.nLastInputMs = t;
				int off; bool acc;
				if (!CCalibrationMath.tPair(t, this.nFirstClickMs, nPeriodMs, nClicks, out off, out acc))
					continue;
				this.listAll.Add(off);
				this.nLastOffset = off;
				this.bLastAccepted = acc;
				if (acc)
					this.listAccepted.Add(off);
				else
					this.nRejected++;
				this.bDirty = true;
				if (this.listAccepted.Count >= CCalibrationMath.nTargetHits)
					break;
			}

			long now = CSoundManager.rcPerformanceTimer.nシステム時刻;
			if (this.listAccepted.Count >= CCalibrationMath.nTargetHits
				|| now > this.nFirstClickMs + (long)(nClicks - 1) * nPeriodMs + nPeriodMs / 2)
			{
				this.tFinish();
			}
		}

		private void tHandleResults()
		{
			if (CSoundManager.rcPerformanceTimer.nシステム時刻 - this.nResultsShownMs < nResultInputGuardMs)
				return;
			var kb = CDTXMania.InputManager.Keyboard;
			if (kb.bKeyPressed((int)SlimDXKey.Escape) || CDTXMania.Pad.bPressed(EInstrumentPart.DRUMS, EPad.LC))
			{
				CDTXMania.Skin.soundCancel.tPlay();
				Trace.TraceInformation("Calibration: closed without applying.");
				this.tClose();
				return;
			}
			bool bLeft = kb.bKeyPressed((int)SlimDXKey.LeftArrow) || kb.bKeyPressed((int)SlimDXKey.UpArrow) || CDTXMania.Pad.bPressed(EInstrumentPart.DRUMS, EPad.HT);
			bool bRight = kb.bKeyPressed((int)SlimDXKey.RightArrow) || kb.bKeyPressed((int)SlimDXKey.DownArrow) || CDTXMania.Pad.bPressed(EInstrumentPart.DRUMS, EPad.LT);
			if (bLeft || bRight)
			{
				CDTXMania.Skin.soundCursorMovement.tPlay();
				int n = this.nResultCursor;
				do { n = (n + (bRight ? 1 : 2)) % 3; } while (n == 0 && !this.result.bEnough);
				this.nResultCursor = n;
				this.bDirty = true;
			}
			bool bDecide = kb.bKeyPressed((int)SlimDXKey.Return) || CDTXMania.Pad.bPressed(EInstrumentPart.DRUMS, EPad.CY) || CDTXMania.Pad.bPressed(EInstrumentPart.DRUMS, EPad.RD);
			if (!bDecide)
				return;
			CDTXMania.Skin.soundDecide.tPlay();
			switch (this.nResultCursor)
			{
				case 0:
					CDTXMania.stageConfig.tApplyDrumsInputAdjust(this.result.nSuggestedInputAdjust);
					Trace.TraceInformation("Calibration: applied InputAdjustTimeDrums={0} (was {1}).", this.result.nSuggestedInputAdjust, this.nInputAdjustAtStart);
					this.tClose();
					break;
				case 1:
					Trace.TraceInformation("Calibration: retry.");
					this.tStart();
					break;
				default:
					Trace.TraceInformation("Calibration: closed without applying.");
					this.tClose();
					break;
			}
		}

		// CActivity

		public override void OnActivate()
		{
			this.ePhase = EPhase.Idle;
			this.bSoundTried = false;
			this.soundClick = null;
			base.OnActivate();
		}
		public override void OnDeactivate()
		{
			if (!base.bNotActivated)
			{
				this.tStopClick();
				if (this.soundClick != null)
				{
					try { CDTXMania.SoundManager.tDiscard(this.soundClick); } catch { }
					this.soundClick = null;
				}
				this.ePhase = EPhase.Idle;
				base.OnDeactivate();
			}
		}
		public override void OnManagedCreateResources()
		{
			if (!base.bNotActivated)
			{
				this.ftTitle = new Font("MS PGothic", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
				this.ftBody = new Font("MS PGothic", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
				this.ftSmall = new Font("MS PGothic", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
				this.txBeatOn = this.tCreateDot(Color.FromArgb(255, 255, 210, 60));
				this.txBeatOff = this.tCreateDot(Color.FromArgb(255, 70, 70, 90));
				this.bDirty = true;
				base.OnManagedCreateResources();
			}
		}
		public override void OnManagedReleaseResources()
		{
			if (!base.bNotActivated)
			{
				CDTXMania.tReleaseTexture(ref this.txPanel);
				CDTXMania.tReleaseTexture(ref this.txBeatOn);
				CDTXMania.tReleaseTexture(ref this.txBeatOff);
				if (this.ftTitle != null) { this.ftTitle.Dispose(); this.ftTitle = null; }
				if (this.ftBody != null) { this.ftBody.Dispose(); this.ftBody = null; }
				if (this.ftSmall != null) { this.ftSmall.Dispose(); this.ftSmall = null; }
				base.OnManagedReleaseResources();
			}
		}
		public override int OnUpdateAndDraw()
		{
			if (base.bNotActivated || this.ePhase == EPhase.Idle)
				return 0;

			if (this.ePhase == EPhase.Running)
				this.tHandleRunning();
			else if (this.ePhase == EPhase.Results)
				this.tHandleResults();
			if (this.ePhase == EPhase.Idle)
				return 0;

			long now = CSoundManager.rcPerformanceTimer.nシステム時刻;
			long kFloor = (long)Math.Floor((double)(now - this.nFirstClickMs) / nPeriodMs);
			if (this.ePhase == EPhase.Running && kFloor != this.nLastDrawnClick)
			{
				this.nLastDrawnClick = kFloor;
				this.bDirty = true;		// the count-in / clicks-left text follows the beat
			}
			if (this.bDirty)
			{
				this.tRenderPanel(kFloor);
				this.bDirty = false;
			}
			if (this.txPanel != null)
				this.txPanel.tDraw2D(CDTXMania.app.Device, PANEL_X, PANEL_Y);

			// beat indicator: four dots, the current beat lit for 120 ms after its click
			if (this.ePhase == EPhase.Running && this.txBeatOn != null && this.txBeatOff != null)
			{
				int beat = -1;
				if (kFloor >= 0 && kFloor < nClicks && now - (this.nFirstClickMs + kFloor * nPeriodMs) < 120)
					beat = (int)(kFloor % 4);
				for (int i = 0; i < 4; i++)
				{
					CTexture tx = (i == beat) ? this.txBeatOn : this.txBeatOff;
					tx.tDraw2D(CDTXMania.app.Device, PANEL_X + PANEL_W - 4 * 56 - 20 + i * 56, PANEL_Y + 18);
				}
			}
			return 0;
		}

		private CTexture tCreateDot(Color c)
		{
			using (var bmp = new Bitmap(44, 44))
			using (var g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				g.Clear(Color.Transparent);
				using (var b = new SolidBrush(c))
					g.FillEllipse(b, 2, 2, 40, 40);
				return CDTXMania.tGenerateTexture(bmp, false);
			}
		}

		private static string strMs(double v)
		{
			int n = (int)Math.Round(v, MidpointRounding.AwayFromZero);
			return (n > 0 ? "+" : "") + n + " ms";
		}

		private void tRenderPanel(long kFloor)
		{
			CDTXMania.tReleaseTexture(ref this.txPanel);
			using (var bmp = new Bitmap(PANEL_W, PANEL_H))
			using (var g = Graphics.FromImage(bmp))
			{
				g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				using (var bg = new SolidBrush(Color.FromArgb(235, 18, 20, 30)))
					g.FillRectangle(bg, 0, 0, PANEL_W, PANEL_H);
				using (var border = new Pen(Color.FromArgb(255, 90, 100, 130), 2))
					g.DrawRectangle(border, 1, 1, PANEL_W - 3, PANEL_H - 3);

				Brush white = Brushes.White;
				Brush grey = new SolidBrush(Color.FromArgb(255, 170, 175, 190));
				Brush gold = new SolidBrush(Color.FromArgb(255, 255, 210, 60));
				Brush red = new SolidBrush(Color.FromArgb(255, 255, 110, 100));
				int x = 24, y = 18;

				g.DrawString("Timing calibration", this.ftTitle, white, x, y);
				y += 40;
				g.DrawString("クリックに合わせてどのパッドでも叩いてください（最初の4回はカウント）。", this.ftSmall, grey, x, y);
				y += 22;
				g.DrawString("Play along with the click on any pad. 4 count-in clicks, then " + CCalibrationMath.nTargetHits + " hits. Esc: cancel.", this.ftSmall, grey, x, y);
				y += 38;

				if (this.ePhase == EPhase.Running)
				{
					string status;
					if (kFloor < 0)
						status = "Get ready...";
					else if (kFloor < CCalibrationMath.nCountInClicks)
						status = "Count-in  " + (kFloor + 1) + " / " + CCalibrationMath.nCountInClicks;
					else
						status = "Hits  " + this.listAccepted.Count + " / " + CCalibrationMath.nTargetHits
							+ (this.nRejected > 0 ? "   (" + this.nRejected + " too far off, not counted)" : "");
					g.DrawString(status, this.ftBody, white, x, y);
					y += 36;
					if (this.listAll.Count > 0)
					{
						string last = "Last hit  " + strMs(this.nLastOffset) + (this.nLastOffset > 0 ? " late" : this.nLastOffset < 0 ? " early" : "")
							+ (this.bLastAccepted ? "" : "  (ignored: over " + CCalibrationMath.nRejectMs + " ms)");
						g.DrawString(last, this.ftBody, this.bLastAccepted ? white : red, x, y);
					}
					y += 36;
					if (this.listAccepted.Count > 0)
					{
						double med = CCalibrationMath.dbMedian(this.listAccepted);
						double sd = CCalibrationMath.dbStdDev(this.listAccepted);
						g.DrawString("Median  " + strMs(med) + "      Spread (SD)  " + ((int)Math.Round(sd)) + " ms", this.ftBody, gold, x, y);
					}
					y += 44;
				}
				else if (this.ePhase == EPhase.Results && this.result != null)
				{
					var r = this.result;
					if (r.bEnough)
					{
						g.DrawString("Median  " + strMs(r.dbMedianMs) + (r.dbMedianMs > 0.5 ? " (late)" : r.dbMedianMs < -0.5 ? " (early)" : "")
							+ "      Spread (SD)  " + ((int)Math.Round(r.dbStdDevMs)) + " ms", this.ftBody, gold, x, y);
						y += 34;
						g.DrawString(r.nUsed + " hits used" + (r.nAccepted > r.nUsed ? ", " + (r.nAccepted - r.nUsed) + " stray dropped" : "")
							+ (this.nRejected > 0 ? ", " + this.nRejected + " over " + CCalibrationMath.nRejectMs + " ms ignored" : ""), this.ftSmall, grey, x, y);
						y += 30;
						g.DrawString("Suggested InputAdjust  " + (r.nSuggestedInputAdjust > 0 ? "+" : "") + r.nSuggestedInputAdjust + " ms   (now "
							+ (this.nInputAdjustAtStart > 0 ? "+" : "") + this.nInputAdjustAtStart + " ms)", this.ftBody, white, x, y);
						y += 34;
						int n95 = (int)Math.Round(2 * r.dbStdDevMs);
						g.DrawString("Perfect is +/-" + this.nPerfectMs + " ms (PerfectRange). About 95% of your hits land within +/-" + n95 + " ms of your average"
							+ (n95 <= this.nPerfectMs ? "." : ": wider than Perfect."), this.ftSmall, grey, x, y);
						y += 30;
					}
					else
					{
						g.DrawString("Only " + r.nUsed + " usable hits (" + CCalibrationMath.nMinHits + " needed). Nothing to suggest; try again.", this.ftBody, red, x, y);
						y += 124;
					}
				}

				// the hits on a +/-150 ms axis, with the Perfect window as it stands now (centred on -InputAdjust)
				int axisY = 380, cx = PANEL_W / 2, half = 330;
				double scale = half / (double)CCalibrationMath.nRejectMs;
				using (var band = new SolidBrush(Color.FromArgb(70, 80, 200, 255)))
				{
					int c0 = cx + (int)(-this.nInputAdjustAtStart * scale);
					int w = (int)(2 * this.nPerfectMs * scale);
					g.FillRectangle(band, c0 - w / 2, axisY - 34, w, 68);
				}
				if (this.ePhase == EPhase.Results && this.result != null && this.result.bEnough)
				{
					using (var pen = new Pen(Color.FromArgb(200, 255, 210, 60), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
					{
						int c1 = cx + (int)(-this.result.nSuggestedInputAdjust * scale);
						int w = (int)(2 * this.nPerfectMs * scale);
						g.DrawRectangle(pen, c1 - w / 2, axisY - 34, w, 68);
					}
				}
				using (var axis = new Pen(Color.FromArgb(255, 150, 155, 170), 1))
				{
					g.DrawLine(axis, cx - half, axisY, cx + half, axisY);
					for (int ms = -150; ms <= 150; ms += 50)
					{
						int tx = cx + (int)(ms * scale);
						g.DrawLine(axis, tx, axisY - (ms == 0 ? 40 : 8), tx, axisY + (ms == 0 ? 40 : 8));
						string lbl = (ms > 0 ? "+" : "") + ms;
						g.DrawString(lbl, this.ftSmall, grey, tx - 14, axisY + 42);
					}
				}
				g.DrawString("early", this.ftSmall, grey, cx - half, axisY - 62);
				g.DrawString("late", this.ftSmall, grey, cx + half - 36, axisY - 62);
				g.DrawString("blue: Perfect with the current InputAdjust" + (this.ePhase == EPhase.Results ? "   dashed: with the suggestion" : ""), this.ftSmall, grey, cx - half, axisY + 68);
				using (var tick = new Pen(Color.FromArgb(200, 255, 255, 255), 2))
				using (var tickLast = new Pen(Color.FromArgb(255, 255, 210, 60), 3))
				{
					for (int i = 0; i < this.listAccepted.Count; i++)
					{
						int tx = cx + (int)(this.listAccepted[i] * scale);
						bool bLast = (i == this.listAccepted.Count - 1) && this.ePhase == EPhase.Running;
						g.DrawLine(bLast ? tickLast : tick, tx, axisY - 26, tx, axisY + 26);
					}
				}
				if (this.ePhase == EPhase.Results && this.result != null && this.result.bEnough)
				{
					using (var medPen = new Pen(Color.FromArgb(255, 255, 210, 60), 3))
					{
						int tx = cx + (int)(this.result.dbMedianMs * scale);
						g.DrawLine(medPen, tx, axisY - 40, tx, axisY + 40);
					}
				}

				if (this.ePhase == EPhase.Results)
				{
					string[] opts = { "Apply", "Retry", "Cancel" };
					int ox = x, oy = PANEL_H - 56;
					for (int i = 0; i < 3; i++)
					{
						bool sel = (i == this.nResultCursor);
						bool enabled = (i != 0) || (this.result != null && this.result.bEnough);
						Brush b = !enabled ? grey : sel ? gold : white;
						string s = sel ? "[ " + opts[i] + " ]" : "  " + opts[i] + "  ";
						g.DrawString(s, this.ftBody, b, ox, oy);
						ox += 170;
					}
					g.DrawString("Left/Right, Enter", this.ftSmall, grey, PANEL_W - 190, oy + 4);
				}

				grey.Dispose(); gold.Dispose(); red.Dispose();
				this.txPanel = CDTXMania.tGenerateTexture(bmp, false);
			}
		}
	}
}
