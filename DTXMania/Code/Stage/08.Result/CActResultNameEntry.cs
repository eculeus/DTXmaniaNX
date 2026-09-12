using System;
using System.Drawing;
using System.Text;
using FDK;

using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using SlimDXKey = SlimDX.DirectInput.Key;

namespace DTXMania
{
	/// <summary>
	/// <para>リザルト画面で、名前つきハイスコア (scores.ini) 用の名前を入力させる。</para>
	/// <para>キーボードから直接 A-Z / 0-9 / SPACE / '-' を受け取る簡易入力。
	/// (BACKSPACE=1文字削除, ENTER=確定, ESC=記録しない)</para>
	/// </summary>
	internal class CActResultNameEntry : CActivity
	{
		// プロパティ

		/// <summary>
		/// 入力中なら true。true の間、リザルト画面側の通常のキー入力は止めること。
		/// </summary>
		public bool bIsInputting
		{
			get;
			private set;
		}

		/// <summary>
		/// ESC で打ち切られたなら true。
		/// </summary>
		public bool bIsCancelled
		{
			get;
			private set;
		}

		/// <summary>
		/// 確定した名前。(空文字列なら呼び出し側で "PLAYER" 扱いにする)
		/// </summary>
		public string strConfirmedName
		{
			get;
			private set;
		}

		/// <summary>
		/// 入力が終了した直後の1回だけ true を返す。(CActTextBox と同じ流儀)
		/// </summary>
		public bool bInputJustFinished
		{
			get
			{
				bool bResult = this.b入力終了直後;
				this.b入力終了直後 = false;
				return bResult;
			}
		}


		// コンストラクタ

		public CActResultNameEntry()
		{
			base.bNotActivated = true;
		}


		// メソッド

		/// <summary>
		/// 名前入力を開始する。
		/// </summary>
		public void tStartInput( string str初期名 )
		{
			this.str入力中の名前 = CHighScores.strSanitizeName( str初期名 );
			this.bIsInputting = true;
			this.bIsCancelled = false;
			this.b入力終了直後 = false;
			this.strConfirmedName = "";
			this.b名前テクスチャの再生成が必要 = true;
		}


		// CActivity 実装

		public override void OnActivate()
		{
			this.n本体X = 340;
			this.n本体Y = 250;
			this.n本体W = 600;
			this.n本体H = 190;
			this.str入力中の名前 = "";
			this.strConfirmedName = "";
			this.bIsInputting = false;
			this.bIsCancelled = false;
			this.b入力終了直後 = false;
			this.b名前テクスチャの再生成が必要 = true;
			base.OnActivate();
		}
		public override void OnDeactivate()
		{
			this.ctカーソル点滅用 = null;
			base.OnDeactivate();
		}
		public override void OnManagedCreateResources()
		{
			if( !base.bNotActivated )
			{
				this.prvf名前 = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 32, FontStyle.Regular );
				this.prvf説明 = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 16, FontStyle.Regular );

				#region [ 背景パネル ]
				using( Bitmap bitmap = new Bitmap( this.n本体W, this.n本体H ) )
				{
					using( Graphics graphics = Graphics.FromImage( bitmap ) )
					{
						graphics.FillRectangle( new SolidBrush( Color.FromArgb( 224, 0, 0, 0 ) ), 0, 0, bitmap.Width, bitmap.Height );
						graphics.DrawRectangle( new Pen( Color.FromArgb( 255, 200, 200, 200 ), 2f ), 1, 1, bitmap.Width - 3, bitmap.Height - 3 );
					}
					this.tx背景 = CDTXMania.tGenerateTexture( bitmap, false );
				}
				#endregion

				#region [ 見出しと説明 ]
				this.tx見出し = this.txFromString( this.prvf説明, "ENTER YOUR NAME FOR THE HIGH SCORE TABLE" );
				this.tx説明1 = this.txFromString( this.prvf説明, "A-Z 0-9 SPACE  : type      BACKSPACE : erase" );
				this.tx説明2 = this.txFromString( this.prvf説明, "ENTER : save (empty name = PLAYER)      ESC : skip" );
				#endregion

				#region [ カーソル ]
				using( Bitmap bitmap = new Bitmap( 4, 36 ) )
				{
					using( Graphics graphics = Graphics.FromImage( bitmap ) )
					{
						graphics.FillRectangle( Brushes.White, 0, 0, bitmap.Width, bitmap.Height );
					}
					this.txカーソル = CDTXMania.tGenerateTexture( bitmap, false );
				}
				#endregion

				this.b名前テクスチャの再生成が必要 = true;
				base.OnManagedCreateResources();
			}
		}
		public override void OnManagedReleaseResources()
		{
			if( !base.bNotActivated )
			{
				CDTXMania.tReleaseTexture( ref this.tx背景 );
				CDTXMania.tReleaseTexture( ref this.tx見出し );
				CDTXMania.tReleaseTexture( ref this.tx説明1 );
				CDTXMania.tReleaseTexture( ref this.tx説明2 );
				CDTXMania.tReleaseTexture( ref this.txカーソル );
				CDTXMania.tReleaseTexture( ref this.tx名前 );
				CDTXMania.t安全にDisposeする( ref this.prvf名前 );
				CDTXMania.t安全にDisposeする( ref this.prvf説明 );
				base.OnManagedReleaseResources();
			}
		}
		public override int OnUpdateAndDraw()
		{
			if( base.bNotActivated || !this.bIsInputting )
				return 0;

			if( this.ctカーソル点滅用 == null )
				this.ctカーソル点滅用 = new CCounter( 0, 1000, 1, CDTXMania.Timer );

			this.t入力を処理する();

			if( !this.bIsInputting )		// 上の処理で確定/中止した場合は、もう描画しない。
				return 0;

			if( this.b名前テクスチャの再生成が必要 )
			{
				this.t名前テクスチャを生成する();
				this.b名前テクスチャの再生成が必要 = false;
			}

			#region [ 描画 ]
			if( this.tx背景 != null )
				this.tx背景.tDraw2D( CDTXMania.app.Device, this.n本体X, this.n本体Y );

			if( this.tx見出し != null )
				this.tx見出し.tDraw2D( CDTXMania.app.Device, this.n本体X + 24, this.n本体Y + 16 );

			int nNameX = this.n本体X + 40;
			int nNameY = this.n本体Y + 56;
			if( this.tx名前 != null )
			{
				this.tx名前.tDraw2D( CDTXMania.app.Device, nNameX, nNameY );
				nNameX += this.tx名前.szImageSize.Width;
			}

			this.ctカーソル点滅用.tUpdateLoop();
			if( ( this.txカーソル != null ) && ( this.ctカーソル点滅用.nCurrentValue <= 500 ) )
				this.txカーソル.tDraw2D( CDTXMania.app.Device, nNameX, nNameY + 6 );

			if( this.tx説明1 != null )
				this.tx説明1.tDraw2D( CDTXMania.app.Device, this.n本体X + 24, this.n本体Y + 122 );

			if( this.tx説明2 != null )
				this.tx説明2.tDraw2D( CDTXMania.app.Device, this.n本体X + 24, this.n本体Y + 148 );
			#endregion

			return 0;
		}


		// その他

		#region [ private ]
		//-----------------
		private int n本体X;
		private int n本体Y;
		private int n本体W;
		private int n本体H;
		private bool b入力終了直後;
		private bool b名前テクスチャの再生成が必要;
		private string str入力中の名前;
		private CCounter ctカーソル点滅用;
		private CPrivateFastFont prvf名前;
		private CPrivateFastFont prvf説明;
		private CTexture tx背景;
		private CTexture tx見出し;
		private CTexture tx説明1;
		private CTexture tx説明2;
		private CTexture txカーソル;
		private CTexture tx名前;

		private CTexture txFromString( CPrivateFastFont prvf, string str )
		{
			if( ( prvf == null ) || string.IsNullOrEmpty( str ) )
				return null;

			using( Bitmap bitmap = prvf.DrawPrivateFont( str, Color.White, Color.Black ) )
			{
				return CDTXMania.tGenerateTexture( bitmap, false );
			}
		}

		private void t名前テクスチャを生成する()
		{
			CDTXMania.tReleaseTexture( ref this.tx名前 );
			if( this.str入力中の名前.Length > 0 )
				this.tx名前 = this.txFromString( this.prvf名前, this.str入力中の名前 );

			if( this.ctカーソル点滅用 != null )
				this.ctカーソル点滅用.nCurrentValue = 0;
		}

		private void t1文字追加する( char ch )
		{
			if( this.str入力中の名前.Length >= CHighScores.nMaxNameLength )
				return;

			this.str入力中の名前 += ch;
			this.b名前テクスチャの再生成が必要 = true;
			CDTXMania.Skin.soundChange.tPlay();
		}

		private void t入力を処理する()
		{
			var keyboard = CDTXMania.InputManager.Keyboard;
			if( keyboard == null )
				return;

			#region [ 確定 / 中止 ]
			if( keyboard.bKeyPressed( (int) SlimDXKey.Escape ) )
			{
				this.bIsInputting = false;
				this.bIsCancelled = true;
				this.b入力終了直後 = true;
				this.strConfirmedName = "";
				CDTXMania.Skin.soundCancel.tPlay();
				return;
			}
			if( keyboard.bKeyPressed( (int) SlimDXKey.Return ) || keyboard.bKeyPressed( (int) SlimDXKey.NumberPadEnter ) )
			{
				this.bIsInputting = false;
				this.bIsCancelled = false;
				this.b入力終了直後 = true;
				this.strConfirmedName = CHighScores.strSanitizeName( this.str入力中の名前 );
				CDTXMania.Skin.soundDecide.tPlay();
				return;
			}
			#endregion

			#region [ 1文字削除 ]
			if( keyboard.bKeyPressed( (int) SlimDXKey.Backspace ) || keyboard.bKeyPressed( (int) SlimDXKey.Delete ) )
			{
				if( this.str入力中の名前.Length > 0 )
				{
					this.str入力中の名前 = this.str入力中の名前.Substring( 0, this.str入力中の名前.Length - 1 );
					this.b名前テクスチャの再生成が必要 = true;
					CDTXMania.Skin.soundCursorMovement.tPlay();
				}
				return;
			}
			#endregion

			#region [ 文字入力 ]
			for( int i = 0; i < 26; i++ )
			{
				if( keyboard.bKeyPressed( (int) SlimDXKey.A + i ) )
				{
					this.t1文字追加する( (char) ( 'A' + i ) );
					return;
				}
			}
			for( int i = 0; i < 10; i++ )
			{
				if( keyboard.bKeyPressed( (int) SlimDXKey.D0 + i ) || keyboard.bKeyPressed( (int) SlimDXKey.NumberPad0 + i ) )
				{
					this.t1文字追加する( (char) ( '0' + i ) );
					return;
				}
			}
			if( keyboard.bKeyPressed( (int) SlimDXKey.Space ) )
			{
				if( this.str入力中の名前.Length > 0 )		// 先頭の空白は受け付けない。
					this.t1文字追加する( ' ' );
				return;
			}
			if( keyboard.bKeyPressed( (int) SlimDXKey.Minus ) || keyboard.bKeyPressed( (int) SlimDXKey.NumberPadMinus ) )
			{
				this.t1文字追加する( '-' );
				return;
			}
			#endregion
		}
		//-----------------
		#endregion
	}
}
