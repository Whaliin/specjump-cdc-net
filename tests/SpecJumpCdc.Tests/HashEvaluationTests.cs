using System.Security.Cryptography;

namespace SpecJumpCdc.Tests;

public class HashEvaluationTests
{
	[Fact]
	public void SpeculativeJump_SignificantlyReducesInnerLoopEvaluations()
	{
		// Generate 1 MB of test data
		byte[] data = new byte[1024 * 1024];
		RandomNumberGenerator.Fill(data);

		SpecJumpCdcOptions options = SpecJumpCdcOptions.Default4KB;

		int totalJumpEvaluations = 0;
		int totalSequentialEvaluations = 0;

		// 1. Measure total evaluations in Speculative Jump CDC across the dataset
		int offset = 0;
		while (offset < data.Length)
		{
			int remaining = data.Length - offset;
			ReadOnlySpan<byte> slice = data.AsSpan(offset, remaining);

			int cut = SpecJumpCdc.JumpChunking(
				slice,
				options.MinSize,
				options.MaxSize,
				options.JumpLength,
				options.MaskC,
				options.MaskJ,
				out int evaluations);

			totalJumpEvaluations += evaluations;
			offset += cut;
		}

		// 2. Measure total evaluations in vanilla byte-by-byte Gear CDC across the same dataset
		offset = 0;
		while (offset < data.Length)
		{
			int remaining = data.Length - offset;
			ReadOnlySpan<byte> slice = data.AsSpan(offset, remaining);

			int limit = Math.Min(options.MaxSize, remaining);
			if (limit <= options.MinSize)
			{
				offset += limit;
				continue;
			}

			int cut = limit;
			ulong fp = 0UL;
			for (int i = options.MinSize; i < limit; i++)
			{
				fp = unchecked((fp << 1) + Table.GearTable[slice[i]]);
				totalSequentialEvaluations++;
				if ((fp & options.MaskC) == 0)
				{
					cut = i;
					break;
				}
			}
			offset += cut;
		}

		Assert.True(totalSequentialEvaluations > 0);
		double evaluationRatio = (double)totalJumpEvaluations / totalSequentialEvaluations;

		// Paper Section III-C / Insight:
		// "JC can reduce the calculation of fingerprinting by 50% compared with Gear-based CDC."
		// Evaluation ratio should be roughly ~0.5 (allowing empirical variance of [0.40, 0.65])
		Assert.InRange(evaluationRatio, 0.40, 0.65);
	}
}
