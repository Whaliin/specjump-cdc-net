using System.IO.Pipelines;
using System.Security.Cryptography;

namespace SpecJumpCdc.Tests;

public class StreamingEquivalenceTests
{
	private sealed class ChunkedReadStream : Stream
	{
		private readonly byte[] _data;
		private readonly int _maxChunkSize;
		private int _position;

		public ChunkedReadStream(byte[] data, int maxChunkSize)
		{
			_data = data;
			_maxChunkSize = maxChunkSize;
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => _data.Length;
		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_position >= _data.Length) return 0;
			int toRead = Math.Min(count, Math.Min(_maxChunkSize, _data.Length - _position));
			Array.Copy(_data, _position, buffer, offset, toRead);
			_position += toRead;
			return toRead;
		}

		public override int Read(Span<byte> buffer)
		{
			if (_position >= _data.Length) return 0;
			int toRead = Math.Min(buffer.Length, Math.Min(_maxChunkSize, _data.Length - _position));
			_data.AsSpan(_position, toRead).CopyTo(buffer);
			_position += toRead;
			return toRead;
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			return Read(buffer.Span);
		}

		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	[Theory]
	[InlineData(1)]
	[InlineData(17)]
	[InlineData(64)]
	[InlineData(256)]
	[InlineData(1024)]
	public async Task AllStreamingVariants_MatchSpanChunking(int streamChunkSize)
	{
		byte[] data = new byte[65536];
		RandomNumberGenerator.Fill(data);

		SpecJumpCdcOptions options = SpecJumpCdcOptions.Default4KB;

		// 1. In-memory span
		List<Chunk> expected = SpecJumpCdc.Chunk(data, options).ToList();

		// 2. ChunkData enumerator
		List<ChunkData> chunkData = SpecJumpCdc.ChunkData(data, options).ToList();
		Assert.Equal(expected.Count, chunkData.Count);
		for (int i = 0; i < expected.Count; i++)
		{
			Assert.Equal(expected[i].Offset, chunkData[i].Offset);
			Assert.Equal(expected[i].Length, chunkData[i].Length);
			Assert.True(data.AsSpan((int)chunkData[i].Offset, chunkData[i].Length).SequenceEqual(chunkData[i].Span));
		}

		// 3. Sync Stream with fragmented chunk sizes
		using (ChunkedReadStream syncStream = new(data, streamChunkSize))
		{
			List<Chunk> syncChunks = SpecJumpCdc.Chunk(syncStream, options).ToList();
			Assert.Equal(expected, syncChunks);
		}

		// 4. Async Stream with fragmented chunk sizes
		using (ChunkedReadStream asyncStream = new(data, streamChunkSize))
		{
			List<Chunk> asyncChunks = [];
			await foreach (Chunk chunk in SpecJumpCdc.ChunkAsync(asyncStream, options))
			{
				asyncChunks.Add(chunk);
			}
			Assert.Equal(expected, asyncChunks);
		}

		// 5. PipeReader
		Pipe pipe = new();
		Task producer = Task.Run(async () =>
		{
			int sent = 0;
			while (sent < data.Length)
			{
				int toWrite = Math.Min(streamChunkSize, data.Length - sent);
				await pipe.Writer.WriteAsync(data.AsMemory(sent, toWrite));
				sent += toWrite;
			}
			await pipe.Writer.CompleteAsync();
		});

		List<Chunk> pipeChunks = [];
		await foreach (Chunk chunk in SpecJumpCdc.ChunkAsync(pipe.Reader, options))
		{
			pipeChunks.Add(chunk);
		}
		await producer;

		Assert.Equal(expected, pipeChunks);
	}
}

