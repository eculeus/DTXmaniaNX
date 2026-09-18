namespace FDK
{
	/// <summary>
	/// <para>再生速度(と周波数倍率)を、どのストリームのどの属性で実現するかの判断。</para>
	/// <para>BASSに一切依存しないので、サウンドデバイスの無い環境でも単体で検証できる。</para>
	/// <para>(検証用ハーネス: Tests/TimeStretchRouting/RoutingTests.cs)</para>
	/// </summary>
	public static class CSoundPlaySpeedRouting
	{
		/// <summary>
		/// <para>ミキサーに繋ぐ(＝再生に使う)ハンドルを返す。</para>
		/// <para>TempoStreamを持っているサウンドは、再生速度がx1.000であっても常にTempoStreamを使う。</para>
		/// <para>再生速度でハンドルを繋ぎ替えると、元のストリーム(TempoStreamのデコード元)とTempoStreamの</para>
		/// <para>両方がミキサーに登録されたままになり、同じデコード元が2重に消費されて再生が速くなる。</para>
		/// </summary>
		public static int nPlaybackHandle(int hBassStream, int hTempoStream)
		{
			return (hTempoStream != 0) ? hTempoStream : hBassStream;
		}

		/// <summary>
		/// <para>再生速度の変更を、TempoStream(BASS_ATTRIB_TEMPO)で行うかどうかを返す。</para>
		/// <para>falseなら周波数(＝ピッチも変わる)で行う。</para>
		/// <para>CSoundManager.bIsTimeStretchはサウンド生成時にしか参照されないので、生成後にONにされた</para>
		/// <para>サウンドにはTempoStreamが無い。その場合も周波数変更にフォールバックする。</para>
		/// <para>(でないとBASS_ATTRIB_TEMPOが素のストリームに対して無視され、再生速度が効かなくなる。)</para>
		/// </summary>
		public static bool bUseTempoStream(int hTempoStream, bool bIsTimeStretch)
		{
			return (hTempoStream != 0 && bIsTimeStretch);
		}

		/// <summary>
		/// BASS_ATTRIB_TEMPO に設定する値(原速に対する増減%)。
		/// </summary>
		public static float fTempoPercent(double db再生速度)
		{
			return (float)(db再生速度 * 100 - 100);
		}

		/// <summary>
		/// <para>周波数に設定する値。</para>
		/// <para>TempoStreamで再生速度を変える場合は、周波数に再生速度を含めてはならない。</para>
		/// <para>(含めると、周波数倍率が変わるチップ(bad hit)だけ再生速度がピッチとして二重に掛かる。)</para>
		/// </summary>
		public static float f周波数(double db周波数倍率, double db再生速度, int nオリジナルの周波数, bool bUseTempoStream)
		{
			return (float)(db周波数倍率 * (bUseTempoStream ? 1.0 : db再生速度) * nオリジナルの周波数);
		}
	}
}
