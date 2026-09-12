using System;
using System.Diagnostics;

namespace FDK
{
	/// <summary>
	/// <para>A sound device that opens nothing and plays nothing.</para>
	/// <para>It exists for one situation: a machine with no audio endpoint at all, where every
	/// real device (ASIO, WASAPI, DirectSound) fails to open. Before this, the fallback chain
	/// ended with SoundDevice left null, and the first thing that touched it - the master volume
	/// setter, or the performance screen's clock - threw a NullReferenceException, so the game
	/// could not start. It is opt-in: CSoundManager only reaches for it when the environment
	/// variable FDK_ALLOW_SILENT_SOUND_DEVICE is set, which a normal desktop run never does.</para>
	/// <para>It reports itself as DirectSound so CSoundTimer drives the performance clock off the
	/// multimedia timer, and it refuses to create sounds - which every caller already handles,
	/// because a chip whose WAV failed to load is an ordinary thing.</para>
	/// </summary>
	public class CSoundDeviceNull : ISoundDevice
	{
		public ESoundDeviceType e出力デバイス
		{
			get;
			private set;
		}
		public long n実出力遅延ms
		{
			get { return 0; }
		}
		public long n実バッファサイズms
		{
			get { return 0; }
		}
		public long n経過時間ms
		{
			get { return this.tmシステムタイマ.nSystemTimeMs; }
		}
		public long n経過時間を更新したシステム時刻ms
		{
			get { return this.tmシステムタイマ.nSystemTimeMs; }
		}
		public CTimer tmシステムタイマ
		{
			get;
			private set;
		}
		public string strDefaultSoundDeviceBusType
		{
			get { return "(silent)"; }
		}
		public int nMasterVolume
		{
			get { return 100; }
			set { }
		}

		public CSoundDeviceNull()
		{
			this.tmシステムタイマ = new CTimer(CTimer.EType.MultiMedia);

			// CSoundTimer only knows how to derive a clock for the device types it was written for;
			// DirectSound is its "just use the multimedia timer" branch, which is exactly right here.
			this.e出力デバイス = ESoundDeviceType.DirectSound;

			Trace.TraceWarning("No sound device could be opened. FDK_ALLOW_SILENT_SOUND_DEVICE is set, so playback is silent.");
		}

		public CSound tサウンドを作成する(string strファイル名)
		{
			throw new NotSupportedException("There is no sound device; sounds cannot be created.");
		}
		public CSound tサウンドを作成する(string strファイル名, CSound.EInstType eInstType)
		{
			throw new NotSupportedException("There is no sound device; sounds cannot be created.");
		}
		public CSound tサウンドを作成する(byte[] byArrWAVファイルイメージ)
		{
			throw new NotSupportedException("There is no sound device; sounds cannot be created.");
		}
		public CSound tサウンドを作成する(byte[] byArrWAVファイルイメージ, CSound.EInstType eInstType)
		{
			throw new NotSupportedException("There is no sound device; sounds cannot be created.");
		}
		public void tサウンドを作成する(string strファイル名, ref CSound sound, CSound.EInstType eInstType)
		{
			throw new NotSupportedException("There is no sound device; sounds cannot be created.");
		}
		public void tサウンドを作成する(byte[] byArrWAVファイルイメージ, ref CSound sound, CSound.EInstType eInstType)
		{
			throw new NotSupportedException("There is no sound device; sounds cannot be created.");
		}

		public bool tStartRecording()
		{
			return false;
		}
		public bool tStopRecording()
		{
			return false;
		}

		public void Dispose()
		{
			CTimer tm = this.tmシステムタイマ;
			this.tmシステムタイマ = null;
			CCommon.tDispose(ref tm);
		}
	}
}
