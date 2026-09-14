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
	/// リザルト画面に、その譜面の名前つきハイスコア表 (scores.ini) を表示する。
	/// </summary>
	internal class CActResultHighScorePanel : CActivity
	{
		// プロパティ

		/// <summary>
		/// 表を表示するなら true。
		/// </summary>
		public bool bIsVisible
		{
			get;
			set;
		}


		// コンストラクタ

		public CActResultHighScorePanel()
		{
			base.bNotActivated = true;
		}


		// メソッド

		/// <summary>
		/// 表示する記録一覧を設定する。
		/// </summary>
		/// <param name="listEntries">スコア降順の記録一覧。</param>
		/// <param name="nHighlightIndex">今回の記録の順位(0開始)。無ければ -1。</param>
		public void tSetEntries( List<CHighScores.CEntry> listEntries, int nHighlightIndex )
		{
			this.listEntries = ( listEntries == null ) ? new List<CHighScores.CEntry>() : listEntries;
			this.nHighlightIndex = nHighlightIndex;
			this.b表の再生成が必要 = true;
		}


		// CActivity 実装

		public override void OnActivate()
		{
			this.n本体X = 850;
			this.n本体Y = 296;
			this.listEntries = new List<CHighScores.CEntry>();
			this.nHighlightIndex = -1;
			this.b表の再生成が必要 = true;
			base.OnActivate();
		}
		public override void OnManagedCreateResources()
		{
			if( !base.bNotActivated )
			{
				this.prvf行 = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 15, FontStyle.Regular );
				this.prvf見出し = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 17, FontStyle.Regular );
				this.b表の再生成が必要 = true;
				base.OnManagedCreateResources();
			}
		}
		public override void OnManagedReleaseResources()
		{
			if( !base.bNotActivated )
			{
				CDTXMania.tReleaseTexture( ref this.tx表 );
				CDTXMania.t安全にDisposeする( ref this.prvf行 );
				CDTXMania.t安全にDisposeする( ref this.prvf見出し );
				base.OnManagedReleaseResources();
			}
		}
		public override int OnUpdateAndDraw()
		{
			if( base.bNotActivated || !this.bIsVisible )
				return 0;

			if( this.b表の再生成が必要 )
			{
				this.t表テクスチャを生成する();
				this.b表の再生成が必要 = false;
			}

			if( this.tx表 != null )
				this.tx表.tDraw2D( CDTXMania.app.Device, this.n本体X, this.n本体Y );

			return 0;
		}


		// その他

		#region [ private ]
		//-----------------
		private const int n表の幅 = 420;
		private const int n行の高さ = 23;
		private const int n1行目のY = 38;

		private int n本体X;
		private int n本体Y;
		private bool b表の再生成が必要;
		private int nHighlightIndex;
		private List<CHighScores.CEntry> listEntries;
		private CPrivateFastFont prvf行;
		private CPrivateFastFont prvf見出し;
		private CTexture tx表;

		private void t表テクスチャを生成する()
		{
			CDTXMania.tReleaseTexture( ref this.tx表 );

			if( ( this.prvf行 == null ) || ( this.prvf見出し == null ) )
				return;

			int nRowCount = ( this.listEntries.Count > 0 ) ? this.listEntries.Count : 1;
			if( nRowCount > CHighScores.nMaxEntries )
				nRowCount = CHighScores.nMaxEntries;

			int n表の高さ = n1行目のY + ( nRowCount * n行の高さ ) + 10;

			using( Bitmap bitmap = new Bitmap( n表の幅, n表の高さ ) )
			{
				using( Graphics graphics = Graphics.FromImage( bitmap ) )
				{
					graphics.FillRectangle( new SolidBrush( Color.FromArgb( 176, 0, 0, 0 ) ), 0, 0, bitmap.Width, bitmap.Height );
					graphics.DrawRectangle( new Pen( Color.FromArgb( 255, 160, 160, 160 ), 1f ), 0, 0, bitmap.Width - 1, bitmap.Height - 1 );

					this.t左寄せで描画する( graphics, this.prvf見出し, "HIGH SCORES", 10, 6, Color.Orange );

					if( this.listEntries.Count == 0 )
					{
						this.t左寄せで描画する( graphics, this.prvf行, "NO NAMED RECORDS YET", 10, n1行目のY, Color.Silver );
					}
					else
					{
						for( int i = 0; ( i < this.listEntries.Count ) && ( i < CHighScores.nMaxEntries ); i++ )
						{
							CHighScores.CEntry entry = this.listEntries[ i ];
							int y = n1行目のY + ( i * n行の高さ );
							Color color = ( i == this.nHighlightIndex ) ? Color.Yellow : Color.White;

							this.t左寄せで描画する( graphics, this.prvf行, string.Format( CultureInfo.InvariantCulture, "{0,2}", i + 1 ), 10, y, color );
							this.t左寄せで描画する( graphics, this.prvf行, entry.strName, 46, y, color );
							this.t右寄せで描画する( graphics, this.prvf行, entry.nScore.ToString( CultureInfo.InvariantCulture ), 280, y, color );
							this.t左寄せで描画する( graphics, this.prvf行, CHighScores.strRankLetter( entry.nRankValue ), 292, y, color );
							this.t右寄せで描画する( graphics, this.prvf行, entry.dbAchievement.ToString( "0.00", CultureInfo.InvariantCulture ) + "%", n表の幅 - 10, y, color );
						}
					}
				}
				this.tx表 = CDTXMania.tGenerateTexture( bitmap, false );
			}
		}

		private void t左寄せで描画する( Graphics graphics, CPrivateFastFont prvf, string str, int x, int y, Color color )
		{
			if( string.IsNullOrEmpty( str ) )
				return;

			using( Bitmap bitmap = prvf.DrawPrivateFont( str, color, Color.Black ) )
			{
				graphics.DrawImage( bitmap, x, y, bitmap.Width, bitmap.Height );
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
