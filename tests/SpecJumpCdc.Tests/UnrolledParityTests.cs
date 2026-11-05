using System.Security.Cryptography;

namespace SpecJumpCdc.Tests;

public class UnrolledParityTests
{
	[Fact]
	public void GearLsTable_MatchesGearTableShiftedByOne()
	{
		Assert.Equal(256, Table.GearTable.Length);
		Assert.Equal(256, Table.GearLsTable.Length);

		for (int i = 0; i < 256; i++)
		{
			ulong expected = unchecked(Table.GearTable[i] << 1);
			Assert.Equal(expected, Table.GearLsTable[i]);
		}
	}

	[Theory]
	[InlineData(4096)]
	[InlineData(8192)]
	[InlineData(16384)]
	[InlineData(32768)]
	[InlineData(65536)]
	[InlineData(131072)]
	[InlineData(262144)]
	public void UnrolledJumpChunking_ProducesIdenticalCutPointsAsScalar(int avgSize)
	{
		SpecJumpCdcOptions options = new(avgSize);

		// Test across multiple buffer lengths (small, medium, large, non-multiple of 6, odd, large multi-chunk)
		int[] testLengths = [512, 1024, 2048, 4096, 7777, 8192, 16384, 25000, 32768, 65536, 131072, 524288, 1048576];

		foreach (int len in testLengths)
		{
			byte[] buffer = new byte[len];
			RandomNumberGenerator.Fill(buffer);

			int offset = 0;
			while (offset < buffer.Length)
			{
				ReadOnlySpan<byte> slice = buffer.AsSpan(offset);

				int unrolledCut = SpecJumpCdc.JumpChunking(slice, in options);
				int scalarCut = SpecJumpCdc.JumpChunking(
					slice,
					options.MinSize,
					options.MaxSize,
					options.JumpLength,
					options.MaskC,
					options.MaskJ,
					out _);

				Assert.Equal(scalarCut, unrolledCut);

				offset += unrolledCut;
			}
		}
	}
}

