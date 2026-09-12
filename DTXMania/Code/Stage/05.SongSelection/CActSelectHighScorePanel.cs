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
		/// 選択曲または難易度が変わったときに、その譜面の scores.ini を読み直す。
		/// </summary>
		public void tSelectedSongChanged()
		{
			this.listEntries = new List<CHighScores.CEntry>();
			this.strTopEntryName = "";
			this.bScoreSelected = false;
			this.strDifficultyLabel = "";

			CSongListNode c曲リストノード = CDTXMania.stageSongSelection.r現在選択中の曲;
			CScore cスコア = CDTXMania.stageSongSelection.rSelectedScore;

			if( ( c曲リストノード != null ) && ( cスコア != null ) &&
				( ( c曲リストノード.eNodeType == CSongListNode.ENodeType.SCORE ) || ( c曲リストノード.eNodeType == CSongListNode.ENodeType.SCORE_MIDI ) ) )
			{
				this.bScoreSelected = true;

				int n難易度 = CDTXMania.stageSongSelection.nSelectedSongDifficultyLevel;
				if( ( n難易度 >= 0 ) && ( n難易度 < 5 ) && !string.IsNullOrEmpty( c曲リストノード.arDifficultyLabel[ n難易度 ] ) )
					this.strDifficultyLabel = c曲リストノード.arDifficultyLabel[ n難易度 ];

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


		// CActivity 実装

		public override void OnActivate()
		{
			this.listEntries = new List<CHighScores.CEntry>();
			this.strTopEntryName = "";
			this.strDifficultyLabel = "";
			this.bScoreSelected = false;
			this.b表の再生成が必要 = true;
			base.OnActivate();
		}
		public override void OnManagedCreateResources()
		{
			if( !base.bNotActivated )
			{
				this.prvf見出し = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 13, FontStyle.Regular );
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
			if( base.bNotActivated || !this.bScoreSelected || !CDTXMania.ConfigIni.bDrumsEnabled )
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
		// インフォメーション(4,0)-(244,42) と スキルポイントパネル(32,180)-(219,242) の間の空き領域。
		private const int n本体X = 4;
		private const int n本体Y = 46;
		private const int n表の幅 = 242;
		private const int n行の高さ = 19;
		private const int n1行目のY = 26;
		private const int nMaxRows = 5;
		// 10pt での実測: 数字7桁=53px, "100.00%"=55px。各列がぶつからないように決めた値。
		private const int nName最大幅 = 94;
		private const int nScore右端 = 176;

		private bool b表の再生成が必要;
		private bool bScoreSelected;
		private string strDifficultyLabel;
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
			int n表の高さ = n1行目のY + ( nRowCount * n行の高さ ) + 6;

			using( Bitmap bitmap = new Bitmap( n表の幅, n表の高さ ) )
			{
				using( Graphics graphics = Graphics.FromImage( bitmap ) )
				{
					graphics.FillRectangle( new SolidBrush( Color.FromArgb( 176, 0, 0, 0 ) ), 0, 0, bitmap.Width, bitmap.Height );
					graphics.DrawRectangle( new Pen( Color.FromArgb( 255, 150, 150, 150 ), 1f ), 0, 0, bitmap.Width - 1, bitmap.Height - 1 );

					string str見出し = ( this.strDifficultyLabel.Length > 0 )
						? "DRUMS BEST - " + this.strDifficultyLabel
						: "DRUMS BEST";
					this.t左寄せで描画する( graphics, this.prvf見出し, str見出し, 6, 2, Color.Orange );

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
			if( string.IsNullOrEmpty( str ) )
				return;

			using( Bitmap bitmap = prvf.DrawPrivateFont( str, color, Color.Black ) )
			{
				graphics.DrawImage( bitmap, xRight - bitmap.Width, y, bitmap.Width, bitmap.Height );
			}
		}
		//-----------------
		#endregion
	}
}
