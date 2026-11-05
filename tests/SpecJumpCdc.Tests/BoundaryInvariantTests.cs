using System.Security.Cryptography;

namespace SpecJumpCdc.Tests;

public class BoundaryInvariantTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(100)]
	[InlineData(511)]
	[InlineData(512)]
	[InlineData(513)]
	[InlineData(1024)]
	[InlineData(4096)]
	[InlineData(8192)]
	[InlineData(10000)]
	[InlineData(65536)]
	public void JumpChunking_SingleCutPoint_SatisfiesBoundaryInvariant(int payloadLength)
	{
		byte[] data = new byte[payloadLength];
		RandomNumberGenerator.Fill(data);

		SpecJumpCdcOptions options = SpecJumpCdcOptions.Default4KB;
		int cMin = options.MinSize;
		int cMax = options.MaxSize;

		int r = SpecJumpCdc.JumpChunking(data, in options);

		if (payloadLength < cMin)
		{
			// When src.Length < cMin, returned cut-point must equal src.Length
			Assert.Equal(payloadLength, r);
		}
		else
		{
			// When src.Length >= cMin, cut-point r must satisfy cMin <= r <= min(cMax, src.Length)
			int expectedUpper = Math.Min(cMax, payloadLength);
			Assert.True(r >= cMin, $"Cut point {r} was less than cMin {cMin} for length {payloadLength}.");
			Assert.True(r <= expectedUpper, $"Cut point {r} was greater than limit {expectedUpper} for length {payloadLength}.");
		}
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(511)]
	[InlineData(512)]
	[InlineData(513)]
	[InlineData(2048)]
	[InlineData(4096)]
	[InlineData(10000)]
	[InlineData(100000)]
	public void FullEnumeration_ChunksCoverBufferExactly_AndRespectBounds(int payloadLength)
	{
		byte[] data = new byte[payloadLength];
		RandomNumberGenerator.Fill(data);

		SpecJumpCdcOptions options = SpecJumpCdcOptions.Default4KB;

		List<Chunk> chunks = SpecJumpCdc.Chunk(data, options).ToList();

		if (payloadLength == 0)
		{
			Assert.Empty(chunks);
			return;
		}

		Assert.NotEmpty(chunks);

		long expectedOffset = 0L;
		long totalLength = 0L;

		for (int idx = 0; idx < chunks.Count; idx++)
		{
			Chunk chunk = chunks[idx];

			Assert.Equal(expectedOffset, chunk.Offset);
			expectedOffset += chunk.Length;
			totalLength += chunk.Length;

			bool isLastChunk = idx == chunks.Count - 1;
			if (!isLastChunk)
			{
				Assert.True(chunk.Length >= options.MinSize, $"Chunk #{idx} length {chunk.Length} < minSize {options.MinSize}.");
			}
			Assert.True(chunk.Length <= options.MaxSize, $"Chunk #{idx} length {chunk.Length} > maxSize {options.MaxSize}.");
		}

		Assert.Equal(payloadLength, totalLength);
	}

	[Theory]
	[InlineData((byte)0x00)]
	[InlineData((byte)0xFF)]
	[InlineData((byte)0x5A)]
	public void HomogeneousPayloads_SatisfyInvariants(byte fillByte)
	{
		byte[] data = new byte[65536];
		Array.Fill(data, fillByte);

		SpecJumpCdcOptions options = SpecJumpCdcOptions.Default4KB;
		List<Chunk> chunks = SpecJumpCdc.Chunk(data, options).ToList();

		Assert.NotEmpty(chunks);

		long expectedOffset = 0L;
		foreach (Chunk chunk in chunks)
		{
			Assert.Equal(expectedOffset, chunk.Offset);
			expectedOffset += chunk.Length;
			Assert.True(chunk.Length <= options.MaxSize);
		}
		Assert.Equal(data.Length, expectedOffset);
	}

	[Fact]
	public void OvershootingJump_ClampedByLimit()
	{
		// Test buffer where a jump pushes the index past limit
		byte[] data = new byte[1000];
		// cMin = 512, cMax = 800, jumpLength = 500
		// If jump occurs at 600, 600 + 500 = 1100, which overshoots limit 800.
		// Math.Min(i, limit) must clamp cut-point to limit (800).
		SpecJumpCdcOptions options = new(avgSize: 400, minSize: 200, maxSize: 800, jumpLength: 500);

		int cutPoint = SpecJumpCdc.JumpChunking(data, in options);
		Assert.True(cutPoint <= 800);
		Assert.True(cutPoint >= 200);
	}

	[Theory]
	[InlineData(131072)]
	[InlineData(262144)]
	public void LargePresets_RespectChunkBoundaries(int avgSize)
	{
		SpecJumpCdcOptions options = avgSize == 131072 ? SpecJumpCdcOptions.Default128KB : SpecJumpCdcOptions.Default256KB;
		byte[] data = new byte[avgSize * 4 + 12345];
		RandomNumberGenerator.Fill(data);

		List<Chunk> chunks = SpecJumpCdc.Chunk(data, options).ToList();
		Assert.NotEmpty(chunks);

		long offset = 0;
		for (int i = 0; i < chunks.Count; i++)
		{
			Chunk c = chunks[i];
			Assert.Equal(offset, c.Offset);
			offset += c.Length;

			bool isLast = i == chunks.Count - 1;
			if (!isLast)
			{
				Assert.True(c.Length >= options.MinSize, $"Chunk #{i} length {c.Length} < MinSize {options.MinSize}");
			}
			Assert.True(c.Length <= options.MaxSize, $"Chunk #{i} length {c.Length} > MaxSize {options.MaxSize}");
		}

		Assert.Equal(data.Length, offset);
	}
}

