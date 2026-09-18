using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FDK;

namespace DTXMania
{
	/// <summary>
	/// 練習モード(PRACTICE)の区間。
	/// 曲フォルダの sections.def から読んだ「曲の構成」と、ユーザが自分で保存した区間の両方を表す。
	/// 位置は .dtx のファイル上の小節番号(#nnn の nnn)で持ち、ms への変換は CPracticeTimeMap が
	/// 一箇所で行う。詳細は docs/practice-mode.md を参照。
	/// </summary>
	public enum EPracticePositionType
	{
		Bar,		// 小節(+拍)指定
		TimeMs,		// 絶対時刻(ms)指定  ("@12345")
	}

	public struct STPracticePosition
	{
		public EPracticePositionType eType;
		/// <summary>.dtx のファイル上の小節番号 (#000 が 0)。ゲーム内部の小節番号ではない。</summary>
		public int nFileBar;
		/// <summary>小節内の拍。1 が小節頭。小節長を考慮した拍数で数える(6/4 の小節なら 1..6)。</summary>
		public double dbBeat;
		/// <summary>eType == TimeMs のときの曲頭からの絶対時刻(ms)。</summary>
		public int nTimeMs;

		public static STPracticePosition FromBar( int nFileBar, double dbBeat )
		{
			STPracticePosition st;
			st.eType = EPracticePositionType.Bar;
			st.nFileBar = nFileBar;
			st.dbBeat = dbBeat;
			st.nTimeMs = 0;
			return st;
		}
		public static STPracticePosition FromTimeMs( int nTimeMs )
		{
			STPracticePosition st;
			st.eType = EPracticePositionType.TimeMs;
			st.nFileBar = 0;
			st.dbBeat = 1.0;
			st.nTimeMs = nTimeMs;
			return st;
		}

		/// <summary>"014" / "030:4" / "@12345" の形式で書き出す。ファイルにも画面にも同じ表記を使う。</summary>
		public override string ToString()
		{
			if( this.eType == EPracticePositionType.TimeMs )
				return "@" + this.nTimeMs.ToString( CultureInfo.InvariantCulture );

			if( Math.Abs( this.dbBeat - 1.0 ) < 0.0005 )
				return this.nFileBar.ToString( "000", CultureInfo.InvariantCulture );

			return this.nFileBar.ToString( "000", CultureInfo.InvariantCulture ) + ":"
				+ this.dbBeat.ToString( "0.###", CultureInfo.InvariantCulture );
		}

		/// <summary>"014" / "030:4" / "@12345" を読む。失敗したら false。</summary>
		public static bool TryParse( string str, out STPracticePosition st )
		{
			st = STPracticePosition.FromBar( 0, 1.0 );
			if( string.IsNullOrEmpty( str ) )
				return false;

			str = str.Trim();

			if( str.StartsWith( "@" ) )
			{
				int nMs;
				if( !int.TryParse( str.Substring( 1 ).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out nMs ) )
					return false;
				st = STPracticePosition.FromTimeMs( Math.Max( 0, nMs ) );
				return true;
			}

			string strBar = str;
			double dbBeat = 1.0;
			int nColon = str.IndexOf( ':' );
			if( nColon >= 0 )
			{
				strBar = str.Substring( 0, nColon ).Trim();
				string strBeat = str.Substring( nColon + 1 ).Trim();
				if( !double.TryParse( strBeat, NumberStyles.Float, CultureInfo.InvariantCulture, out dbBeat ) )
					return false;
			}

			int nBar;
			if( !int.TryParse( strBar, NumberStyles.Integer, CultureInfo.InvariantCulture, out nBar ) )
				return false;
			if( nBar < 0 )
				return false;
			if( dbBeat < 1.0 )
				dbBeat = 1.0;

			st = STPracticePosition.FromBar( nBar, dbBeat );
			return true;
		}
	}

	public class CPracticeRange
	{
		public const int nLeadBarsDefault = 1;

		public string strName = "";
		/// <summary>区間の本来の開始位置。準備用の先行小節(nLeadBars)は含まない。</summary>
		public STPracticePosition stStart = STPracticePosition.FromBar( 0, 1.0 );
		/// <summary>区間の終了位置。この位置は区間に含まれない(次の区間の開始位置と同じ)。</summary>
		public STPracticePosition stEnd = STPracticePosition.FromBar( 0, 1.0 );
		/// <summary>ループ開始を何小節前に取るか。曲の構成から自動生成した区間は 1 が既定。</summary>
		public int nLeadBars = nLeadBarsDefault;
		/// <summary>ユーザが自分で保存した区間なら true。曲フォルダの sections.def 由来なら false。</summary>
		public bool bIsUserRange = false;

		public CPracticeRange()
		{
		}
		public CPracticeRange( string strName, STPracticePosition stStart, STPracticePosition stEnd, int nLeadBars )
		{
			this.strName = strName ?? "";
			this.stStart = stStart;
			this.stEnd = stEnd;
			this.nLeadBars = Math.Max( 0, nLeadBars );
		}

		public CPracticeRange Clone()
		{
			return new CPracticeRange( this.strName, this.stStart, this.stEnd, this.nLeadBars ) { bIsUserRange = this.bIsUserRange };
		}

		/// <summary>"014 - 020 (-1)" のような、画面に出す短い表記。</summary>
		public string strRangeText
		{
			get
			{
				string str = this.stStart.ToString() + " - " + this.stEnd.ToString();
				if( this.nLeadBars > 0 )
					str += " (-" + this.nLeadBars.ToString( CultureInfo.InvariantCulture ) + ")";
				return str;
			}
		}
	}

	/// <summary>
	/// 譜面のチップ時刻から作る「小節+拍 ⇔ ms」の変換表。
	/// BPM はチップなので小節の途中でも変わりうる。ここでは自分で BPM を積算せず、
	/// ゲームが計算済みの CChip.nPlaybackTimeMs を引いて線形補間するだけにしてある。
	/// (小節線チップは全小節に、拍線チップは全拍に入っているので、補間区間の中で
	///  BPM も小節長も変わらない = 補間は厳密。)
	/// </summary>
	internal class CPracticeTimeMap
	{
		private readonly List<CChip> listChip;
		private readonly double[] dbBarLength;		// 内部小節番号で引く小節長倍率
		private readonly int nMaxInternalBar;

		/// <summary>この譜面で指定できる最大のファイル小節番号。</summary>
		public int nMaxFileBar
		{
			get { return Math.Max( 0, this.nMaxInternalBar - 1 ); }
		}

		/// <summary>譜面の最後のチップの時刻(ms)。</summary>
		public int nSongEndTimeMs
		{
			get { return ( this.listChip.Count > 0 ) ? this.listChip[ this.listChip.Count - 1 ].nPlaybackTimeMs : 0; }
		}

		public CPracticeTimeMap( CDTX dtx )
		{
			this.listChip = ( dtx != null && dtx.listChip != null ) ? dtx.listChip : new List<CChip>();

			// listChip は発声位置で整列済み(CChip.CompareTo)なので、最後のチップが最大の小節。
			this.nMaxInternalBar = ( this.listChip.Count > 0 )
				? ( this.listChip[ this.listChip.Count - 1 ].nPlaybackPosition / 384 )
				: 0;

			// 小節長倍率(Ch.02)は指定された小節から後にずっと効き続ける。小節ごとに読むと 3/4 や 6/4 の曲が壊れる。
			this.dbBarLength = new double[ this.nMaxInternalBar + 2 ];
			double db = 1.0;
			int nBar = 0;
			for( int i = 0; i < this.listChip.Count; i++ )
			{
				CChip chip = this.listChip[ i ];
				int nChipBar = chip.nPlaybackPosition / 384;
				while( nBar <= Math.Min( nChipBar, this.nMaxInternalBar + 1 ) )
					this.dbBarLength[ nBar++ ] = db;
				if( chip.nChannelNumber == EChannel.BarLength )
				{
					db = chip.db実数値;
					if( nChipBar >= 0 && nChipBar < this.dbBarLength.Length )
						this.dbBarLength[ nChipBar ] = db;
				}
			}
			while( nBar < this.dbBarLength.Length )
				this.dbBarLength[ nBar++ ] = db;
		}

		/// <summary>その小節の拍数。4/4 なら 4、6/4 なら 6、3/4 なら 3。</summary>
		public double dbBeatsInFileBar( int nFileBar )
		{
			int nInternalBar = nFileBar + 1;
			if( nInternalBar < 0 )
				nInternalBar = 0;
			if( nInternalBar >= this.dbBarLength.Length )
				nInternalBar = this.dbBarLength.Length - 1;
			return 4.0 * this.dbBarLength[ nInternalBar ];
		}

		/// <summary>
		/// ファイル上の小節番号+拍 → チップ位置(384 分解能)。
		/// CDTX の t入力・行解析・チップ配置() が譜面の先頭に空の1小節を入れているので、
		/// 内部小節番号 = ファイル小節番号 + 1。この +1 はここだけで行う。
		/// </summary>
		public int nTickAt( int nFileBar, double dbBeat )
		{
			int nInternalBar = Math.Max( 0, nFileBar + 1 );
			double dbBeats = 4.0 * this.dbBarLength[ Math.Min( nInternalBar, this.dbBarLength.Length - 1 ) ];
			if( dbBeats <= 0.0 )
				dbBeats = 4.0;

			double dbInBar = ( dbBeat - 1.0 ) / dbBeats;
			if( dbInBar < 0.0 ) dbInBar = 0.0;
			if( dbInBar > 1.0 ) dbInBar = 1.0;

			return ( nInternalBar * 384 ) + (int) Math.Round( dbInBar * 384.0 );
		}

		/// <summary>チップ位置(384 分解能) → ms。前後のチップの時刻を線形補間する。</summary>
		public long nTimeMsAtTick( int nTick )
		{
			if( this.listChip.Count == 0 )
				return 0;
			if( nTick <= this.listChip[ 0 ].nPlaybackPosition )
				return this.listChip[ 0 ].nPlaybackTimeMs;

			int nLast = this.listChip.Count - 1;
			if( nTick >= this.listChip[ nLast ].nPlaybackPosition )
				return this.listChip[ nLast ].nPlaybackTimeMs;

			// nTick 以下で最大の位置を持つチップを二分探索
			int lo = 0, hi = nLast;
			while( lo < hi )
			{
				int mid = ( lo + hi + 1 ) / 2;
				if( this.listChip[ mid ].nPlaybackPosition <= nTick )
					lo = mid;
				else
					hi = mid - 1;
			}

			CChip chipBefore = this.listChip[ lo ];
			if( chipBefore.nPlaybackPosition == nTick )
				return chipBefore.nPlaybackTimeMs;

			int nAfter = lo + 1;
			while( nAfter <= nLast && this.listChip[ nAfter ].nPlaybackPosition == chipBefore.nPlaybackPosition )
				nAfter++;
			if( nAfter > nLast )
				return chipBefore.nPlaybackTimeMs;

			CChip chipAfter = this.listChip[ nAfter ];
			int nSpan = chipAfter.nPlaybackPosition - chipBefore.nPlaybackPosition;
			if( nSpan <= 0 )
				return chipBefore.nPlaybackTimeMs;

			double dbRatio = ( (double) ( nTick - chipBefore.nPlaybackPosition ) ) / nSpan;
			return (long) Math.Round( chipBefore.nPlaybackTimeMs
				+ dbRatio * ( chipAfter.nPlaybackTimeMs - chipBefore.nPlaybackTimeMs ) );
		}

		/// <summary>区間の位置 → ms。nLeadBars 小節ぶん手前に寄せる(準備時間)。</summary>
		public long nTimeMsAt( STPracticePosition st, int nLeadBars )
		{
			if( st.eType == EPracticePositionType.TimeMs )
				return st.nTimeMs;

			int nFileBar = st.nFileBar - Math.Max( 0, nLeadBars );
			if( nFileBar < 0 )
				nFileBar = 0;

			return this.nTimeMsAtTick( this.nTickAt( nFileBar, st.dbBeat ) );
		}
	}

	/// <summary>
	/// 曲フォルダの sections.def (曲の構成) と、ユーザ自身が保存した区間 (PracticeRanges.ini) の読み書き。
	/// </summary>
	public static class CPracticeSections
	{
		public const string strSectionFileName = "sections.def";
		public const string strUserRangeFileName = "PracticeRanges.ini";

		#region [ 曲フォルダの sections.def ]

		/// <summary>
		/// 譜面に付いてくる区間表を読む。
		/// &lt;譜面ファイル名&gt;.sections.def があればそれを、なければフォルダの sections.def を使う。
		/// 無ければ空のリストを返す(エラーにはしない)。
		/// </summary>
		public static List<CPracticeRange> tLoadSongSections( string strChartPath )
		{
			var list = new List<CPracticeRange>();
			if( string.IsNullOrEmpty( strChartPath ) )
				return list;

			try
			{
				string strFolder = Path.GetDirectoryName( strChartPath );
				if( string.IsNullOrEmpty( strFolder ) )
					return list;

				string strPerChart = Path.Combine( strFolder, Path.GetFileName( strChartPath ) + "." + strSectionFileName );
				string strPerFolder = Path.Combine( strFolder, strSectionFileName );
				string strPath = File.Exists( strPerChart ) ? strPerChart : strPerFolder;
				if( !File.Exists( strPath ) )
					return list;

				int nDefaultLeadBars = CPracticeRange.nLeadBarsDefault;
				foreach( string strRaw in tReadAllLines( strPath ) )
				{
					string str = strRaw;
					int nComment = str.IndexOf( ';' );
					if( nComment >= 0 )
						str = str.Substring( 0, nComment );
					str = str.Trim();
					if( str.Length == 0 || !str.StartsWith( "#" ) )
						continue;

					int nColon = str.IndexOf( ':' );
					if( nColon < 0 )
						continue;
					string strKey = str.Substring( 1, nColon - 1 ).Trim().ToUpperInvariant();
					string strValue = str.Substring( nColon + 1 ).Trim();

					if( strKey.Equals( "LEADBARS" ) )
					{
						int n;
						if( int.TryParse( strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out n ) && n >= 0 )
							nDefaultLeadBars = n;
					}
					else if( strKey.Equals( "BARBASE" ) )
					{
						// DTX 以外は今のところ無い。違う値なら読み手が古いということなので警告だけ出して DTX 扱いにする。
						if( !strValue.Trim().ToUpperInvariant().Equals( "DTX" ) )
							Trace.TraceWarning( "practice: #BARBASE が DTX ではありません({0})。DTX として読みます。[{1}]", strValue, strPath );
					}
					else if( strKey.Equals( "SECTION" ) )
					{
						CPracticeRange range = tParseSectionValue( strValue, nDefaultLeadBars, strPath );
						if( range != null )
							list.Add( range );
					}
					// #VERSION その他の未知のキーは読み飛ばす(前方互換)。
				}

				Trace.TraceInformation( "practice: {0} から区間を {1} 件読みました。", strPath, list.Count );
			}
			catch( Exception e )
			{
				Trace.TraceWarning( "practice: 区間表の読み込みに失敗しました。({0}) {1}", strChartPath, e.Message );
			}

			return list;
		}

		/// <summary>"009,014,Verse 1[,lead]" を 1 区間に。</summary>
		private static CPracticeRange tParseSectionValue( string strValue, int nDefaultLeadBars, string strPath )
		{
			string[] parts = strValue.Split( new char[] { ',' }, 4 );
			if( parts.Length < 2 )
			{
				Trace.TraceWarning( "practice: #SECTION の引数が足りません。[{0}] {1}", strPath, strValue );
				return null;
			}

			STPracticePosition stStart, stEnd;
			if( !STPracticePosition.TryParse( parts[ 0 ], out stStart ) || !STPracticePosition.TryParse( parts[ 1 ], out stEnd ) )
			{
				Trace.TraceWarning( "practice: #SECTION の位置が読めません。[{0}] {1}", strPath, strValue );
				return null;
			}

			string strName = ( parts.Length >= 3 ) ? parts[ 2 ].Trim() : "";
			int nLeadBars = nDefaultLeadBars;
			if( parts.Length >= 4 )
			{
				int n;
				if( int.TryParse( parts[ 3 ].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n ) && n >= 0 )
					nLeadBars = n;
			}

			return new CPracticeRange( strName, stStart, stEnd, nLeadBars );
		}

		#endregion

		#region [ ユーザが保存した区間 (PracticeRanges.ini) ]

		/// <summary>
		/// ユーザの区間は曲フォルダではなくゲーム側に置く。曲フォルダは Google Drive から
		/// 入れ直されることがあり、その度に消えてしまうため。
		/// </summary>
		public static string strUserRangeFilePath
		{
			get
			{
				string strFolder = CDTXMania.strEXEのあるフォルダ;
				return string.IsNullOrEmpty( strFolder ) ? null : Path.Combine( strFolder, strUserRangeFileName );
			}
		}

		/// <summary>
		/// 曲を表すキー。フォルダの親+自身の名前にしてあるので、ドライブ文字が変わっても
		/// DTXFiles ごと移動しても同じ曲として引ける。同じ名前のフォルダが複数あると衝突する。
		/// </summary>
		public static string strSongKey( string strChartPath )
		{
			if( string.IsNullOrEmpty( strChartPath ) )
				return "";
			try
			{
				string strFolder = Path.GetDirectoryName( Path.GetFullPath( strChartPath ) );
				if( string.IsNullOrEmpty( strFolder ) )
					return "";
				string strLeaf = Path.GetFileName( strFolder.TrimEnd( Path.DirectorySeparatorChar ) );
				string strParent = Path.GetFileName( Path.GetDirectoryName( strFolder ) ?? "" );
				string strKey = ( string.IsNullOrEmpty( strParent ) ? strLeaf : strParent + "/" + strLeaf );
				return strKey.Replace( ']', ')' ).Replace( '[', '(' ).ToLowerInvariant();
			}
			catch
			{
				return "";
			}
		}

		public static List<CPracticeRange> tLoadUserRanges( string strChartPath )
		{
			var list = new List<CPracticeRange>();
			string strKey = strSongKey( strChartPath );
			if( strKey.Length == 0 )
				return list;

			string strPath = strUserRangeFilePath;
			if( string.IsNullOrEmpty( strPath ) || !File.Exists( strPath ) )
				return list;

			try
			{
				bool bInSection = false;
				foreach( string strRaw in tReadAllLines( strPath ) )
				{
					string str = strRaw.Trim();
					if( str.Length == 0 || str.StartsWith( ";" ) )
						continue;

					if( str.StartsWith( "[" ) && str.EndsWith( "]" ) )
					{
						bInSection = str.Substring( 1, str.Length - 2 ).Trim().ToLowerInvariant().Equals( strKey );
						continue;
					}
					if( !bInSection )
						continue;

					// name=start,end[,lead]   (#SECTION とは並びが違う。名前が = の左にあるため)
					int nEq = str.IndexOf( '=' );
					if( nEq <= 0 )
						continue;
					string strName = str.Substring( 0, nEq ).Trim();
					string[] parts = str.Substring( nEq + 1 ).Split( ',' );
					if( parts.Length < 2 )
						continue;

					STPracticePosition stStart, stEnd;
					if( !STPracticePosition.TryParse( parts[ 0 ], out stStart ) || !STPracticePosition.TryParse( parts[ 1 ], out stEnd ) )
					{
						Trace.TraceWarning( "practice: 区間の位置が読めません。[{0}] {1}", strPath, str );
						continue;
					}

					int nLeadBars = 0;
					if( parts.Length >= 3 )
						int.TryParse( parts[ 2 ].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out nLeadBars );

					var range = new CPracticeRange( strName, stStart, stEnd, nLeadBars );
					range.bIsUserRange = true;
					list.Add( range );
				}
			}
			catch( Exception e )
			{
				Trace.TraceWarning( "practice: {0} が読めません。{1}", strPath, e.Message );
			}

			return list;
		}

		/// <summary>その曲のユーザ区間をまるごと書き直す。同名の区間は上書きになる。</summary>
		public static void tSaveUserRanges( string strChartPath, List<CPracticeRange> listRanges )
		{
			string strKey = strSongKey( strChartPath );
			if( strKey.Length == 0 )
				return;

			string strPath = strUserRangeFilePath;
			if( string.IsNullOrEmpty( strPath ) )
				return;
			var listOut = new List<string>();
			bool bWritten = false;

			try
			{
				if( File.Exists( strPath ) )
				{
					bool bInSection = false;
					foreach( string strRaw in tReadAllLines( strPath ) )
					{
						string str = strRaw.Trim();
						if( str.StartsWith( "[" ) && str.EndsWith( "]" ) )
						{
							bool bMine = str.Substring( 1, str.Length - 2 ).Trim().ToLowerInvariant().Equals( strKey );
							if( bMine )
							{
								bInSection = true;
								tAppendSection( listOut, strKey, listRanges );
								bWritten = true;
								continue;
							}
							bInSection = false;
						}
						if( !bInSection )
							listOut.Add( strRaw );
					}
				}
				else
				{
					listOut.Add( "; DTXManiaNX - practice ranges you saved yourself." );
					listOut.Add( "; One section per song folder; \"name=start,end,leadbars\" per range." );
					listOut.Add( "; Bar numbers are .dtx file bar numbers. See docs/practice-mode.md." );
				}

				if( !bWritten )
				{
					listOut.Add( "" );
					tAppendSection( listOut, strKey, listRanges );
				}

				File.WriteAllLines( strPath, listOut.ToArray(), Encoding.UTF8 );
				Trace.TraceInformation( "practice: {0} に [{1}] の区間を {2} 件保存しました。", strPath, strKey, listRanges.Count );
			}
			catch( Exception e )
			{
				Trace.TraceWarning( "practice: {0} に保存できません。{1}", strPath, e.Message );
			}
		}

		private static void tAppendSection( List<string> listOut, string strKey, List<CPracticeRange> listRanges )
		{
			if( listRanges == null || listRanges.Count == 0 )
				return;
			listOut.Add( "[" + strKey + "]" );
			foreach( CPracticeRange range in listRanges )
			{
				listOut.Add( string.Format( CultureInfo.InvariantCulture, "{0}={1},{2},{3}",
					range.strName.Replace( '=', '-' ).Replace( ',', ' ' ),
					range.stStart.ToString(), range.stEnd.ToString(), range.nLeadBars ) );
			}
		}

		#endregion

		/// <summary>その曲で選べる区間(曲の構成 → ユーザの保存分の順)。</summary>
		public static List<CPracticeRange> tLoadAll( string strChartPath )
		{
			var list = tLoadSongSections( strChartPath );
			list.AddRange( tLoadUserRanges( strChartPath ) );
			return list;
		}

		/// <summary>
		/// .dtx を軽く走査して最大の小節番号を得る。区間を手で入れるときの上限表示に使うだけなので、
		/// 読めなければ 0 を返す。
		/// </summary>
		public static int nReadHighestFileBar( string strChartPath )
		{
			int nMax = 0;
			try
			{
				if( string.IsNullOrEmpty( strChartPath ) || !File.Exists( strChartPath ) )
					return 0;

				foreach( string strRaw in tReadAllLines( strChartPath ) )
				{
					string str = strRaw.TrimStart();
					if( str.Length < 6 || str[ 0 ] != '#' )
						continue;
					int n = CConversion.nConvert3DigitMeasureNumberToNumber( str.Substring( 1, 3 ) );
					if( n > nMax )
						nMax = n;
				}
			}
			catch
			{
				return nMax;
			}
			return nMax;
		}

		/// <summary>
		/// Shift_JIS で読む。BOM が付いていればその通りに読む(UTF-8 を推奨)。
		/// .dtx や set.def と同じ扱い。
		/// </summary>
		private static IEnumerable<string> tReadAllLines( string strPath )
		{
			var enc = Encoding.GetEncoding( "Shift_JIS" );
			using( var sr = new StreamReader( strPath, enc, true ) )
			{
				string str;
				while( ( str = sr.ReadLine() ) != null )
					yield return str;
			}
		}
	}
}
