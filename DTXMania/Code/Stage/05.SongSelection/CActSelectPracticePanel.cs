using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using FDK;

using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using SlimDXKey = SlimDX.DirectInput.Key;

namespace DTXMania
{
	/// <summary>
	/// <para>選曲画面の PRACTICE パネル。演奏の前にループさせたい区間を選ぶ。</para>
	/// <para>曲フォルダの sections.def にある曲の構成と、ユーザが自分で保存した区間
	/// (PracticeRanges.ini) を一覧にして、その場で小節番号を入れて名前をつけて保存もできる。
	/// 選んだ区間は CDTXMania.rPracticeRange に入り、演奏画面がそれを見てループを組む。</para>
	/// <para>詳細は docs/practice-mode.md を参照。</para>
	/// </summary>
	internal class CActSelectPracticePanel : CActivity
	{
		/// <summary>パネルを開いている間 true。選曲画面は他のキーを止める。</summary>
		public bool bIsActivePopupMenu
		{
			get;
			private set;
		}

		public CActSelectPracticePanel()
		{
			base.bNotActivated = true;
		}

		#region [ 開く・閉じる ]

		public void tActivatePopupMenu()
		{
			this.strChartPath = strCurrentChartPath();
			this.listRanges = CPracticeSections.tLoadAll( this.strChartPath );
			this.nMaxFileBar = CPracticeSections.nReadHighestFileBar( this.strChartPath );

			this.ePhase = EPhase.List;
			this.nCursor = 0;
			this.nEditCursor = 0;
			this.strTypeBuffer = "";
			this.strEditName = "";
			this.strMessage = "";

			// いま選ばれている区間があればその行にカーソルを置く。
			CPracticeRange rActive = CDTXMania.rPracticeRange;
			if( rActive != null )
			{
				for( int i = 0; i < this.listRanges.Count; i++ )
				{
					if( this.listRanges[ i ].strName.Equals( rActive.strName, StringComparison.OrdinalIgnoreCase ) )
					{
						this.nCursor = i + 1;		// 0 行目は OFF
						break;
					}
				}
			}

			this.bIsActivePopupMenu = true;
			this.bRedraw = true;
		}

		public void tDeativatePopupMenu()
		{
			this.bIsActivePopupMenu = false;
			this.bRedraw = true;
		}

		/// <summary>
		/// 選曲が変わったら、その曲のものでない区間選択は捨てる。
		/// ただし演奏から戻ってきた直後(まだ曲を覚えていないとき)は捨てない。同じ区間をもう一度
		/// 演奏したいのが普通なので、選び直させないため。
		/// </summary>
		public void tSelectedSongChanged()
		{
			string str = strCurrentChartPath();
			if( !string.Equals( str, this.strChartPath, StringComparison.OrdinalIgnoreCase ) )
			{
				if( this.strChartPath.Length > 0 )
					CDTXMania.rPracticeRange = null;
				this.strChartPath = str;
				this.bRedraw = true;
			}
		}

		private static string strCurrentChartPath()
		{
			CStageSongSelection stage = CDTXMania.stageSongSelection;
			CSongListNode node = ( stage != null ) ? stage.r現在選択中の曲 : null;
			CScore score = ( stage != null ) ? stage.rSelectedScore : null;

			if( ( node == null ) || ( score == null ) )
				return "";
			if( ( node.eNodeType != CSongListNode.ENodeType.SCORE ) && ( node.eNodeType != CSongListNode.ENodeType.SCORE_MIDI ) )
				return "";

			return score.FileInformation.AbsoluteFilePath ?? "";
		}

		#endregion

		#region [ CActivity 実装 ]

		public override void OnActivate()
		{
			this.bIsActivePopupMenu = false;
			this.listRanges = new List<CPracticeRange>();
			this.strChartPath = "";
			this.strMessage = "";
			this.bRedraw = true;
			base.OnActivate();
		}
		public override void OnManagedCreateResources()
		{
			if( !base.bNotActivated )
			{
				this.prvf見出し = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 15, FontStyle.Bold );
				this.prvf行 = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 12, FontStyle.Regular );
				this.prvf小 = new CPrivateFastFont( new FontFamily( CDTXMania.ConfigIni.str選曲リストフォント ), 10, FontStyle.Regular );
				this.bRedraw = true;
				base.OnManagedCreateResources();
			}
		}
		public override void OnManagedReleaseResources()
		{
			if( !base.bNotActivated )
			{
				CDTXMania.tReleaseTexture( ref this.txPanel );
				CDTXMania.tReleaseTexture( ref this.txIndicator );
				CDTXMania.t安全にDisposeする( ref this.prvf見出し );
				CDTXMania.t安全にDisposeする( ref this.prvf行 );
				CDTXMania.t安全にDisposeする( ref this.prvf小 );
				base.OnManagedReleaseResources();
			}
		}

		public override int OnUpdateAndDraw()
		{
			throw new InvalidOperationException( "tUpdateAndDraw() のほうを使用してください。" );
		}

		/// <summary>
		/// 選曲画面から毎フレーム呼ぶ。開いていれば入力を処理してパネルを描き、
		/// 閉じていれば画面の隅に今の区間を小さく出すだけ。
		/// </summary>
		public int tUpdateAndDraw()
		{
			if( base.bNotActivated )
				return 0;

			if( this.bIsActivePopupMenu )
			{
				this.tHandleKeyInput();

				if( this.bRedraw )
				{
					this.tGeneratePanelTexture();
					this.bRedraw = false;
				}
				if( this.txPanel != null )
					this.txPanel.tDraw2D( CDTXMania.app.Device, nPanelX, nPanelY );
			}
			else
			{
				this.tDrawIndicator();
			}

			return 0;
		}

		#endregion

		#region [ 入力 ]

		private void tHandleKeyInput()
		{
			IInputDevice keyboard = CDTXMania.InputManager.Keyboard;
			if( keyboard == null )
				return;

			switch( this.ePhase )
			{
				case EPhase.List:
					this.tHandleKeyInput_List( keyboard );
					break;

				case EPhase.EditRange:
					this.tHandleKeyInput_EditRange( keyboard );
					break;

				case EPhase.EditName:
					this.tHandleKeyInput_EditName( keyboard );
					break;
			}
		}

		private void tHandleKeyInput_List( IInputDevice keyboard )
		{
			int nRows = this.listRanges.Count + 2;		// OFF + 区間 + "New range..."

			if( bPressedUp( keyboard ) )
			{
				this.nCursor = ( this.nCursor + nRows - 1 ) % nRows;
				CDTXMania.Skin.soundCursorMovement.tPlay();
				this.bRedraw = true;
			}
			else if( bPressedDown( keyboard ) )
			{
				this.nCursor = ( this.nCursor + 1 ) % nRows;
				CDTXMania.Skin.soundCursorMovement.tPlay();
				this.bRedraw = true;
			}
			else if( keyboard.bKeyPressed( (int) SlimDXKey.Delete ) )
			{
				int nIndex = this.nCursor - 1;
				if( ( nIndex >= 0 ) && ( nIndex < this.listRanges.Count ) && this.listRanges[ nIndex ].bIsUserRange )
				{
					CPracticeRange rDeleted = this.listRanges[ nIndex ];
					this.listRanges.RemoveAt( nIndex );
					this.tSaveUserRanges();
					if( ( CDTXMania.rPracticeRange != null ) && object.ReferenceEquals( CDTXMania.rPracticeRange, rDeleted ) )
						CDTXMania.rPracticeRange = null;
					this.strMessage = "deleted \"" + rDeleted.strName + "\"";
					if( this.nCursor >= this.listRanges.Count + 2 )
						this.nCursor = this.listRanges.Count + 1;
					CDTXMania.Skin.soundCancel.tPlay();
					this.bRedraw = true;
				}
			}
			else if( bPressedDecide( keyboard ) )
			{
				if( this.nCursor == 0 )
				{
					CDTXMania.rPracticeRange = null;
					CDTXMania.Skin.soundDecide.tPlay();
					this.tDeativatePopupMenu();
				}
				else if( this.nCursor == nRows - 1 )
				{
					// New range...
					this.tStartNewRange();
					CDTXMania.Skin.soundDecide.tPlay();
					this.bRedraw = true;
				}
				else
				{
					CDTXMania.rPracticeRange = this.listRanges[ this.nCursor - 1 ];
					CDTXMania.Skin.soundDecide.tPlay();
					this.tDeativatePopupMenu();
				}
			}
			else if( bPressedCancel( keyboard ) )
			{
				CDTXMania.Skin.soundCancel.tPlay();
				this.tDeativatePopupMenu();
			}
		}

		private void tStartNewRange()
		{
			int nEnd = ( this.nMaxFileBar > 0 ) ? Math.Min( 8, this.nMaxFileBar + 1 ) : 8;
			this.rEditing = new CPracticeRange( "", STPracticePosition.FromBar( 0, 1.0 ), STPracticePosition.FromBar( nEnd, 1.0 ), 0 );
			this.rEditing.bIsUserRange = true;
			this.ePhase = EPhase.EditRange;
			this.nEditCursor = 0;
			this.strTypeBuffer = "";
			this.strMessage = "";
		}

		private void tHandleKeyInput_EditRange( IInputDevice keyboard )
		{
			const int nEditRows = 5;		// START / END / LEAD / USE NOW / NAME & SAVE

			if( bPressedCancel( keyboard ) )
			{
				CDTXMania.Skin.soundCancel.tPlay();
				this.ePhase = EPhase.List;
				this.bRedraw = true;
				return;
			}
			if( bPressedUp( keyboard ) )
			{
				this.nEditCursor = ( this.nEditCursor + nEditRows - 1 ) % nEditRows;
				this.strTypeBuffer = "";
				CDTXMania.Skin.soundCursorMovement.tPlay();
				this.bRedraw = true;
				return;
			}
			if( bPressedDown( keyboard ) )
			{
				this.nEditCursor = ( this.nEditCursor + 1 ) % nEditRows;
				this.strTypeBuffer = "";
				CDTXMania.Skin.soundCursorMovement.tPlay();
				this.bRedraw = true;
				return;
			}

			if( this.nEditCursor <= 2 )
			{
				int nDelta = 0;
				if( keyboard.bKeyPressed( (int) SlimDXKey.LeftArrow ) )
					nDelta = -1;
				else if( keyboard.bKeyPressed( (int) SlimDXKey.RightArrow ) )
					nDelta = +1;

				if( nDelta != 0 )
				{
					this.tAdjustEditValue( this.nEditCursor, nDelta );
					this.strTypeBuffer = "";
					CDTXMania.Skin.soundChange.tPlay();
					this.bRedraw = true;
					return;
				}

				int nDigit = nTypedDigit( keyboard );
				if( nDigit >= 0 )
				{
					if( this.strTypeBuffer.Length >= 3 )
						this.strTypeBuffer = "";
					this.strTypeBuffer += nDigit.ToString( CultureInfo.InvariantCulture );
					int nValue;
					if( int.TryParse( this.strTypeBuffer, NumberStyles.Integer, CultureInfo.InvariantCulture, out nValue ) )
						this.tSetEditValue( this.nEditCursor, nValue );
					this.bRedraw = true;
					return;
				}
				if( keyboard.bKeyPressed( (int) SlimDXKey.Backspace ) )
				{
					if( this.strTypeBuffer.Length > 0 )
						this.strTypeBuffer = this.strTypeBuffer.Substring( 0, this.strTypeBuffer.Length - 1 );
					int nValue = 0;
					int.TryParse( this.strTypeBuffer, NumberStyles.Integer, CultureInfo.InvariantCulture, out nValue );
					this.tSetEditValue( this.nEditCursor, nValue );
					this.bRedraw = true;
					return;
				}
			}

			if( bPressedDecide( keyboard ) )
			{
				if( !this.bEditingRangeIsValid() )
				{
					this.strMessage = "end must be after start";
					CDTXMania.Skin.soundCancel.tPlay();
					this.bRedraw = true;
					return;
				}

				if( this.nEditCursor == 4 )
				{
					// NAME & SAVE
					this.strEditName = "";
					this.ePhase = EPhase.EditName;
					CDTXMania.Skin.soundDecide.tPlay();
					this.bRedraw = true;
				}
				else
				{
					// USE NOW (値の行で Enter を押したときもこれ)
					this.rEditing.strName = "custom " + this.rEditing.strRangeText;
					CDTXMania.rPracticeRange = this.rEditing;
					CDTXMania.Skin.soundDecide.tPlay();
					this.tDeativatePopupMenu();
				}
			}
		}

		private void tHandleKeyInput_EditName( IInputDevice keyboard )
		{
			// ここは文字入力なので、パッドは見ない。LC は既定で A と Z に割り当たっているので、
			// パッドを見ると名前に A を打っただけで取り消しになってしまう。
			if( keyboard.bKeyPressed( (int) SlimDXKey.Escape ) )
			{
				CDTXMania.Skin.soundCancel.tPlay();
				this.ePhase = EPhase.EditRange;
				this.bRedraw = true;
				return;
			}
			if( keyboard.bKeyPressed( (int) SlimDXKey.Backspace ) )
			{
				if( this.strEditName.Length > 0 )
					this.strEditName = this.strEditName.Substring( 0, this.strEditName.Length - 1 );
				this.bRedraw = true;
				return;
			}
			if( keyboard.bKeyPressed( (int) SlimDXKey.Return ) || keyboard.bKeyPressed( (int) SlimDXKey.NumberPadEnter ) )
			{
				string strName = this.strEditName.Trim();
				if( strName.Length == 0 )
					strName = "custom " + this.rEditing.strRangeText;

				this.rEditing.strName = strName;
				this.rEditing.bIsUserRange = true;

				// 同じ名前があれば上書き。
				for( int i = this.listRanges.Count - 1; i >= 0; i-- )
				{
					if( this.listRanges[ i ].bIsUserRange && this.listRanges[ i ].strName.Equals( strName, StringComparison.OrdinalIgnoreCase ) )
						this.listRanges.RemoveAt( i );
				}
				this.listRanges.Add( this.rEditing );
				this.tSaveUserRanges();

				CDTXMania.rPracticeRange = this.rEditing;
				CDTXMania.Skin.soundDecide.tPlay();
				this.tDeativatePopupMenu();
				return;
			}

			if( this.strEditName.Length < nMaxNameLength )
			{
				char ch = chTypedNameChar( keyboard );
				if( ch != '\0' )
				{
					this.strEditName += ch;
					this.bRedraw = true;
				}
			}
		}

		// ポップアップメニューと同じ操作でパッドからも動かせるようにしておく。
		private static bool bPressedUp( IInputDevice keyboard )
		{
			return keyboard.bKeyPressed( (int) SlimDXKey.UpArrow )
				|| CDTXMania.Pad.bPressed( EInstrumentPart.DRUMS, EPad.HT )
				|| CDTXMania.Pad.bPressedGB( EPad.R );
		}
		private static bool bPressedDown( IInputDevice keyboard )
		{
			return keyboard.bKeyPressed( (int) SlimDXKey.DownArrow )
				|| CDTXMania.Pad.bPressed( EInstrumentPart.DRUMS, EPad.LT )
				|| CDTXMania.Pad.bPressedGB( EPad.G );
		}
		private static bool bPressedDecide( IInputDevice keyboard )
		{
			return keyboard.bKeyPressed( (int) SlimDXKey.Return )
				|| keyboard.bKeyPressed( (int) SlimDXKey.NumberPadEnter )
				|| CDTXMania.Pad.bPressedDGB( EPad.Decide )
				|| CDTXMania.Pad.bPressed( EInstrumentPart.DRUMS, EPad.RD );
		}
		private static bool bPressedCancel( IInputDevice keyboard )
		{
			return keyboard.bKeyPressed( (int) SlimDXKey.Escape )
				|| CDTXMania.Pad.bPressed( EInstrumentPart.DRUMS, EPad.LC )
				|| CDTXMania.Pad.bPressedGB( EPad.Cancel );
		}

		private static int nTypedDigit( IInputDevice keyboard )
		{
			for( int i = 0; i <= 9; i++ )
			{
				if( keyboard.bKeyPressed( (int) SlimDXKey.D0 + i ) || keyboard.bKeyPressed( (int) SlimDXKey.NumberPad0 + i ) )
					return i;
			}
			return -1;
		}

		private static char chTypedNameChar( IInputDevice keyboard )
		{
			for( int i = 0; i < 26; i++ )
			{
				if( keyboard.bKeyPressed( (int) SlimDXKey.A + i ) )
					return (char) ( 'A' + i );
			}
			int nDigit = nTypedDigit( keyboard );
			if( nDigit >= 0 )
				return (char) ( '0' + nDigit );
			if( keyboard.bKeyPressed( (int) SlimDXKey.Space ) )
				return ' ';
			if( keyboard.bKeyPressed( (int) SlimDXKey.Minus ) )
				return '-';
			return '\0';
		}

		private void tAdjustEditValue( int nRow, int nDelta )
		{
			this.tSetEditValue( nRow, this.nEditValue( nRow ) + nDelta );
		}

		private int nEditValue( int nRow )
		{
			switch( nRow )
			{
				case 0: return this.rEditing.stStart.nFileBar;
				case 1: return this.rEditing.stEnd.nFileBar;
				default: return this.rEditing.nLeadBars;
			}
		}

		private void tSetEditValue( int nRow, int nValue )
		{
			int nMax = ( this.nMaxFileBar > 0 ) ? ( this.nMaxFileBar + 1 ) : 999;
			switch( nRow )
			{
				case 0:
					this.rEditing.stStart = STPracticePosition.FromBar( Math.Max( 0, Math.Min( nValue, nMax ) ), 1.0 );
					break;
				case 1:
					this.rEditing.stEnd = STPracticePosition.FromBar( Math.Max( 0, Math.Min( nValue, nMax ) ), 1.0 );
					break;
				default:
					this.rEditing.nLeadBars = Math.Max( 0, Math.Min( nValue, 8 ) );
					break;
			}
		}

		private bool bEditingRangeIsValid()
		{
			return ( this.rEditing != null ) && ( this.rEditing.stEnd.nFileBar > this.rEditing.stStart.nFileBar );
		}

		private void tSaveUserRanges()
		{
			var listUser = new List<CPracticeRange>();
			foreach( CPracticeRange range in this.listRanges )
			{
				if( range.bIsUserRange )
					listUser.Add( range );
			}
			CPracticeSections.tSaveUserRanges( this.strChartPath, listUser );
		}

		#endregion

		#region [ 描画 ]

		private void tDrawIndicator()
		{
			if( !CDTXMania.ConfigIni.bPracticeMode )
				return;

			CPracticeRange rRange = CDTXMania.rPracticeRange;
			string strText = ( rRange != null )
				? ( "PRACTICE  " + rRange.strName + "  [" + rRange.strRangeText + "]" )
				: "PRACTICE  (Shift+F2 to pick a section)";

			if( !string.Equals( strText, this.strIndicatorText, StringComparison.Ordinal ) )
			{
				this.strIndicatorText = strText;
				CDTXMania.tReleaseTexture( ref this.txIndicator );
			}

			if( ( this.txIndicator == null ) && ( this.prvf小 != null ) )
			{
				using( Bitmap bmpText = this.prvf小.DrawPrivateFont( strText, Color.White, Color.Black ) )
				using( Bitmap bitmap = new Bitmap( bmpText.Width + 12, bmpText.Height + 6 ) )
				{
					using( Graphics g = Graphics.FromImage( bitmap ) )
					{
						g.FillRectangle( new SolidBrush( Color.FromArgb( 200, 20, 40, 20 ) ), 0, 0, bitmap.Width, bitmap.Height );
						g.DrawRectangle( new Pen( Color.FromArgb( 255, 120, 220, 120 ), 1f ), 0, 0, bitmap.Width - 1, bitmap.Height - 1 );
						g.DrawImage( bmpText, 6, 3 );
					}
					this.txIndicator = CDTXMania.tGenerateTexture( bitmap, false );
				}
			}

			if( this.txIndicator != null )
				this.txIndicator.tDraw2D( CDTXMania.app.Device, nIndicatorX, nIndicatorY );
		}

		private void tGeneratePanelTexture()
		{
			CDTXMania.tReleaseTexture( ref this.txPanel );

			if( ( this.prvf見出し == null ) || ( this.prvf行 == null ) || ( this.prvf小 == null ) )
				return;

			using( Bitmap bitmap = new Bitmap( nPanelWidth, nPanelHeight ) )
			{
				using( Graphics g = Graphics.FromImage( bitmap ) )
				{
					g.FillRectangle( new SolidBrush( Color.FromArgb( 232, 0, 0, 0 ) ), 0, 0, bitmap.Width, bitmap.Height );
					g.DrawRectangle( new Pen( Color.FromArgb( 255, 150, 220, 150 ), 2f ), 1, 1, bitmap.Width - 3, bitmap.Height - 3 );

					this.tDrawText( g, this.prvf見出し, "PRACTICE LOOP", 12, 8, Color.Lime );

					switch( this.ePhase )
					{
						case EPhase.List:
							this.tDrawList( g );
							break;
						case EPhase.EditRange:
							this.tDrawEditRange( g );
							break;
						case EPhase.EditName:
							this.tDrawEditName( g );
							break;
					}

					if( this.strMessage.Length > 0 )
						this.tDrawText( g, this.prvf小, this.strMessage, 12, nPanelHeight - 40, Color.Orange );

					this.tDrawText( g, this.prvf小, this.strFooter(), 12, nPanelHeight - 22, Color.Silver );
				}
				this.txPanel = CDTXMania.tGenerateTexture( bitmap, false );
			}
		}

		private string strFooter()
		{
			switch( this.ePhase )
			{
				case EPhase.EditRange:
					return "Up/Down: field   Left/Right or 0-9: value   Enter: go   Esc: back";
				case EPhase.EditName:
					return "A-Z 0-9 space -   Backspace   Enter: save   Esc: back";
				default:
					return "Up/Down: pick   Enter: use   Del: remove a saved range   Esc: close";
			}
		}

		private void tDrawList( Graphics g )
		{
			if( this.strChartPath.Length == 0 )
			{
				this.tDrawText( g, this.prvf行, "Select a song first.", 16, n1行目のY, Color.Silver );
				return;
			}

			int nRows = this.listRanges.Count + 2;
			int nTop = 0;
			if( nRows > nVisibleRows )
			{
				nTop = this.nCursor - ( nVisibleRows / 2 );
				if( nTop < 0 ) nTop = 0;
				if( nTop > nRows - nVisibleRows ) nTop = nRows - nVisibleRows;
			}

			string strBars = ( this.nMaxFileBar > 0 )
				? ( "bars 000-" + this.nMaxFileBar.ToString( "000", CultureInfo.InvariantCulture ) )
				: "";
			this.tDrawTextRight( g, this.prvf小, strBars, nPanelWidth - 12, 14, Color.Aqua );

			for( int nRow = nTop; ( nRow < nRows ) && ( nRow < nTop + nVisibleRows ); nRow++ )
			{
				int y = n1行目のY + ( ( nRow - nTop ) * n行の高さ );
				bool bSelected = ( nRow == this.nCursor );
				Color color = bSelected ? Color.Yellow : Color.White;

				if( bSelected )
					g.FillRectangle( new SolidBrush( Color.FromArgb( 90, 90, 200, 90 ) ), 8, y - 2, nPanelWidth - 16, n行の高さ );

				if( nRow == 0 )
				{
					this.tDrawText( g, this.prvf行, "OFF - play the whole song", 16, y, color );
					if( CDTXMania.rPracticeRange == null )
						this.tDrawTextRight( g, this.prvf小, "active", nPanelWidth - 16, y + 2, Color.Lime );
				}
				else if( nRow == nRows - 1 )
				{
					this.tDrawText( g, this.prvf行, "New range...", 16, y, color );
				}
				else
				{
					CPracticeRange range = this.listRanges[ nRow - 1 ];
					string strName = range.strName;
					if( strName.Length == 0 )
						strName = "(unnamed)";
					this.tDrawText( g, this.prvf行, ( range.bIsUserRange ? "* " : "  " ) + strName, 16, y, color, nName最大幅 );
					this.tDrawTextRight( g, this.prvf小, range.strRangeText, nPanelWidth - 16, y + 2,
						object.ReferenceEquals( range, CDTXMania.rPracticeRange ) ? Color.Lime : Color.Aqua );
				}
			}

			if( this.listRanges.Count == 0 )
				this.tDrawText( g, this.prvf小, "no sections.def in this song folder", 16, n1行目のY + ( 2 * n行の高さ ) + 6, Color.Silver );
		}

		private void tDrawEditRange( Graphics g )
		{
			string[] arLabel = new string[] { "START bar", "END bar (exclusive)", "LEAD bars", "USE NOW", "NAME & SAVE" };

			for( int nRow = 0; nRow < arLabel.Length; nRow++ )
			{
				int y = n1行目のY + ( nRow * n行の高さ );
				bool bSelected = ( nRow == this.nEditCursor );
				Color color = bSelected ? Color.Yellow : Color.White;

				if( bSelected )
					g.FillRectangle( new SolidBrush( Color.FromArgb( 90, 90, 200, 90 ) ), 8, y - 2, nPanelWidth - 16, n行の高さ );

				this.tDrawText( g, this.prvf行, arLabel[ nRow ], 16, y, color );

				if( nRow <= 2 )
				{
					this.tDrawTextRight( g, this.prvf行,
						this.nEditValue( nRow ).ToString( ( nRow <= 1 ) ? "000" : "0", CultureInfo.InvariantCulture ),
						nPanelWidth - 16, y, color );
				}
			}

			int yInfo = n1行目のY + ( arLabel.Length * n行の高さ ) + 8;
			this.tDrawText( g, this.prvf小,
				"loops " + this.rEditing.strRangeText + "  (bar numbers are .dtx bars)", 16, yInfo, Color.Aqua );
		}

		private void tDrawEditName( Graphics g )
		{
			this.tDrawText( g, this.prvf行, "Name this range:", 16, n1行目のY, Color.White );
			this.tDrawText( g, this.prvf見出し, this.strEditName + "_", 16, n1行目のY + n行の高さ + 4, Color.Yellow );
			this.tDrawText( g, this.prvf小, "range " + this.rEditing.strRangeText, 16, n1行目のY + ( 3 * n行の高さ ), Color.Aqua );
			this.tDrawText( g, this.prvf小, "saved in " + CPracticeSections.strUserRangeFileName + " next to the game", 16, n1行目のY + ( 4 * n行の高さ ), Color.Silver );
		}

		private void tDrawText( Graphics g, CPrivateFastFont prvf, string str, int x, int y, Color color )
		{
			this.tDrawText( g, prvf, str, x, y, color, 0 );
		}
		private void tDrawText( Graphics g, CPrivateFastFont prvf, string str, int x, int y, Color color, int n最大幅 )
		{
			if( string.IsNullOrEmpty( str ) || ( prvf == null ) )
				return;
			using( Bitmap bitmap = prvf.DrawPrivateFont( str, color, Color.Black ) )
			{
				int nWidth = bitmap.Width;
				if( ( n最大幅 > 0 ) && ( nWidth > n最大幅 ) )
					nWidth = n最大幅;
				g.DrawImage( bitmap, x, y, nWidth, bitmap.Height );
			}
		}
		private void tDrawTextRight( Graphics g, CPrivateFastFont prvf, string str, int xRight, int y, Color color )
		{
			if( string.IsNullOrEmpty( str ) || ( prvf == null ) )
				return;
			using( Bitmap bitmap = prvf.DrawPrivateFont( str, color, Color.Black ) )
			{
				g.DrawImage( bitmap, xRight - bitmap.Width, y, bitmap.Width, bitmap.Height );
			}
		}

		#endregion

		#region [ private ]
		//-----------------
		private enum EPhase
		{
			List,
			EditRange,
			EditName,
		}

		private const int nPanelX = 300;
		private const int nPanelY = 120;
		private const int nPanelWidth = 680;
		private const int nPanelHeight = 460;
		private const int n1行目のY = 44;
		private const int n行の高さ = 26;
		private const int nVisibleRows = 13;
		private const int nName最大幅 = 430;
		private const int nMaxNameLength = 20;
		private const int nIndicatorX = 4;
		private const int nIndicatorY = 4;

		private EPhase ePhase = EPhase.List;
		private bool bRedraw = true;
		private int nCursor;
		private int nEditCursor;
		private int nMaxFileBar;
		private string strChartPath = "";
		private string strTypeBuffer = "";
		private string strEditName = "";
		private string strMessage = "";
		private string strIndicatorText = "";
		private CPracticeRange rEditing;
		private List<CPracticeRange> listRanges = new List<CPracticeRange>();
		private CPrivateFastFont prvf見出し;
		private CPrivateFastFont prvf行;
		private CPrivateFastFont prvf小;
		private CTexture txPanel;
		private CTexture txIndicator;
		//-----------------
		#endregion
	}
}
