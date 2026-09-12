using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using FDK;

using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;

namespace DTXMania
{
	/// <summary>
	/// <para>選曲画面に、選択中の譜面＋難易度の名前つきハイスコア (scores.ini) の上位を表示する。</para>
	/// <para>画面左上、インフォメーションとスキルポイントパネルの間の空き領域に置く。</para>
	/// </summary>
	internal class CActSelectHighScorePanel : CActivity
	{
		// プロパティ

		/// <summary>
		/// 1位の名前。記録が無ければ空文字列。(ステータスパネルの "BEST:" 表示が読む)
		/// </summary>
		public string strTopEntryName
		{
			get;
			private set;
		}


		// コンストラクタ

		public CActSelectHighScorePanel()
		{
			base.bNotActivated = true;
		}


		// メソッド

		/// <summary>
		/// 選択曲が変わったときの通知。(難易度変更は毎フレームの監視で拾うので、これは必須ではない)
		/// </summary>
		public void tSelectedSongChanged()
		{
			this.tReloadIfSelectionChanged( true );
		}

		/// <summary>
		/// <para>(選択曲, 難易度スロット, 譜面) が前回と変わっていたら scores.ini を読み直す。</para>
		/// <para>難易度は CActSelectSongList の「アンカ難易度」から算出される値で、HHx2 の難易度変更のほか、
		/// その難易度を持たない曲へスクロールしただけでも変わる。通知漏れを気にせず済むように、
		/// 変更通知ではなく毎フレームの比較で検出する。(比較は参照とintのみ。読み込みは変化時だけ)</para>
		/// </summary>
		private void tReloadIfSelectionChanged( bool bForce )
		{
			CStageSongSelection stage = CDTXMania.stageSongSelection;
			CSongListNode c曲リストノード = ( stage != null ) ? stage.r現在選択中の曲 : null;
			CScore cスコア = ( stage != null ) ? stage.rSelectedScore : null;
			int n難易度 = ( stage != null ) ? stage.nSelectedSongDifficultyLevel : 0;

			if( !bForce &&
				object.ReferenceEquals( c曲リストノード, this.r直前の曲 ) &&
				object.ReferenceEquals( cスコア, this.r直前のスコア ) &&
				( n難易度 == this.n直前の難易度 ) )
				return;

			this.r直前の曲 = c曲リストノード;
			this.r直前のスコア = cスコア;
			this.n直前の難易度 = n難易度;

			this.listEntries = new List<CHighScores.CEntry>();
			this.strTopEntryName = "";
			this.bScoreSelected = false;
			this.strDifficultyLabel = "";
			this.strDifficultyLevel = "";

			if( ( c曲リストノード != null ) && ( cスコア != null ) &&
				( ( c曲リストノード.eNodeType == CSongListNode.ENodeType.SCORE ) || ( c曲リストノード.eNodeType == CSongListNode.ENodeType.SCORE_MIDI ) ) )
			{
				this.bScoreSelected = true;

				if( ( n難易度 >= 0 ) && ( n難易度 < 5 ) && !string.IsNullOrEmpty( c曲リストノード.arDifficultyLabel[ n難易度 ] ) )
					this.strDifficultyLabel = c曲リストノード.arDifficultyLabel[ n難易度 ];

				this.strDifficultyLevel = strLevel表記( cスコア );

				this.listEntries = CHighScores.tLoad(
					CHighScores.strFilePath( cスコア.FileInformation.AbsoluteFilePath ) ).listEntries(
					EInstrumentPart.DRUMS, n難易度 );

				if( this.listEntries.Count > nMaxRows )
					this.listEntries.RemoveRange( nMaxRows, this.listEntries.Count - nMaxRows );

				if( this.listEntries.Count > 0 )
					this.strTopEntryName = this.listEntries[ 0 ].strName;
			}

			this.b表の再生成が必要 = true;
		}

		/// <summary>
		/// ステータスパネルのレベル表示と同じ書式で、ドラムのレベルを返す。
		/// </summary>
		private static string strLevel表記( CScore cスコア )
		{
			int nLevel = ( cスコア.SongInformation.Level.Drums * 10 ) + cスコア.SongInformation.LevelDec.Drums;
			if( nLevel <= 0 )
				return "";

			return ( CDTXMania.ConfigIni.nSkillMode == 0 )
				? string.Format( CultureInfo.InvariantCulture, "{0,2:00}", nLevel / 10 )
				: string.Format( CultureInfo.InvariantCulture, "{0}.{1:00}", nLevel / 100, nLevel % 100 );
		}


		// CActivity 実装

		public override void OnActivate()
		{
			this.listEntries = new List<CHighScores.CEntry>();
			this.strTopEntryName = "";
			this.strDifficultyLabel = "";
			this.strDifficultyLevel = "";
			this.bScoreSelected = false;
			this.b表の再生成が必要 = true;

			// 演奏から戻ってきたときは同じ曲・同じ難易度のまま scores.ini だけが変わっているので、
			// 次のフレームで必ず読み直させる。
			this.r直前の曲 = null;
			this.r直前のスコア = null;
			this.n直前の難易度 = -1;

			base.OnActivate();
		}
		public override void OnManagedCreateResources()
		{
			if( !base.bNotActivated )
			{
				this.prvf見出し = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 11, FontStyle.Regular );
				this.prvf行 = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 10, FontStyle.Regular );
				this.b表の再生成が必要 = true;
				base.OnManagedCreateResources();
			}
		}
		public override void OnManagedReleaseResources()
		{
			if( !base.bNotActivated )
			{
				CDTXMania.tReleaseTexture( ref this.tx表 );
				CDTXMania.t安全にDisposeする( ref this.prvf見出し );
				CDTXMania.t安全にDisposeする( ref this.prvf行 );
				base.OnManagedReleaseResources();
			}
		}
		public override int OnUpdateAndDraw()
		{
			if( base.bNotActivated )
				return 0;

			this.tReloadIfSelectionChanged( false );		// 難易度変更・曲移動をここで拾う。

			if( !this.bScoreSelected || !CDTXMania.ConfigIni.bDrumsEnabled )
				return 0;

			if( this.b表の再生成が必要 )
			{
				this.t表テクスチャを生成する();
				this.b表の再生成が必要 = false;
			}

			if( this.tx表 != null )
				this.tx表.tDraw2D( CDTXMania.app.Device, n本体X, n本体Y );

			return 0;
		}


		// その他

		#region [ private ]
		//-----------------
		// 画面左端の空き領域。上は 5_header panel.png の帯(x 0-250 では y50 まで不透明)、
		// 下はスキルポイントパネル(32,180)-(219,242)、右はプリイメージパネル(x250-)。
		private const int n本体X = 4;
		private const int n本体Y = 52;
		private const int n表の幅 = 242;
		private const int n行の高さ = 19;
		private const int n1行目のY = 25;
		private const int nMaxRows = 5;
		// 10pt での実測: 数字7桁=53px, "100.00%"=55px。各列がぶつからないように決めた値。
		private const int nName最大幅 = 94;
		private const int nScore右端 = 176;
		private const int n見出し右側最大幅 = 124;

		private bool b表の再生成が必要;
		private bool bScoreSelected;
		private int n直前の難易度 = -1;
		private string strDifficultyLabel;
		private string strDifficultyLevel;
		private CSongListNode r直前の曲;
		private CScore r直前のスコア;
		private List<CHighScores.CEntry> listEntries;
		private CPrivateFastFont prvf見出し;
		private CPrivateFastFont prvf行;
		private CTexture tx表;

		private void t表テクスチャを生成する()
		{
			CDTXMania.tReleaseTexture( ref this.tx表 );

			if( ( this.prvf見出し == null ) || ( this.prvf行 == null ) )
				return;

			int nRowCount = ( this.listEntries.Count > 0 ) ? this.listEntries.Count : 1;
			int n表の高さ = n1行目のY + ( nRowCount * n行の高さ ) + 4;

			using( Bitmap bitmap = new Bitmap( n表の幅, n表の高さ ) )
			{
				using( Graphics graphics = Graphics.FromImage( bitmap ) )
				{
					graphics.FillRectangle( new SolidBrush( Color.FromArgb( 176, 0, 0, 0 ) ), 0, 0, bitmap.Width, bitmap.Height );
					graphics.DrawRectangle( new Pen( Color.FromArgb( 255, 150, 150, 150 ), 1f ), 0, 0, bitmap.Width - 1, bitmap.Height - 1 );

					// 左に「何の表か」、右に「いま見ている難易度スロットのラベルとレベル」。
					this.t左寄せで描画する( graphics, this.prvf見出し, "DRUMS BEST", 6, 1, Color.Orange );

					string str難易度 = this.strDifficultyLabel;
					if( this.strDifficultyLevel.Length > 0 )
						str難易度 = ( str難易度.Length > 0 ) ? ( str難易度 + " " + this.strDifficultyLevel ) : ( "Lv " + this.strDifficultyLevel );

					if( str難易度.Length > 0 )
						this.t右寄せで描画する( graphics, this.prvf見出し, str難易度, n表の幅 - 6, 1, Color.Aqua, n見出し右側最大幅 );

					if( this.listEntries.Count == 0 )
					{
						this.t左寄せで描画する( graphics, this.prvf行, "no scores yet", 8, n1行目のY, Color.Silver );
					}
					else
					{
						for( int i = 0; ( i < this.listEntries.Count ) && ( i < nMaxRows ); i++ )
						{
							CHighScores.CEntry entry = this.listEntries[ i ];
							int y = n1行目のY + ( i * n行の高さ );
							Color color = ( i == 0 ) ? Color.Yellow : Color.White;

							this.t左寄せで描画する( graphics, this.prvf行, ( i + 1 ).ToString( CultureInfo.InvariantCulture ) + ".", 6, y, color );
							this.t左寄せで描画する( graphics, this.prvf行, entry.strName, 24, y, color, nName最大幅 );
							this.t右寄せで描画する( graphics, this.prvf行, entry.nScore.ToString( CultureInfo.InvariantCulture ), nScore右端, y, color );
							this.t右寄せで描画する( graphics, this.prvf行, entry.dbAchievement.ToString( "0.00", CultureInfo.InvariantCulture ) + "%", n表の幅 - 6, y, color );
						}
					}
				}
				this.tx表 = CDTXMania.tGenerateTexture( bitmap, false );
			}
		}

		private void t左寄せで描画する( Graphics graphics, CPrivateFastFont prvf, string str, int x, int y, Color color )
		{
			this.t左寄せで描画する( graphics, prvf, str, x, y, color, 0 );
		}

		/// <param name="n最大幅">0 以外なら、これを超える幅の文字列は横に縮めて収める。</param>
		private void t左寄せで描画する( Graphics graphics, CPrivateFastFont prvf, string str, int x, int y, Color color, int n最大幅 )
		{
			if( string.IsNullOrEmpty( str ) )
				return;

			using( Bitmap bitmap = prvf.DrawPrivateFont( str, color, Color.Black ) )
			{
				int nWidth = bitmap.Width;
				if( ( n最大幅 > 0 ) && ( nWidth > n最大幅 ) )
					nWidth = n最大幅;

				graphics.DrawImage( bitmap, x, y, nWidth, bitmap.Height );
			}
		}

		private void t右寄せで描画する( Graphics graphics, CPrivateFastFont prvf, string str, int xRight, int y, Color color )
		{
			this.t右寄せで描画する( graphics, prvf, str, xRight, y, color, 0 );
		}

		/// <param name="n最大幅">0 以外なら、これを超える幅の文字列は横に縮めて収める。</param>
		private void t右寄せで描画する( Graphics graphics, CPrivateFastFont prvf, string str, int xRight, int y, Color color, int n最大幅 )
		{
			if( string.IsNullOrEmpty( str ) )
				return;

			using( Bitmap bitmap = prvf.DrawPrivateFont( str, color, Color.Black ) )
			{
				int nWidth = bitmap.Width;
				if( ( n最大幅 > 0 ) && ( nWidth > n最大幅 ) )
					nWidth = n最大幅;

				graphics.DrawImage( bitmap, xRight - nWidth, y, nWidth, bitmap.Height );
			}
		}
		//-----------------
		#endregion
	}
}
