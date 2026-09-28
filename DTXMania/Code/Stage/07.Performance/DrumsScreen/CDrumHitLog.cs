using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FDK;

namespace DTXMania
{
	/// <summary>
	/// Per-hit drum timing log (Config.ini [Log] DrumHitLog=1). See docs/hit-log.md.
	///
	/// While a drums performance runs this only appends small row objects to an in-memory list:
	/// no file I/O, no formatting. When the performance screen is deactivated (clear, fail, Escape,
	/// '=' restart) the list is handed to a worker thread that writes
	/// HitLogs\&lt;yyyyMMdd-HHmmss&gt;_&lt;title&gt;_&lt;difficulty&gt;.csv next to the exe.
	///
	/// It never changes what the game does: every value is read from the same variables the
	/// judgement uses, after the judgement has used them. With the option off no instance exists and
	/// every hook is a null check.
	/// </summary>
	internal sealed class CDrumHitLog
	{
		// Outcome names written to the CSV. docs/hit-log.md explains each.
		public const string HIT = "HIT";
		public const string IGNORED_VELOCITY = "IGNORED_VELOCITY";
		public const string NO_CHIP_IN_RANGE = "NO_CHIP_IN_RANGE";
		public const string MISSED_CHIP = "MISSED_CHIP";
		public const string SKIPPED_CHIP = "SKIPPED_CHIP";
		public const string UNASSIGNED_NOTE = "UNASSIGNED_NOTE";
		public const string SEEK = "SEEK";

		private const int NONE = int.MinValue;
		private const int NEAREST_SEARCH_MS = 1000;   // how far either side to look for "the chip you were aiming at"
		private const int DRIFT_WINDOW_BARS = 16;

		private sealed class Row
		{
			public long nSongMs;
			public int nSegment;
			public int nInputId = NONE;
			public string strPad = "";
			public string strDevice = "";
			public int nCode = NONE;
			public int nVelocity = NONE;
			public string strOutcome = "";
			public string strJudgement = "";
			public int nChipLane = NONE;
			public int nChipMs = NONE;
			public int nLagMs = NONE;
			public int nFileBar = NONE;
			public double dbBeat = double.NaN;
			public int nInputAdjustMs = NONE;
			public string strDetail = "";
			public int nOrder;          // creation order, the tie-break when sorting
			public bool bChordExtra;    // second chip taken by the same stroke; not a separate input

			// only while the row is the current input event
			public EPad ePad;
			public int nVelocityMin;
			public int nHitsOnThisInput;

			public Row Clone()
			{
				return (Row) this.MemberwiseClone();
			}
		}

		private readonly List<Row> listRows = new List<Row>( 4096 );
		private readonly List<CChip> listChip;
		private readonly CPracticeTimeMap timeMap;
		private readonly List<string> listHeader = new List<string>();
		private readonly Dictionary<long, string> dicDrumMidiBinding = new Dictionary<long, string>();
		private readonly string strFilePath;
		private readonly STHitRanges stDrumRanges;
		private readonly STHitRanges stPedalRanges;
		private Row rowCurrentInput;
		private int nSegment;
		private int nNextInputId = 1;
		private int nNextOrder;
		private bool bFinished;


		/// <summary>Null unless DrumHitLog is on. Called from the drums screen's OnActivate.</summary>
		public static CDrumHitLog tCreateIfEnabled( int nInputAdjustTimeMsDrums )
		{
			if ( !CDTXMania.ConfigIni.bDrumHitLog )
				return null;
			try
			{
				return new CDrumHitLog( nInputAdjustTimeMsDrums );
			}
			catch ( Exception e )
			{
				// A diagnostic must never be the reason a song does not start.
				Trace.TraceWarning( "DrumHitLog: disabled for this play, setup failed: " + e );
				return null;
			}
		}

		private CDrumHitLog( int nInputAdjustTimeMsDrums )
		{
			CDTX dtx = CDTXMania.DTX;
			CConfigIni cfg = CDTXMania.ConfigIni;
			this.listChip = ( dtx != null && dtx.listChip != null ) ? dtx.listChip : new List<CChip>();
			this.timeMap = new CPracticeTimeMap( dtx );
			this.stDrumRanges = CDTXMania.stDrumHitRanges;
			this.stPedalRanges = CDTXMania.stDrumPedalHitRanges;

			DateTime dtStart = DateTime.Now;
			string strTitle = ( dtx != null && !string.IsNullOrEmpty( dtx.TITLE ) ) ? dtx.TITLE : "untitled";
			string strChartPath = ( dtx != null ) ? dtx.strファイル名の絶対パス : "";
			string strDifficulty = tDifficultyLabel( strChartPath );

			string strDir = Path.Combine( CDTXMania.strEXEのあるフォルダ, "HitLogs" );
			string strName = string.Format( "{0}_{1}_{2}.csv", dtStart.ToString( "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture ),
				tSanitize( strTitle, 60 ), tSanitize( strDifficulty, 20 ) );
			this.strFilePath = Path.Combine( strDir, strName );

			#region [ drum-pad MIDI bindings, for spotting notes the kit sends that no pad listens to ]
			for ( int nPad = 0; nPad < (int) EPad.MAX; nPad++ )
			{
				CConfigIni.CKeyAssign.STKEYASSIGN[] assigns = cfg.KeyAssign[ (int) EKeyConfigPart.DRUMS ][ nPad ];
				if ( assigns == null )
					continue;
				foreach ( CConfigIni.CKeyAssign.STKEYASSIGN a in assigns )
				{
					if ( a.InputDevice != EInputDevice.MIDI入力 )
						continue;
					long key = tMidiKey( a.ID, a.Code );
					string strPad = tPadName( (EPad) nPad );
					string strOld;
					this.dicDrumMidiBinding[ key ] = this.dicDrumMidiBinding.TryGetValue( key, out strOld ) ? ( strOld + "+" + strPad ) : strPad;
				}
			}
			#endregion

			#region [ header ]
			Action<string, object> h = ( k, v ) => this.listHeader.Add( "# " + k + ": " + Convert.ToString( v, CultureInfo.InvariantCulture ) );
			this.listHeader.Add( "# DTXManiaNX drum hit log (docs/hit-log.md). Lines starting with # are not data." );
			h( "game", CDTXMania.VERSION + " (exe built " + tExeTimestamp() + ")" );
			h( "started", dtStart.ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture ) );
			h( "title", strTitle );
			h( "artist", ( dtx != null ) ? dtx.ARTIST : "" );
			h( "chart", strChartPath );
			h( "difficulty", strDifficulty );
			h( "dlevel", ( dtx != null ) ? dtx.LEVEL.Drums : 0 );
			h( "play_speed", string.Format( CultureInfo.InvariantCulture, "{0:0.000} (PlaySpeed={1})", cfg.nPlaySpeed / 20.0, cfg.nPlaySpeed ) );
			h( "time_stretch", cfg.bTimeStretch ? 1 : 0 );
			h( "input_adjust_ms", nInputAdjustTimeMsDrums + " (InputAdjustTimeDrums; + moves the judgement as if you hit later)" );
			h( "bgm_adjust_ms", string.Format( "chart {0}, common {1}", ( dtx != null ) ? dtx.nBGMAdjust : 0, cfg.nCommonBGMAdjustMs ) );
			h( "pedal_lag_ms", cfg.nPedalLagTime );
			h( "hit_range_ms", tRangesText( this.stDrumRanges ) );
			h( "pedal_hit_range_ms", tRangesText( this.stPedalRanges ) );
			h( "velocity_min", string.Format( "HH={0} SD={1} BD={2} HT={3} LT={4} FT={5} CY={6} RD={7} LC={8} LP={9} LBD={10}",
				cfg.nVelocityMin.HH, cfg.nVelocityMin.SD, cfg.nVelocityMin.BD, cfg.nVelocityMin.HT, cfg.nVelocityMin.LT,
				cfg.nVelocityMin.FT, cfg.nVelocityMin.CY, cfg.nVelocityMin.RD, cfg.nVelocityMin.LC, cfg.nVelocityMin.LP, cfg.nVelocityMin.LBD ) );
			h( "groups", string.Format( "HHGroup={0} FTGroup={1} CYGroup={2} BDGroup={3}",
				(int) cfg.eHHGroup, (int) cfg.eFTGroup, (int) cfg.eCYGroup, (int) cfg.eBDGroup ) );
			h( "tight", cfg.bTight ? 1 : 0 );
			h( "buffered_input", cfg.bバッファ入力を行う ? 1 : 0 );
			h( "vsync", cfg.bVerticalSyncWait ? 1 : 0 );
			h( "sound_device", string.Format( "type={0} ({1}), output delay {2} ms",
				cfg.nSoundDeviceType, CDTXMania.SoundManager != null ? CDTXMania.SoundManager.GetCurrentSoundDeviceType() : "?",
				CDTXMania.SoundManager != null ? CDTXMania.SoundManager.GetSoundDelay() : 0 ) );
			h( "autoplay_lanes", tAutoLanes( cfg ) );
			h( "segments", "a practice-loop lap, '=' rewind, seek or Skip writes a SEEK row and starts a new segment" );
			if ( CDTXMania.InputManager != null )
			{
				foreach ( IInputDevice dev in CDTXMania.InputManager.listInputDevices )
				{
					if ( dev.eInputDeviceType == EInputDeviceType.MidiIn )
						h( "midi_in", string.Format( "MIDI{0} = {1}", dev.ID, dev.strDeviceName ) );
				}
			}
			foreach ( KeyValuePair<long, string> kv in this.dicDrumMidiBinding.OrderBy( kv => kv.Key ) )
				h( "midi_binding", string.Format( "MIDI{0} note {1} -> {2}", kv.Key >> 16, kv.Key & 0xFFFF, kv.Value ) );
			#endregion

			Trace.TraceInformation( "DrumHitLog: on, will write " + this.strFilePath );
		}


		#region [ hooks, called from the performance screens ]

		/// <summary>
		/// A pressed input event for a drum pad, before the game looks for a chip. Every chip the game
		/// then hits for it arrives through tChipJudged() while this event is current.
		/// </summary>
		public void tBeginInput( EPad ePad, STInputEvent inputEvent, long nSongMs, int nInputAdjustMs )
		{
			if ( this.bFinished )
				return;
			this.tEndInput();
			if ( ePad < EPad.HH || ePad > EPad.LBD )
				return;     // Cancel is not a drum

			Row r = new Row();
			r.nSongMs = nSongMs;
			r.nSegment = this.nSegment;
			r.nInputId = this.nNextInputId++;
			r.ePad = ePad;
			r.strPad = tPadName( ePad );
			r.strDevice = tFindDevice( ePad, inputEvent );
			r.nCode = inputEvent.nKey;
			r.nVelocity = inputEvent.nVelocity;
			r.nInputAdjustMs = nInputAdjustMs;
			r.nVelocityMin = tVelocityMin( ePad );
			// Same test, same order as tHandleInput_Drums(): the game drops the event right after this.
			r.strOutcome = ( inputEvent.nVelocity <= r.nVelocityMin ) ? IGNORED_VELOCITY : NO_CHIP_IN_RANGE;
			this.tAdd( r );
			this.rowCurrentInput = r;
		}

		/// <summary>
		/// A drum chip has been judged (CStagePerfCommonScreen.tProcessChipHit), after the judgement is made.
		/// With an input current it is that input's hit; otherwise the chip went past the bar unhit
		/// (or was skipped over with the Skip key).
		/// </summary>
		public void tChipJudged( long nHitTime, CChip pChip, EJudgement eJudgement, bool bAutoPlay, bool bCorrectLane, int nInputAdjustMs )
		{
			if ( this.bFinished || pChip == null )
				return;

			Row cur = this.rowCurrentInput;
			if ( cur != null )
			{
				Row r = cur;
				if ( cur.nHitsOnThisInput > 0 )
				{
					// one stroke that took two chips at the same position (a chord on grouped lanes)
					r = cur.Clone();
					r.bChordExtra = true;
					r.strDetail = "same input as the previous row: chips at the same position hit together";
					this.tAdd( r );
				}
				cur.nHitsOnThisInput++;
				r.strOutcome = HIT;
				r.strJudgement = bAutoPlay ? "Auto" : eJudgement.ToString();
				this.tFillChip( r, pChip );
				r.nLagMs = pChip.nLag;      // exactly what e指定時刻からChipのJUDGEを返す() just computed
				r.nInputAdjustMs = nInputAdjustMs;
				return;
			}

			if ( bAutoPlay )
				return;

			Row m = new Row();
			m.nSongMs = nHitTime;
			m.nSegment = this.nSegment;
			m.strPad = tLanePadName( pChip.nChannelNumber );
			m.strOutcome = bCorrectLane ? MISSED_CHIP : SKIPPED_CHIP;
			m.strJudgement = "Miss";
			this.tFillChip( m, pChip );
			m.nInputAdjustMs = nInputAdjustMs;
			m.strDetail = bCorrectLane ? "passed the bar unhit; song_ms is when the game gave up on it"
			                           : "jumped over with the Skip key (counts as a miss)";
			this.tAdd( m );
		}

		/// <summary>Called when the game has finished with the current input event.</summary>
		public void tEndInput()
		{
			Row r = this.rowCurrentInput;
			this.rowCurrentInput = null;
			if ( r == null || r.nHitsOnThisInput > 0 )
				return;
			this.tExplainUnmatched( r );
		}

		/// <summary>
		/// Once per frame, after the drum input: MIDI note-ons that no drum pad is bound to. The game
		/// ignores these silently; they are what "my hit did nothing" usually is.
		/// </summary>
		public void tScanUnassignedMidi( long nTimerResetSystemTime, int nInputAdjustMs )
		{
			if ( this.bFinished || CDTXMania.InputManager == null )
				return;
			foreach ( IInputDevice dev in CDTXMania.InputManager.listInputDevices )
			{
				if ( dev.eInputDeviceType != EInputDeviceType.MidiIn || dev.listInputEvent == null )
					continue;
				foreach ( STInputEvent ev in dev.listInputEvent )
				{
					if ( !ev.b押された || this.dicDrumMidiBinding.ContainsKey( tMidiKey( dev.ID, ev.nKey ) ) )
						continue;
					Row r = new Row();
					r.nSongMs = ev.nTimeStamp - nTimerResetSystemTime;
					r.nSegment = this.nSegment;
					r.nInputId = this.nNextInputId++;
					r.strDevice = "MIDI" + dev.ID;
					r.nCode = ev.nKey;
					r.nVelocity = ev.nVelocity;
					r.nInputAdjustMs = nInputAdjustMs;
					r.strOutcome = UNASSIGNED_NOTE;
					r.strDetail = "note-on not bound to any drum pad in KeyAssign";
					this.tAdd( r );
				}
			}
		}

		/// <summary>tJumpInSong(): a practice-loop lap, '=' rewind, seek or Skip. Starts a new segment.</summary>
		public void tMarkJump( long nFromMs, long nToMs )
		{
			if ( this.bFinished )
				return;
			this.tEndInput();
			Row r = new Row();
			r.nSongMs = nFromMs;
			r.nSegment = this.nSegment;
			r.strOutcome = SEEK;
			r.strDetail = string.Format( CultureInfo.InvariantCulture, "song time jumps from {0} ms to {1} ms", nFromMs, nToMs );
			this.tAdd( r );
			this.nSegment++;
		}

		/// <summary>
		/// End of the performance. Stops recording and writes the file on a worker thread (a
		/// foreground thread, so a compact-mode exit still waits for the file).
		/// </summary>
		public void tFinishAndWrite()
		{
			if ( this.bFinished )
				return;
			this.tEndInput();
			this.bFinished = true;
			try
			{
				Thread t = new Thread( this.tWriteFile );
				t.IsBackground = false;
				t.Name = "DrumHitLog writer";
				t.Start();
			}
			catch ( Exception e )
			{
				Trace.TraceWarning( "DrumHitLog: could not start the writer thread, writing inline: " + e.Message );
				this.tWriteFile();
			}
		}

		#endregion


		#region [ recording helpers ]

		private void tAdd( Row r )
		{
			r.nOrder = this.nNextOrder++;
			this.listRows.Add( r );
		}

		private void tFillChip( Row r, CChip pChip )
		{
			r.nChipLane = (int) pChip.nChannelNumber;
			r.nChipMs = pChip.nPlaybackTimeMs;
			int nInternalBar = pChip.nPlaybackPosition / 384;
			r.nFileBar = nInternalBar - 1;   // DTXMania puts an empty bar in front of every chart
			double dbBeats = this.timeMap.dbBeatsInFileBar( r.nFileBar );
			r.dbBeat = 1.0 + ( ( pChip.nPlaybackPosition % 384 ) / 384.0 ) * dbBeats;
		}

		/// <summary>
		/// An input that hit nothing: find the chip it was most likely aimed at (nearest in time on
		/// the lanes this pad can reach) and say why it did not count.
		/// </summary>
		private void tExplainUnmatched( Row r )
		{
			int[] lanes = tLanesForPad( r.ePad );
			CChip best = null;
			long nBestDist = long.MaxValue;
			long nFrom = r.nSongMs - NEAREST_SEARCH_MS, nTo = r.nSongMs + NEAREST_SEARCH_MS;
			for ( int i = tLowerBound( nFrom ); i < this.listChip.Count; i++ )
			{
				CChip c = this.listChip[ i ];
				if ( c.nPlaybackTimeMs > nTo )
					break;
				if ( Array.IndexOf( lanes, (int) c.nChannelNumber ) < 0 )
					continue;
				long d = Math.Abs( r.nSongMs - c.nPlaybackTimeMs );
				if ( d < nBestDist )
				{
					nBestDist = d;
					best = c;
				}
			}

			string strWhy;
			if ( r.strOutcome == IGNORED_VELOCITY )
				strWhy = string.Format( "velocity {0} <= VelocityMin {1} for this pad: dropped before any chip was looked at", r.nVelocity, r.nVelocityMin );
			else if ( best == null )
				strWhy = "no chip on this pad's lanes within 1 s";
			else if ( best.bHit )
				strWhy = "nearest chip was already hit (double trigger, flam, or an earlier stroke took it)";
			else
			{
				int nLag = (int) ( r.nSongMs + r.nInputAdjustMs - best.nPlaybackTimeMs );
				int nPoor = tIsPedalLane( (int) best.nChannelNumber ) ? this.stPedalRanges.nPoorSizeMs : this.stDrumRanges.nPoorSizeMs;
				if ( Math.Abs( nLag ) > nPoor )
					strWhy = string.Format( "nearest unhit chip is outside the Poor window (+/-{0} ms)", nPoor );
				else
					strWhy = "nearest unhit chip is in range but on a lane this pad does not hit with the current HH/FT/CY/BD group settings";
			}
			if ( best != null )
			{
				this.tFillChip( r, best );
				r.nLagMs = (int) ( r.nSongMs + r.nInputAdjustMs - best.nPlaybackTimeMs );
				strWhy += " (chip columns = nearest chip, not a hit)";
			}
			if ( r.strOutcome == NO_CHIP_IN_RANGE && CDTXMania.ConfigIni.bTight )
				strWhy += "; Tight is on, so this stroke broke the combo";
			r.strDetail = strWhy;
		}

		private int tLowerBound( long nMs )
		{
			int lo = 0, hi = this.listChip.Count;
			while ( lo < hi )
			{
				int mid = ( lo + hi ) >> 1;
				if ( this.listChip[ mid ].nPlaybackTimeMs < nMs )
					lo = mid + 1;
				else
					hi = mid;
			}
			// chip times are not strictly monotonic (BGM adjust shifts some); step back a little
			return Math.Max( 0, lo - 64 );
		}

		private string tFindDevice( EPad ePad, STInputEvent ev )
		{
			try
			{
				CConfigIni.CKeyAssign.STKEYASSIGN[] assigns = CDTXMania.ConfigIni.KeyAssign[ (int) EKeyConfigPart.DRUMS ][ (int) ePad ];
				foreach ( IInputDevice dev in CDTXMania.InputManager.listInputDevices )
				{
					if ( dev.listInputEvent == null )
						continue;
					bool bHas = false;
					foreach ( STInputEvent e in dev.listInputEvent )
					{
						if ( e.nKey == ev.nKey && e.nTimeStamp == ev.nTimeStamp && e.b押された )
						{
							bHas = true;
							break;
						}
					}
					if ( !bHas )
						continue;
					foreach ( CConfigIni.CKeyAssign.STKEYASSIGN a in assigns )
					{
						if ( a.Code != ev.nKey )
							continue;
						switch ( a.InputDevice )
						{
							case EInputDevice.MIDI入力:
								if ( dev.eInputDeviceType == EInputDeviceType.MidiIn && dev.ID == a.ID ) return "MIDI" + dev.ID;
								break;
							case EInputDevice.Keyboard:
								if ( dev.eInputDeviceType == EInputDeviceType.Keyboard ) return "Keyboard";
								break;
							case EInputDevice.Joypad:
								if ( dev.eInputDeviceType == EInputDeviceType.Joystick && dev.ID == a.ID ) return "Joypad" + dev.ID;
								break;
							case EInputDevice.Mouse:
								if ( dev.eInputDeviceType == EInputDeviceType.Mouse ) return "Mouse";
								break;
						}
					}
				}
			}
			catch ( Exception )
			{
			}
			return "?";
		}

		#endregion


		#region [ writing ]

		private void tWriteFile()
		{
			try
			{
				List<Row> rows = this.listRows.OrderBy( r => r.nSegment ).ThenBy( r => r.nSongMs ).ThenBy( r => r.nOrder ).ToList();
				StringBuilder sb = new StringBuilder( 128 * ( rows.Count + 64 ) );
				foreach ( string s in this.listHeader )
					sb.Append( s ).Append( "\r\n" );
				sb.Append( "song_ms,segment,input_id,pad,device,note_or_key,velocity,outcome,judgement,chip_lane,chip_ms,lag_ms,bar,beat,input_adjust_ms,detail\r\n" );
				foreach ( Row r in rows )
				{
					sb.Append( r.nSongMs.ToString( CultureInfo.InvariantCulture ) ).Append( ',' );
					sb.Append( r.nSegment.ToString( CultureInfo.InvariantCulture ) ).Append( ',' );
					sb.Append( tNum( r.nInputId ) ).Append( ',' );
					sb.Append( tCsv( r.strPad ) ).Append( ',' );
					sb.Append( tCsv( r.strDevice ) ).Append( ',' );
					sb.Append( tNum( r.nCode ) ).Append( ',' );
					sb.Append( tNum( r.nVelocity ) ).Append( ',' );
					sb.Append( r.strOutcome ).Append( ',' );
					sb.Append( r.strJudgement ).Append( ',' );
					sb.Append( r.nChipLane == NONE ? "" : r.nChipLane.ToString( "X2", CultureInfo.InvariantCulture ) ).Append( ',' );
					sb.Append( tNum( r.nChipMs ) ).Append( ',' );
					sb.Append( tNum( r.nLagMs ) ).Append( ',' );
					sb.Append( r.nFileBar == NONE ? "" : r.nFileBar.ToString( "000", CultureInfo.InvariantCulture ) ).Append( ',' );
					sb.Append( double.IsNaN( r.dbBeat ) ? "" : r.dbBeat.ToString( "0.###", CultureInfo.InvariantCulture ) ).Append( ',' );
					sb.Append( tNum( r.nInputAdjustMs ) ).Append( ',' );
					sb.Append( tCsv( r.strDetail ) ).Append( "\r\n" );
				}
				tAppendSummary( sb, rows );

				Directory.CreateDirectory( Path.GetDirectoryName( this.strFilePath ) );
				File.WriteAllText( this.strFilePath, sb.ToString(), new UTF8Encoding( true ) );
				Trace.TraceInformation( "DrumHitLog: wrote {0} ({1} rows)", this.strFilePath, rows.Count );
			}
			catch ( Exception e )
			{
				Trace.TraceWarning( "DrumHitLog: could not write " + this.strFilePath + ": " + e );
			}
		}

		private static void tAppendSummary( StringBuilder sb, List<Row> rows )
		{
			sb.Append( "#\r\n# SUMMARY. lag = hit time + input adjust - chip time: + is late, - is early. Stats over HIT rows (Perfect..Poor, not Auto).\r\n" );
			sb.Append( "# by_pad,pad,inputs,chips_hit,perfect,great,good,poor,ignored_velocity,no_chip_in_range,mean_lag,median_lag,stdev_lag,mean_abs_lag\r\n" );
			string[] pads = { "HH", "HHO", "SD", "BD", "HT", "LT", "FT", "CY", "RD", "LC", "LP", "LBD" };
			List<string> keys = new List<string>( pads );
			keys.Add( "ALL" );
			foreach ( string p in keys )
			{
				List<Row> all = rows.Where( r => r.nInputId != NONE && r.strOutcome != UNASSIGNED_NOTE && ( p == "ALL" || r.strPad == p ) ).ToList();
				if ( all.Count == 0 && p != "ALL" )
					continue;
				List<Row> inputs = all.Where( r => !r.bChordExtra ).ToList();
				List<Row> hits = all.Where( tIsScoredHit ).ToList();
				sb.AppendFormat( CultureInfo.InvariantCulture, "# by_pad,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}\r\n",
					p, inputs.Count, hits.Count,
					hits.Count( r => r.strJudgement == "Perfect" ), hits.Count( r => r.strJudgement == "Great" ),
					hits.Count( r => r.strJudgement == "Good" ), hits.Count( r => r.strJudgement == "Poor" ),
					inputs.Count( r => r.strOutcome == IGNORED_VELOCITY ), inputs.Count( r => r.strOutcome == NO_CHIP_IN_RANGE ),
					tStats( hits ) );
			}

			sb.Append( "# by_lane,chip_lane,chips_judged,perfect,great,good,poor,missed,skipped,mean_lag,median_lag,stdev_lag,mean_abs_lag\r\n" );
			foreach ( int lane in rows.Where( r => r.nChipLane != NONE && ( r.strOutcome == HIT || r.strOutcome == MISSED_CHIP || r.strOutcome == SKIPPED_CHIP ) )
			                          .Select( r => r.nChipLane ).Distinct().OrderBy( x => x ) )
			{
				List<Row> judged = rows.Where( r => r.nChipLane == lane && ( r.strOutcome == HIT || r.strOutcome == MISSED_CHIP || r.strOutcome == SKIPPED_CHIP ) ).ToList();
				List<Row> hits = judged.Where( tIsScoredHit ).ToList();
				sb.AppendFormat( CultureInfo.InvariantCulture, "# by_lane,{0:X2} {1},{2},{3},{4},{5},{6},{7},{8},{9}\r\n",
					lane, tLaneName( lane ), judged.Count,
					hits.Count( r => r.strJudgement == "Perfect" ), hits.Count( r => r.strJudgement == "Great" ),
					hits.Count( r => r.strJudgement == "Good" ), hits.Count( r => r.strJudgement == "Poor" ),
					judged.Count( r => r.strOutcome == MISSED_CHIP ), judged.Count( r => r.strOutcome == SKIPPED_CHIP ),
					tStats( hits ) );
			}

			sb.AppendFormat( CultureInfo.InvariantCulture, "# drift ({0}-bar windows of .dtx file bars; a steady slide in mean_lag means the chart and the audio disagree or you are dragging/rushing),window,hits,missed,mean_lag,median_lag,stdev_lag,mean_abs_lag\r\n", DRIFT_WINDOW_BARS );
			var windows = rows.Where( r => r.nFileBar != NONE && ( tIsScoredHit( r ) || r.strOutcome == MISSED_CHIP ) )
			                  .GroupBy( r => (int) Math.Floor( r.nFileBar / (double) DRIFT_WINDOW_BARS ) ).OrderBy( g => g.Key );
			foreach ( var g in windows )
			{
				List<Row> hits = g.Where( tIsScoredHit ).ToList();
				int nFirst = g.Key * DRIFT_WINDOW_BARS;
				sb.AppendFormat( CultureInfo.InvariantCulture, "# drift,bars {0:000}-{1:000},{2},{3},{4}\r\n",
					nFirst, nFirst + DRIFT_WINDOW_BARS - 1, hits.Count, g.Count( r => r.strOutcome == MISSED_CHIP ), tStats( hits ) );
			}
			int nUnassigned = rows.Count( r => r.strOutcome == UNASSIGNED_NOTE );
			sb.AppendFormat( CultureInfo.InvariantCulture, "# unassigned_midi_notes,{0}{1}\r\n", nUnassigned,
				nUnassigned == 0 ? "" : ",notes: " + string.Join( " ", rows.Where( r => r.strOutcome == UNASSIGNED_NOTE )
					.GroupBy( r => r.strDevice + ":" + r.nCode ).Select( g => g.Key + "x" + g.Count() ) ) );
		}

		private static bool tIsScoredHit( Row r )
		{
			return r.strOutcome == HIT && r.nLagMs != NONE && r.strJudgement != "Auto";
		}

		private static string tStats( List<Row> hits )
		{
			if ( hits.Count == 0 )
				return ",,,";
			double[] lags = hits.Select( r => (double) r.nLagMs ).OrderBy( x => x ).ToArray();
			double mean = lags.Average();
			double median = ( lags.Length % 2 == 1 ) ? lags[ lags.Length / 2 ] : ( lags[ lags.Length / 2 - 1 ] + lags[ lags.Length / 2 ] ) / 2.0;
			double sd = ( lags.Length > 1 ) ? Math.Sqrt( lags.Sum( x => ( x - mean ) * ( x - mean ) ) / ( lags.Length - 1 ) ) : 0.0;
			double meanAbs = lags.Select( Math.Abs ).Average();
			return string.Format( CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0},{3:0.0}", mean, median, sd, meanAbs );
		}

		private static string tNum( int n )
		{
			return n == NONE ? "" : n.ToString( CultureInfo.InvariantCulture );
		}

		private static string tCsv( string s )
		{
			if ( string.IsNullOrEmpty( s ) )
				return "";
			if ( s.IndexOfAny( new[] { ',', '"', '\r', '\n' } ) < 0 )
				return s;
			return "\"" + s.Replace( "\"", "\"\"" ) + "\"";
		}

		#endregion


		#region [ small lookups ]

		private static long tMidiKey( int nDeviceId, int nNote )
		{
			return ( (long) nDeviceId << 16 ) | (uint) ( nNote & 0xFFFF );
		}

		private static string tPadName( EPad ePad )
		{
			switch ( ePad )
			{
				case EPad.HH: return "HH";
				case EPad.SD: return "SD";
				case EPad.BD: return "BD";
				case EPad.HT: return "HT";
				case EPad.LT: return "LT";
				case EPad.FT: return "FT";
				case EPad.CY: return "CY";
				case EPad.HHO: return "HHO";
				case EPad.RD: return "RD";
				case EPad.LC: return "LC";
				case EPad.LP: return "LP";
				case EPad.LBD: return "LBD";
				case EPad.Cancel: return "Cancel";
			}
			return ( (int) ePad ).ToString( CultureInfo.InvariantCulture );
		}

		/// <summary>The pad a lane belongs to when nobody hit it (for MISSED_CHIP rows).</summary>
		private static string tLanePadName( EChannel ch )
		{
			switch ( (int) ch )
			{
				case 0x11: return "HH";
				case 0x12: return "SD";
				case 0x13: return "BD";
				case 0x14: return "HT";
				case 0x15: return "LT";
				case 0x16: return "CY";
				case 0x17: return "FT";
				case 0x18: return "HHO";
				case 0x19: return "RD";
				case 0x1A: return "LC";
				case 0x1B: return "LP";
				case 0x1C: return "LBD";
			}
			return "";
		}

		private static string tLaneName( int lane )
		{
			switch ( lane )
			{
				case 0x11: return "HiHatClose";
				case 0x12: return "Snare";
				case 0x13: return "BassDrum";
				case 0x14: return "HighTom";
				case 0x15: return "LowTom";
				case 0x16: return "Cymbal";
				case 0x17: return "FloorTom";
				case 0x18: return "HiHatOpen";
				case 0x19: return "Ride";
				case 0x1A: return "LeftCymbal";
				case 0x1B: return "LeftPedal";
				case 0x1C: return "LeftBassDrum";
			}
			return "";
		}

		private static bool tIsPedalLane( int lane )
		{
			return lane == 0x13 || lane == 0x1B || lane == 0x1C;
		}

		/// <summary>Lanes a pad can plausibly be aimed at, for the "nearest chip" explanation only.</summary>
		private static int[] tLanesForPad( EPad ePad )
		{
			switch ( ePad )
			{
				case EPad.HH:
				case EPad.HHO: return new[] { 0x11, 0x18, 0x1A };
				case EPad.LC: return new[] { 0x1A, 0x11, 0x18, 0x16 };
				case EPad.SD: return new[] { 0x12 };
				case EPad.BD:
				case EPad.LP:
				case EPad.LBD: return new[] { 0x13, 0x1B, 0x1C };
				case EPad.HT: return new[] { 0x14 };
				case EPad.LT:
				case EPad.FT: return new[] { 0x15, 0x17 };
				case EPad.CY:
				case EPad.RD: return new[] { 0x16, 0x19, 0x1A };
			}
			return new int[ 0 ];
		}

		/// <summary>The VelocityMin tHandleInput_Drums() compares this pad's hits against (HHO uses HH's).</summary>
		private static int tVelocityMin( EPad ePad )
		{
			CConfigIni.STLANEVALUE v = CDTXMania.ConfigIni.nVelocityMin;
			switch ( ePad )
			{
				case EPad.HH:
				case EPad.HHO: return v.HH;
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
			return -1;
		}

		private static string tRangesText( STHitRanges r )
		{
			return string.Format( CultureInfo.InvariantCulture, "Perfect {0} Great {1} Good {2} Poor {3}",
				r.nPerfectSizeMs, r.nGreatSizeMs, r.nGoodSizeMs, r.nPoorSizeMs );
		}

		private static string tAutoLanes( CConfigIni cfg )
		{
			List<string> on = new List<string>();
			if ( cfg.bAutoPlay.LC ) on.Add( "LC" );
			if ( cfg.bAutoPlay.HH ) on.Add( "HH" );
			if ( cfg.bAutoPlay.SD ) on.Add( "SD" );
			if ( cfg.bAutoPlay.BD ) on.Add( "BD" );
			if ( cfg.bAutoPlay.HT ) on.Add( "HT" );
			if ( cfg.bAutoPlay.LT ) on.Add( "LT" );
			if ( cfg.bAutoPlay.FT ) on.Add( "FT" );
			if ( cfg.bAutoPlay.CY ) on.Add( "CY" );
			if ( cfg.bAutoPlay.RD ) on.Add( "RD" );
			if ( cfg.bAutoPlay.LP ) on.Add( "LP" );
			if ( cfg.bAutoPlay.LBD ) on.Add( "LBD" );
			return on.Count == 0 ? "none" : string.Join( " ", on );
		}

		private static string tDifficultyLabel( string strChartPath )
		{
			try
			{
				if ( !CDTXMania.bCompactMode )
				{
					CSongListNode song = CDTXMania.stageSongSelection.rConfirmedSong;
					int n = CDTXMania.stageSongSelection.nConfirmedSongDifficulty;
					if ( song != null && song.arDifficultyLabel != null && n >= 0 && n < song.arDifficultyLabel.Length
					     && !string.IsNullOrEmpty( song.arDifficultyLabel[ n ] ) )
						return song.arDifficultyLabel[ n ];
				}
			}
			catch ( Exception )
			{
			}
			return string.IsNullOrEmpty( strChartPath ) ? "unknown" : Path.GetFileNameWithoutExtension( strChartPath );
		}

		private static string tSanitize( string s, int nMax )
		{
			StringBuilder sb = new StringBuilder();
			char[] bad = Path.GetInvalidFileNameChars();
			foreach ( char c in s.Trim() )
				sb.Append( ( Array.IndexOf( bad, c ) >= 0 || c == '_' ) ? '-' : c );
			string r = sb.ToString().Trim( ' ', '.' );
			if ( r.Length > nMax )
				r = r.Substring( 0, nMax ).Trim( ' ', '.' );
			return r.Length == 0 ? "untitled" : r;
		}

		private static string tExeTimestamp()
		{
			try
			{
				return File.GetLastWriteTime( System.Reflection.Assembly.GetExecutingAssembly().Location ).ToString( "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture );
			}
			catch ( Exception )
			{
				return "?";
			}
		}

		#endregion
	}
}
