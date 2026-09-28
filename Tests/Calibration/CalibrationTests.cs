// Offline checks for the drum timing calibration arithmetic (Config > Drums > Calibrate).
//
// Compiled together with the shipped DTXMania/Code/Stage/04.Config/CCalibrationMath.cs, so what is
// checked is the real pairing, outlier handling and InputAdjust suggestion, plus the click WAV the
// screen plays (its clicks must start on the exact samples the pairing assumes).
//
// Build + run:  mono:  mcs -out:calib.exe CalibrationTests.cs ../../DTXMania/Code/Stage/04.Config/CCalibrationMath.cs && mono calib.exe
//               .NET:  csc  /out:calib.exe CalibrationTests.cs ..\..\DTXMania\Code\Stage\04.Config\CCalibrationMath.cs && calib.exe
// (C# 5 only, so the .NET Framework compiler on the CI runner can build it.)

using System;
using System.Collections.Generic;
using DTXMania;

namespace CalibrationTests
{
	internal static class Program
	{
		private static int failures = 0;

		private static void Check(bool ok, string what)
		{
			Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
			if (!ok) failures++;
		}

		private static int Main()
		{
			const int period = 500;		// 120 BPM
			const long first = 10000;	// first click on the timer, ms
			const int clicks = 40;
			int off; bool acc;

			// --- pairing ---
			Check(!CCalibrationMath.tPair(first + 3 * period + 20, first, period, clicks, out off, out acc), "a hit on count-in click 4 is ignored");
			Check(CCalibrationMath.tPair(first + 4 * period + 20, first, period, clicks, out off, out acc) && off == 20 && acc, "the first click after the count-in pairs: +20 late");
			Check(CCalibrationMath.tPair(first + 10 * period - 35, first, period, clicks, out off, out acc) && off == -35 && acc, "early hit pairs with the next click: -35");
			Check(CCalibrationMath.tPair(first + 10 * period + 249, first, period, clicks, out off, out acc) && off == 249 && !acc, "+249 pairs with click 10 but is rejected (> 150)");
			Check(CCalibrationMath.tPair(first + 10 * period + 251, first, period, clicks, out off, out acc) && off == -249 && !acc, "+251 pairs with click 11 as -249, rejected");
			Check(CCalibrationMath.tPair(first + 10 * period + 150, first, period, clicks, out off, out acc) && acc, "exactly 150 ms is still accepted");
			Check(!CCalibrationMath.tPair(first + clicks * period, first, period, clicks, out off, out acc), "a hit past the last click is ignored");
			Check(!CCalibrationMath.tPair(first - 2000, first, period, clicks, out off, out acc), "a hit before the first click is ignored");

			// --- statistics ---
			Check(CCalibrationMath.dbMedian(new List<int> { 5, 1, 3 }) == 3.0, "median odd");
			Check(CCalibrationMath.dbMedian(new List<int> { 4, 1, 3, 2 }) == 2.5, "median even");
			Check(CCalibrationMath.dbMad(new List<int> { 1, 2, 3, 4, 100 }) == 1.0, "MAD ignores the outlier");
			Check(Math.Abs(CCalibrationMath.dbStdDev(new List<int> { 2, 4, 4, 4, 5, 5, 7, 9 }) - 2.138) < 0.001, "sample SD");

			// --- suggestion: lag = hit + InputAdjust - chip, so InputAdjust = -median ---
			Check(CCalibrationMath.nSuggestedInputAdjust(23.0) == -23, "late by 23 -> InputAdjust -23");
			Check(CCalibrationMath.nSuggestedInputAdjust(-12.0) == 12, "early by 12 -> InputAdjust +12");
			Check(CCalibrationMath.nSuggestedInputAdjust(7.5) == -8, "7.5 rounds away from zero -> -8");
			Check(CCalibrationMath.nSuggestedInputAdjust(140.0) == -99, "clamped to -99");
			Check(CCalibrationMath.nSuggestedInputAdjust(-140.0) == 99, "clamped to 99");

			// --- a whole session: player 30 ms late with +-8 ms scatter and two stray hits ---
			List<int> acceptedOffsets = new List<int>();
			Random rnd = new Random(1);
			for (int k = 4; k < 4 + 24; k++)
			{
				long hit = first + k * period + 30 + rnd.Next(-8, 9);
				if (CCalibrationMath.tPair(hit, first, period, clicks, out off, out acc) && acc)
					acceptedOffsets.Add(off);
			}
			acceptedOffsets.Add(140);	// a stray, inside +-150 but far from the rest
			acceptedOffsets.Add(-120);
			CCalibrationMath.CResult r = CCalibrationMath.tSummarize(acceptedOffsets);
			Console.WriteLine(string.Format("     session: accepted {0}, used {1}, median {2}, MAD {3:0.0}, SD {4:0.0}, suggest {5}",
				r.nAccepted, r.nUsed, r.dbMedianMs, r.dbMadMs, r.dbStdDevMs, r.nSuggestedInputAdjust));
			Check(r.nAccepted == 26 && r.nUsed == 24, "the two strays are dropped by the 3-MAD test, the 24 real hits kept");
			Check(Math.Abs(r.dbMedianMs - 30) <= 3, "median near +30");
			Check(r.nSuggestedInputAdjust <= -27 && r.nSuggestedInputAdjust >= -33, "suggestion near -30");
			Check(r.dbStdDevMs < 8, "SD reflects the +-8 scatter, not the strays");
			Check(r.bEnough, "enough hits");
			Check(!CCalibrationMath.tSummarize(new List<int> { 1, 2, 3 }).bEnough, "3 hits is not enough");
			Check(CCalibrationMath.tSummarize(new List<int>()).nAccepted == 0, "empty session");

			// a perfectly steady player: MAD 0 must not throw away hits 1 ms off
			CCalibrationMath.CResult steady = CCalibrationMath.tSummarize(new List<int> { 10, 10, 10, 10, 10, 10, 11, 9, 10, 12 });
			Check(steady.nUsed == 10, "MAD floor keeps near-identical hits");

			// --- the click WAV ---
			int rate = 44100, lead = 500;
			byte[] wav = CCalibrationMath.tGenerateClickWav(rate, lead, period, clicks, 500);
			Check(wav[0] == 'R' && wav[8] == 'W' && wav[36] == 'd', "RIFF/WAVE/data header");
			int nSamples = (wav.Length - 44) / 2;
			Check(nSamples == (lead + period * clicks + 500) * rate / 1000, "length = lead + clicks + tail");
			// the first non-silent sample after each click time must be at (or one sample after) it
			bool onsetsOk = true;
			for (int k = 0; k < clicks; k++)
			{
				int expected = (int)(((long)lead + (long)k * period) * rate / 1000);
				int firstLoud = -1;
				for (int i = expected - 50; i < expected + 50; i++)
				{
					short s = (short)(wav[44 + i * 2] | (wav[44 + i * 2 + 1] << 8));
					if (s != 0) { firstLoud = i; break; }
				}
				if (firstLoud < expected || firstLoud > expected + 1) { onsetsOk = false; Console.WriteLine("     click " + k + ": first sample " + firstLoud + ", expected " + expected); }
			}
			Check(onsetsOk, "every click starts on its exact sample");

			Console.WriteLine(failures == 0 ? "all calibration checks passed" : failures + " calibration check(s) FAILED");
			return failures == 0 ? 0 : 1;
		}
	}
}
