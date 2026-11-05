namespace SpecJumpCdc.Tests;

public class ValidationTests
{
	[Fact]
	public void Options_NonPositiveSizes_ThrowExceptions()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new SpecJumpCdcOptions(avgSize: 4096, minSize: 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new SpecJumpCdcOptions(avgSize: 4096, minSize: -10));
		Assert.Throws<ArgumentOutOfRangeException>(() => new SpecJumpCdcOptions(avgSize: 100, minSize: 500));
		Assert.Throws<ArgumentException>(() => new SpecJumpCdcOptions(avgSize: 4096, minSize: 512, maxSize: 2000));
		Assert.Throws<ArgumentOutOfRangeException>(() => new SpecJumpCdcOptions(avgSize: 4096, minSize: 512, jumpLength: 0));
		Assert.Throws<ArgumentException>(() => new SpecJumpCdcOptions(avgSize: 4096, minSize: 512, maxSize: 8192, jumpLength: 8192));
	}

	[Fact]
	public async Task Stream_NullOrUnreadable_ThrowsExceptions()
	{
		Assert.Throws<ArgumentNullException>(() => SpecJumpCdc.Chunk((Stream)null!).ToList());
		Assert.Throws<ArgumentNullException>(() => SpecJumpCdc.ChunkData((Stream)null!).ToList());

		await Assert.ThrowsAsync<ArgumentNullException>(async () =>
		{
			await foreach (Chunk _ in SpecJumpCdc.ChunkAsync((Stream)null!)) { }
		});

		await Assert.ThrowsAsync<ArgumentNullException>(async () =>
		{
			await foreach (ChunkData _ in SpecJumpCdc.ChunkDataAsync((Stream)null!)) { }
		});

		await Assert.ThrowsAsync<ArgumentNullException>(async () =>
		{
			await foreach (Chunk _ in SpecJumpCdc.ChunkAsync((System.IO.Pipelines.PipeReader)null!)) { }
		});

		using MemoryStream stream = new([1, 2, 3]);
		Assert.Throws<ArgumentOutOfRangeException>(() => SpecJumpCdc.Chunk(stream, bufferSize: -1).ToList());
		Assert.Throws<ArgumentOutOfRangeException>(() => SpecJumpCdc.ChunkData(stream, bufferSize: -1).ToList());
	}
}

