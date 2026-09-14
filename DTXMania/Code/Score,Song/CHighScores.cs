using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace DTXMania
{
	/// <summary>
	/// <para>名前つきハイスコア表（*.scores.ini）の読み書き。</para>
	/// <para>1つの譜面につき、楽器＋難易度ごとに上位 <see cref="nMaxEntries"/> 件を保持する。</para>
	/// <para>従来の *.score.ini (<see cref="CScoreIni"/>) とは完全に独立しており、
	/// あちらのフォーマットや記録内容には一切手を触れない。</para>
	/// </summary>
	public class CHighScores
	{
		// 定数

		public const int nMaxEntries = 10;
		public const int nMaxNameLength = 12;
		public const string strDefaultName = "PLAYER";

		/// <summary>The letter grade (SS, S, A ... E) for a stored rank value, "-" if unknown.</summary>
		public static string strRankLetter( int nRankValue )
		{
			switch( nRankValue )
			{
				case (int) CScoreIni.ERANK.SS: return "SS";
				case (int) CScoreIni.ERANK.S: return "S";
				case (int) CScoreIni.ERANK.A: return "A";
				case (int) CScoreIni.ERANK.B: return "B";
				case (int) CScoreIni.ERANK.C: return "C";
				case (int) CScoreIni.ERANK.D: return "D";
				case (int) CScoreIni.ERANK.E: return "E";
				default: return "-";
			}
		}


		// 1件分の記録

		public class CEntry
		{
			public string strName;
			public long nScore;
			public int nRankValue;      // CScoreIni.ERANK
			public double dbAchievement;    // 達成率 (0-100)
			public double dbSkill;          // 曲別スキル
			public string strDate;          // yyyy-MM-dd

			public CEntry()
			{
				this.strName = strDefaultName;
				this.nScore = 0;
				this.nRankValue = (int) CScoreIni.ERANK.UNKNOWN;
				this.dbAchievement = 0.0;
				this.dbSkill = 0.0;
				this.strDate = "";
			}
		}


		// メソッド

		/// <summary>
		/// 譜面ファイルのパスから scores.ini のパスを返す。(*.score.ini の隣に置く)
		/// </summary>
		public static string strFilePath( string str譜面ファイルの絶対パス )
		{
			if( string.IsNullOrEmpty( str譜面ファイルの絶対パス ) )
				return null;

			return str譜面ファイルの絶対パス + ".scores.ini";
		}

		/// <summary>
		/// セクション名を返す。(例: "Drums.3")
		/// </summary>
		public static string strSectionName( EInstrumentPart part, int n難易度 )
		{
			string strPart;
			switch( part )
			{
				case EInstrumentPart.GUITAR:
					strPart = "Guitar";
					break;

				case EInstrumentPart.BASS:
					strPart = "Bass";
					break;

				default:
					strPart = "Drums";
					break;
			}
			if( n難易度 < 0 )
				n難易度 = 0;

			return strPart + "." + n難易度.ToString( CultureInfo.InvariantCulture );
		}

		/// <summary>
		/// 名前を記録可能な形に整える。(カンマ・制御文字の除去と長さ制限)
		/// </summary>
		public static string strSanitizeName( string strName )
		{
			if( string.IsNullOrEmpty( strName ) )
				return "";

			StringBuilder builder = new StringBuilder( nMaxNameLength );
			foreach( char ch in strName )
			{
				if( ch == ',' || ch == '\r' || ch == '\n' || ch == '\t' || ch == '[' || ch == ']' || ch == '=' || char.IsControl( ch ) )
					continue;

				builder.Append( ch );
				if( builder.Length >= nMaxNameLength )
					break;
			}
			return builder.ToString().Trim();
		}

		/// <summary>
		/// 指定した楽器＋難易度の記録一覧（スコア降順）のコピーを返す。無ければ空のリスト。
		/// </summary>
		public List<CEntry> listEntries( EInstrumentPart part, int n難易度 )
		{
			List<CEntry> list;
			if( !this.dicSection.TryGetValue( strSectionName( part, n難易度 ), out list ) || list == null )
				return new List<CEntry>();

			return new List<CEntry>( list );
		}

		/// <summary>
		/// 記録を追加する。
		/// </summary>
		/// <returns>入った順位(0開始)。ランク外なら -1。</returns>
		public int tAddEntry( EInstrumentPart part, int n難易度, CEntry entry )
		{
			if( entry == null )
				return -1;

			entry.strName = strSanitizeName( entry.strName );
			if( entry.strName.Length == 0 )
				entry.strName = strDefaultName;

			string strSection = strSectionName( part, n難易度 );
			List<CEntry> list;
			if( !this.dicSection.TryGetValue( strSection, out list ) || list == null )
			{
				list = new List<CEntry>();
				this.dicSection[ strSection ] = list;
			}

			int nIndex = list.Count;
			for( int i = 0; i < list.Count; i++ )
			{
				if( entry.nScore > list[ i ].nScore )
				{
					nIndex = i;
					break;
				}
			}
			if( nIndex >= nMaxEntries )
				return -1;

			list.Insert( nIndex, entry );
			if( list.Count > nMaxEntries )
				list.RemoveRange( nMaxEntries, list.Count - nMaxEntries );

			return nIndex;
		}

		/// <summary>
		/// <para>scores.ini を読み込む。</para>
		/// <para>ファイルが無い場合や壊れている場合でも例外は投げず、読めた分だけを返す。</para>
		/// </summary>
		public static CHighScores tLoad( string strファイル名 )
		{
			CHighScores highScores = new CHighScores();

			try
			{
				if( string.IsNullOrEmpty( strファイル名 ) || !File.Exists( strファイル名 ) )
					return highScores;

				string strSection = "";
				foreach( string strLine元 in File.ReadAllLines( strファイル名, Encoding.UTF8 ) )
				{
					string strLine = ( strLine元 == null ) ? "" : strLine元.Trim();
					if( strLine.Length == 0 || strLine[ 0 ] == ';' || strLine[ 0 ] == '#' )
						continue;

					if( strLine[ 0 ] == '[' )
					{
						int nEnd = strLine.IndexOf( ']' );
						strSection = ( nEnd > 1 ) ? strLine.Substring( 1, nEnd - 1 ).Trim() : "";
						continue;
					}

					if( strSection.Length == 0 )
						continue;

					int nEq = strLine.IndexOf( '=' );
					if( nEq < 0 )
						continue;

					CEntry entry = tParseEntry( strLine.Substring( nEq + 1 ) );
					if( entry == null )
						continue;

					List<CEntry> list;
					if( !highScores.dicSection.TryGetValue( strSection, out list ) || list == null )
					{
						list = new List<CEntry>();
						highScores.dicSection[ strSection ] = list;
					}
					list.Add( entry );
				}

				foreach( List<CEntry> list in highScores.dicSection.Values )
				{
					list.Sort( tCompareEntry );
					if( list.Count > nMaxEntries )
						list.RemoveRange( nMaxEntries, list.Count - nMaxEntries );
				}
			}
			catch( Exception exception )
			{
				Trace.TraceWarning( "scores.ini の読み込みに失敗しました。({0}) {1}", strファイル名, exception.Message );
			}

			return highScores;
		}

		/// <summary>
		/// <para>scores.ini を出力する。</para>
		/// <para>一時ファイルに書いてから置き換えるので、書き込み中に落ちても既存の記録は壊れない。</para>
		/// </summary>
		public void tExport( string strファイル名 )
		{
			if( string.IsNullOrEmpty( strファイル名 ) )
				return;

			string str一時ファイル名 = strファイル名 + ".tmp";
			try
			{
				using( StreamWriter writer = new StreamWriter( str一時ファイル名, false, new UTF8Encoding( false ) ) )
				{
					writer.WriteLine( "; DTXManiaNX named high scores." );
					writer.WriteLine( "; <position>=<name>,<score>,<rank>,<achievement %>,<skill>,<date>" );

					List<string> listSection名 = new List<string>( this.dicSection.Keys );
					listSection名.Sort( StringComparer.Ordinal );

					foreach( string strSection in listSection名 )
					{
						List<CEntry> list = this.dicSection[ strSection ];
						if( list == null || list.Count == 0 )
							continue;

						writer.WriteLine();
						writer.WriteLine( "[{0}]", strSection );

						for( int i = 0; ( i < list.Count ) && ( i < nMaxEntries ); i++ )
						{
							CEntry entry = list[ i ];
							writer.WriteLine( "{0}={1},{2},{3},{4},{5},{6}",
								i + 1,
								strSanitizeName( entry.strName ),
								entry.nScore.ToString( CultureInfo.InvariantCulture ),
								( (CScoreIni.ERANK) entry.nRankValue ).ToString(),
								entry.dbAchievement.ToString( "0.00", CultureInfo.InvariantCulture ),
								entry.dbSkill.ToString( "0.00", CultureInfo.InvariantCulture ),
								entry.strDate );
						}
					}
				}

				if( File.Exists( strファイル名 ) )
				{
					try
					{
						File.Replace( str一時ファイル名, strファイル名, null );
					}
					catch
					{
						// ネットワークドライブ等で File.Replace() が使えない場合の保険。
						File.Delete( strファイル名 );
						File.Move( str一時ファイル名, strファイル名 );
					}
				}
				else
				{
					File.Move( str一時ファイル名, strファイル名 );
				}
			}
			catch( Exception exception )
			{
				Trace.TraceWarning( "scores.ini の出力に失敗しました。({0}) {1}", strファイル名, exception.Message );
				try
				{
					if( File.Exists( str一時ファイル名 ) )
						File.Delete( str一時ファイル名 );
				}
				catch
				{
				}
			}
		}


		// その他

		#region [ private ]
		//-----------------
		private readonly Dictionary<string, List<CEntry>> dicSection = new Dictionary<string, List<CEntry>>();

		private static int tCompareEntry( CEntry x, CEntry y )
		{
			if( x == null )
				return ( y == null ) ? 0 : 1;
			if( y == null )
				return -1;

			int nResult = y.nScore.CompareTo( x.nScore );
			if( nResult != 0 )
				return nResult;

			return y.dbAchievement.CompareTo( x.dbAchievement );
		}

		private static CEntry tParseEntry( string strValue )
		{
			if( strValue == null )
				return null;

			string[] strFields = strValue.Split( ',' );
			if( strFields.Length < 1 )
				return null;

			CEntry entry = new CEntry();
			entry.strName = strSanitizeName( strFields[ 0 ] );
			if( entry.strName.Length == 0 )
				entry.strName = strDefaultName;

			if( strFields.Length > 1 )
				long.TryParse( strFields[ 1 ].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out entry.nScore );

			if( strFields.Length > 2 )
				entry.nRankValue = nParseRank( strFields[ 2 ].Trim() );

			if( strFields.Length > 3 )
				double.TryParse( strFields[ 3 ].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out entry.dbAchievement );

			if( strFields.Length > 4 )
				double.TryParse( strFields[ 4 ].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out entry.dbSkill );

			if( strFields.Length > 5 )
				entry.strDate = strFields[ 5 ].Trim();

			return entry;
		}

		/// <summary>
		/// "SS" のようなランク名でも "0" のような数値でも受け付ける。
		/// </summary>
		private static int nParseRank( string strRank )
		{
			if( string.IsNullOrEmpty( strRank ) )
				return (int) CScoreIni.ERANK.UNKNOWN;

			try
			{
				object objRank = Enum.Parse( typeof( CScoreIni.ERANK ), strRank, true );
				return (int) (CScoreIni.ERANK) objRank;
			}
			catch
			{
			}

			int nRank;
			if( int.TryParse( strRank, NumberStyles.Integer, CultureInfo.InvariantCulture, out nRank ) )
				return nRank;

			return (int) CScoreIni.ERANK.UNKNOWN;
		}
		//-----------------
		#endregion
	}
}
