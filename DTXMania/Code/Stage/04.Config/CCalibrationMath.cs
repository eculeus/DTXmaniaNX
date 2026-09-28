using System;
using System.Collections.Generic;

namespace DTXMania
{
	/// <summary>
	/// The arithmetic of the drum timing calibration (Config > Drums > Calibrate), kept free of
	/// any game dependency so CI can compile and check it on its own (Tests\Calibration).
	/// Written to C# 5 for that reason: the CI check compiles it with the Framework csc.
	/// </summary>
	/// <remarks>
	/// Offsets are raw: hit timestamp minus click time, both on CSoundManager.rcPerformanceTimer,
	/// with NO InputAdjust applied. + is late, - is early. The judgement computes
	/// lag = hit + InputAdjust - chip, so the InputAdjust that centres the median on 0 is -median,
	/// whatever InputAdjust is set to now (the suggestion is an absolute value, not an increment).
	/// </remarks>
	public static class CCalibrationMath
	{
		/// <summary>Clicks at the start that are only a count-in; hits near them are ignored.</summary>
		public const int nCountInClicks = 4;
		/// <summary>Hits to collect after the count-in.</summary>
		public const int nTargetHits = 24;
		/// <summary>Fewer usable hits than this and no suggestion is made.</summary>
		public const int nMinHits = 8;
		/// <summary>A hit further than this from its nearest click is not counted at all.</summary>
		public const int nRejectMs = 150;
		/// <summary>After the median is known, hits further than this many MADs from it are dropped.</summary>
		public const double dbMadLimit = 3.0;
		/// <summary>Floor for the MAD in that test, so a very steady player does not lose ordinary hits.</summary>
		public const double dbMadFloorMs = 2.0;
		public const int nInputAdjustMin = -99;
		public const int nInputAdjustMax = 99;

		/// <summary>Index of the click nearest to <paramref name="nHitMs"/> (may be negative or past the end).</summary>
		public static long nNearestClick(long nHitMs, long nFirstClickMs, int nPeriodMs)
		{
			return (long)Math.Floor((double)(nHitMs - nFirstClickMs) / nPeriodMs + 0.5);
		}

		/// <summary>
		/// Pair a hit with its nearest click. Returns false if that click is part of the count-in or
		/// beyond the last click (the hit is ignored). Otherwise <paramref name="nOffsetMs"/> is
		/// hit - click and <paramref name="bAccepted"/> says whether it is within ±nRejectMs.
		/// </summary>
		public static bool tPair(long nHitMs, long nFirstClickMs, int nPeriodMs, int nClicks, out int nOffsetMs, out bool bAccepted)
		{
			long k = nNearestClick(nHitMs, nFirstClickMs, nPeriodMs);
			nOffsetMs = 0;
			bAccepted = false;
			if (k < nCountInClicks || k >= nClicks)
				return false;
			nOffsetMs = (int)(nHitMs - (nFirstClickMs + k * nPeriodMs));
			bAccepted = Math.Abs(nOffsetMs) <= nRejectMs;
			return true;
		}

		public static double dbMedian(IList<int> values)
		{
			if (values == null || values.Count == 0)
				return 0.0;
			List<int> sorted = new List<int>(values);
			sorted.Sort();
			int n = sorted.Count;
			return (n % 2 == 1) ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
		}

		public static double dbMedian(IList<double> values)
		{
			if (values == null || values.Count == 0)
				return 0.0;
			List<double> sorted = new List<double>(values);
			sorted.Sort();
			int n = sorted.Count;
			return (n % 2 == 1) ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
		}

		/// <summary>Median absolute deviation from the median.</summary>
		public static double dbMad(IList<int> values)
		{
			if (values == null || values.Count == 0)
				return 0.0;
			double med = dbMedian(values);
			List<double> dev = new List<double>();
			foreach (int v in values)
				dev.Add(Math.Abs(v - med));
			return dbMedian(dev);
		}

		/// <summary>Sample standard deviation (0 for fewer than two values).</summary>
		public static double dbStdDev(IList<int> values)
		{
			if (values == null || values.Count < 2)
				return 0.0;
			double sum = 0;
			foreach (int v in values) sum += v;
			double mean = sum / values.Count;
			double ss = 0;
			foreach (int v in values) ss += (v - mean) * (v - mean);
			return Math.Sqrt(ss / (values.Count - 1));
		}

		/// <summary>The InputAdjust that puts <paramref name="dbMedianMs"/> on 0: -median, rounded, clamped to -99..99.</summary>
		public static int nSuggestedInputAdjust(double dbMedianMs)
		{
			int n = -(int)Math.Round(dbMedianMs, MidpointRounding.AwayFromZero);
			if (n < nInputAdjustMin) n = nInputAdjustMin;
			if (n > nInputAdjustMax) n = nInputAdjustMax;
			return n;
		}

		public class CResult
		{
			public int nAccepted;		// hits within ±nRejectMs
			public int nUsed;			// of those, kept by the MAD test
			public double dbMedianMs;	// of the used hits
			public double dbMadMs;
			public double dbStdDevMs;
			public bool bEnough;		// nUsed >= nMinHits
			public int nSuggestedInputAdjust;
			public List<int> listUsed = new List<int>();
		}

		/// <summary>Summarise the accepted offsets: drop hits beyond 3 MAD of the median, then median, MAD, SD and the suggestion.</summary>
		public static CResult tSummarize(IList<int> accepted)
		{
			CResult r = new CResult();
			r.nAccepted = (accepted == null) ? 0 : accepted.Count;
			if (r.nAccepted == 0)
				return r;
			double med0 = dbMedian(accepted);
			double limit = dbMadLimit * Math.Max(dbMad(accepted), dbMadFloorMs);
			foreach (int v in accepted)
			{
				if (Math.Abs(v - med0) <= limit)
					r.listUsed.Add(v);
			}
			r.nUsed = r.listUsed.Count;
			r.dbMedianMs = dbMedian(r.listUsed);
			r.dbMadMs = dbMad(r.listUsed);
			r.dbStdDevMs = dbStdDev(r.listUsed);
			r.bEnough = r.nUsed >= nMinHits;
			r.nSuggestedInputAdjust = nSuggestedInputAdjust(r.dbMedianMs);
			return r;
		}

		/// <summary>
		/// A mono 16-bit PCM WAV with a click at nLeadMs + k * nPeriodMs for k = 0..nClicks-1, the first
		/// of every four accented. Each click starts on its exact sample so the click times are exact.
		/// </summary>
		public static byte[] tGenerateClickWav(int nSampleRate, int nLeadMs, int nPeriodMs, int nClicks, int nTailMs)
		{
			long nTotalMs = (long)nLeadMs + (long)nPeriodMs * nClicks + nTailMs;
			int nSamples = (int)(nTotalMs * nSampleRate / 1000);
			short[] pcm = new short[nSamples];
			int nClickLen = nSampleRate * 40 / 1000;	// 40 ms, decaying
			for (int k = 0; k < nClicks; k++)
			{
				int start = (int)(((long)nLeadMs + (long)k * nPeriodMs) * nSampleRate / 1000);
				double freq = (k % 4 == 0) ? 2000.0 : 1400.0;
				double amp = (k % 4 == 0) ? 0.75 : 0.55;
				for (int i = 0; i < nClickLen && start + i < nSamples; i++)
				{
					double t = (double)i / nSampleRate;
					double env = Math.Exp(-t * 120.0);
					pcm[start + i] = (short)(amp * env * Math.Sin(2 * Math.PI * freq * t) * short.MaxValue);
				}
			}

			int nDataBytes = nSamples * 2;
			byte[] wav = new byte[44 + nDataBytes];
			tPutAscii(wav, 0, "RIFF");
			tPutInt32(wav, 4, 36 + nDataBytes);
			tPutAscii(wav, 8, "WAVE");
			tPutAscii(wav, 12, "fmt ");
			tPutInt32(wav, 16, 16);
			tPutInt16(wav, 20, 1);					// PCM
			tPutInt16(wav, 22, 1);					// mono
			tPutInt32(wav, 24, nSampleRate);
			tPutInt32(wav, 28, nSampleRate * 2);	// byte rate
			tPutInt16(wav, 32, 2);					// block align
			tPutInt16(wav, 34, 16);					// bits per sample
			tPutAscii(wav, 36, "data");
			tPutInt32(wav, 40, nDataBytes);
			for (int i = 0; i < nSamples; i++)
			{
				wav[44 + i * 2] = (byte)(pcm[i] & 0xff);
				wav[44 + i * 2 + 1] = (byte)((pcm[i] >> 8) & 0xff);
			}
			return wav;
		}

		private static void tPutAscii(byte[] b, int at, string s)
		{
			for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i];
		}
		private static void tPutInt32(byte[] b, int at, int v)
		{
			b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24);
		}
		private static void tPutInt16(byte[] b, int at, int v)
		{
			b[at] = (byte)v; b[at + 1] = (byte)(v >> 8);
		}
	}
}
