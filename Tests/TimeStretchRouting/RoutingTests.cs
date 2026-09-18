// Offline harness for the TimeStretch play-speed routing (fix/timestretch-playspeed).
//
// It is compiled together with the shipped FDK/Code/03.Sound/CSoundPlaySpeedRouting.cs, so the
// routing RULES under test are the real ones. What is modelled here is the mixer bookkeeping
// around them: BASS itself needs a sound device, and neither this Mac nor the CI runner has one.
//
// The model is deliberately small:
//   - a BASS mixer is a set of channel handles;
//   - a channel pulls from its decode source once per mixing tick;
//   - the tempo stream's decode source IS the raw stream (BASS_FX_TempoCreate(raw, ... FREESOURCE)),
//     so if both are in the mixer at once, the raw stream is consumed twice per tick and the song
//     runs at about twice the rate it should. That is the bug Greg heard: slow it down and it
//     speeds up, put it back to 1.00 and it never recovers.
//
// Build + run:  mono:  mcs -out:routing.exe RoutingTests.cs ../../FDK/Code/03.Sound/CSoundPlaySpeedRouting.cs && mono routing.exe
//               .NET:  csc  /out:routing.exe RoutingTests.cs ..\..\FDK\Code\03.Sound\CSoundPlaySpeedRouting.cs && routing.exe
// (C# 5 only, so the .NET Framework compiler on the CI runner can build it.)

using System;
using System.Collections.Generic;
using FDK;

namespace TimeStretchRoutingTests
{
	/// <summary>A BASS mixer: a set of channels, each pulling from its decode source per tick.</summary>
	internal class FakeMixer
	{
		private readonly List<int> channels = new List<int>();
		private readonly Dictionary<int, int> sourceOfChannel = new Dictionary<int, int>();

		public void AddChannel(int hChannel, int hDecodeSource)
		{
			if (this.channels.Contains(hChannel))
				return;                                 // BASS_Mixer_ChannelGetMixer() guard in tBASSAddSoundToMixer()
			this.channels.Add(hChannel);
			this.sourceOfChannel[hChannel] = hDecodeSource;
		}

		public bool RemoveChannel(int hChannel)
		{
			return this.channels.Remove(hChannel);
		}

		public bool Contains(int hChannel)
		{
			return this.channels.Contains(hChannel);
		}

		public int ChannelCount
		{
			get { return this.channels.Count; }
		}

		/// <summary>How many times one mixing tick consumes the given decode source.</summary>
		public int PullsPerTick(int hDecodeSource)
		{
			int n = 0;
			foreach (int ch in this.channels)
			{
				if (this.sourceOfChannel[ch] == hDecodeSource)
					n++;
			}
			return n;
		}
	}

	/// <summary>A CSound, reduced to the parts that decide which handle the mixer sees.</summary>
	internal class FakeSound
	{
		public const int hRaw = 101;                    // _hBassStream
		public const int hTempo = 202;                  // _hTempoStream (its decode source is hRaw)

		private readonly FakeMixer mixer;
		private readonly bool bTimeStretch;
		private readonly bool bNewRouting;
		private int _hTempoStream;
		private double _db再生速度 = 1.0;

		public int hPlayback;                           // hBassStream: what actually goes into the mixer
		public int nMixerSwitches;                      // how many times the mixed handle changed identity
		public double dbLastPositionSec;                // position preserved across a handle switch

		public FakeSound(FakeMixer mixer, bool bTimeStretch, bool bTempoStreamExists, bool bNewRouting)
		{
			this.mixer = mixer;
			this.bTimeStretch = bTimeStretch;
			this.bNewRouting = bNewRouting;
			this._hTempoStream = bTempoStreamExists ? hTempo : 0;

			// tBASSサウンドを作成する_ストリーム生成後の共通処理()
			this.hPlayback = bNewRouting
				? CSoundPlaySpeedRouting.nPlaybackHandle(hRaw, this._hTempoStream)
				: hRaw;                                 // 旧: bIs1倍速再生 == true なので必ず素のストリーム
		}

		/// <summary>tBASSAddSoundToMixer()</summary>
		public void AddToMixer()
		{
			this.mixer.AddChannel(this.hPlayback, hRaw);
		}

		/// <summary>dbPlaySpeed setter</summary>
		public void SetPlaySpeed(double db再生速度)
		{
			if (this._db再生速度 == db再生速度)
				return;
			this._db再生速度 = db再生速度;

			int hWanted = this.bNewRouting
				? CSoundPlaySpeedRouting.nPlaybackHandle(hRaw, this._hTempoStream)
				: ((this._hTempoStream != 0 && db再生速度 != 1.000) ? hTempo : hRaw);   // 旧

			if (this.bNewRouting)
				this.tSetPlaybackHandle(hWanted);       // ミキサーからの削除を伴う繋ぎ替え
			else
				this.hPlayback = hWanted;               // 旧: 削除せずに差し替えるだけ

			// tPlaySound(): BASS_Mixer_ChannelPlay() が失敗したらミキサーに追加してから再生する
			this.AddToMixer();
		}

		/// <summary>tBASS再生に使用するハンドルを設定する()</summary>
		public void tSetPlaybackHandle(int hNewStream)
		{
			if (hNewStream == this.hPlayback)
				return;
			this.nMixerSwitches++;
			if (this.mixer.Contains(this.hPlayback))
			{
				double dbPos = this.dbLastPositionSec;
				this.mixer.RemoveChannel(this.hPlayback);
				this.hPlayback = hNewStream;
				this.AddToMixer();
				this.dbLastPositionSec = dbPos;         // 再生位置を引き継ぐ
			}
			else
			{
				this.hPlayback = hNewStream;
			}
		}

		public bool bUseTempo
		{
			get { return CSoundPlaySpeedRouting.bUseTempoStream(this._hTempoStream, this.bTimeStretch); }
		}

		public float fFrequency(double db周波数倍率, int nオリジナルの周波数)
		{
			return CSoundPlaySpeedRouting.f周波数(db周波数倍率, this._db再生速度, nオリジナルの周波数, this.bUseTempo);
		}
	}

	internal static class Program
	{
		private static int nFailures;

		private static void Check(bool bCondition, string strWhat)
		{
			Console.WriteLine((bCondition ? "PASS  " : "FAIL  ") + strWhat);
			if (!bCondition)
				nFailures++;
		}

		private static void CheckEqual(double dbExpected, double dbActual, string strWhat)
		{
			// float単精度で計算される値も比較するので、相対誤差で見る
			Check(Math.Abs(dbExpected - dbActual) <= 1e-6 * Math.Max(1.0, Math.Abs(dbExpected)), strWhat + " (expected " + dbExpected + ", got " + dbActual + ")");
		}

		public static int Main()
		{
			Console.WriteLine("--- the bug, as it is today (speed picks the handle, nothing is removed) ---");
			{
				var mixer = new FakeMixer();
				var sound = new FakeSound(mixer, true, true, false);
				sound.AddToMixer();
				Check(mixer.ChannelCount == 1 && mixer.Contains(FakeSound.hRaw), "x1.000: only the raw stream is mixed");
				CheckEqual(1, mixer.PullsPerTick(FakeSound.hRaw), "x1.000: the decode source is consumed once per tick");

				sound.SetPlaySpeed(0.90);
				Check(mixer.Contains(FakeSound.hRaw) && mixer.Contains(FakeSound.hTempo), "x0.900: BOTH handles are now in the mixer");
				CheckEqual(2, mixer.PullsPerTick(FakeSound.hRaw), "x0.900: the decode source is consumed TWICE per tick (the song speeds up)");

				sound.SetPlaySpeed(1.00);
				CheckEqual(2, mixer.PullsPerTick(FakeSound.hRaw), "back to x1.000: still consumed twice (it never recovers)");
			}

			Console.WriteLine();
			Console.WriteLine("--- with the fix (the tempo stream is the mixed handle for the life of the sound) ---");
			{
				var mixer = new FakeMixer();
				var sound = new FakeSound(mixer, true, true, true);
				sound.AddToMixer();
				int hAtLoad = sound.hPlayback;
				Check(hAtLoad == FakeSound.hTempo, "x1.000: the tempo stream is what is mixed");
				CheckEqual(1, mixer.PullsPerTick(FakeSound.hRaw), "x1.000: the decode source is consumed once per tick");

				sound.SetPlaySpeed(0.90);
				Check(sound.hPlayback == hAtLoad, "x0.900: the mixed handle did not change identity");
				Check(mixer.ChannelCount == 1 && !mixer.Contains(FakeSound.hRaw), "x0.900: the raw stream is never added to the mixer");
				CheckEqual(1, mixer.PullsPerTick(FakeSound.hRaw), "x0.900: the decode source is still consumed once per tick");

				sound.SetPlaySpeed(1.00);
				Check(sound.hPlayback == hAtLoad && mixer.ChannelCount == 1, "back to x1.000: same handle, one channel");
				CheckEqual(1, mixer.PullsPerTick(FakeSound.hRaw), "back to x1.000: the decode source is consumed once per tick");
				Check(sound.nMixerSwitches == 0, "no handle switch ever happens");
			}

			Console.WriteLine();
			Console.WriteLine("--- the defensive switch, if a future change ever moves the handle again ---");
			{
				var mixer = new FakeMixer();
				var sound = new FakeSound(mixer, true, true, true);
				sound.AddToMixer();
				sound.dbLastPositionSec = 12.5;
				// Force a switch by hand (nothing in the shipped code does this any more).
				sound.tSetPlaybackHandle(FakeSound.hRaw);
				Check(mixer.ChannelCount == 1, "after a forced switch the mixer still holds exactly one channel");
				Check(mixer.Contains(FakeSound.hRaw) && !mixer.Contains(FakeSound.hTempo), "the old handle was removed and the new one added");
				CheckEqual(1, mixer.PullsPerTick(FakeSound.hRaw), "the decode source is still consumed once per tick");
				CheckEqual(12.5, sound.dbLastPositionSec, "the playback position survived the switch");
			}

			Console.WriteLine();
			Console.WriteLine("--- TimeStretch ON but the sound was loaded before it was switched on ---");
			{
				var mixer = new FakeMixer();
				var sound = new FakeSound(mixer, true, false, true);     // bIsTimeStretch ON, no tempo stream
				sound.AddToMixer();
				Check(sound.hPlayback == FakeSound.hRaw, "the raw stream is mixed (there is no tempo stream)");
				Check(!sound.bUseTempo, "BASS_ATTRIB_TEMPO is not used on a stream that cannot honour it");
				sound.SetPlaySpeed(0.50);
				CheckEqual(22050, sound.fFrequency(1.0, 44100), "the play speed falls back to the frequency, so it is not silently ignored");
			}

			Console.WriteLine();
			Console.WriteLine("--- the bad-hit pitch multiplier must not re-apply the play speed in TimeStretch mode ---");
			{
				var mixer = new FakeMixer();
				var stretched = new FakeSound(mixer, true, true, true);
				stretched.SetPlaySpeed(0.80);
				Check(stretched.bUseTempo, "the speed is carried by BASS_ATTRIB_TEMPO");
				CheckEqual(-20.0, CSoundPlaySpeedRouting.fTempoPercent(0.80), "x0.800 is -20% tempo");
				CheckEqual(1.07 * 44100, stretched.fFrequency(1.07, 44100), "frequency = pitch multiplier only");

				var pitched = new FakeSound(mixer, false, false, true);  // TimeStretch OFF: the old behaviour
				pitched.SetPlaySpeed(0.80);
				CheckEqual(1.07 * 0.80 * 44100, pitched.fFrequency(1.07, 44100), "frequency = pitch multiplier x play speed");
			}

			Console.WriteLine();
			Console.WriteLine(nFailures == 0 ? "All routing checks passed." : (nFailures + " routing check(s) FAILED."));
			return (nFailures == 0) ? 0 : 1;
		}
	}
}
