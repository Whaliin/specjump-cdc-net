using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace SpecJumpCdc;

/// <summary>
/// Splits binary data into variable-sized chunks using Speculative Jump Content-Defined Chunking.
/// </summary>
public static partial class SpecJumpCdc
{
	/// <summary>
	/// Splits in-memory data into chunks.
	/// </summary>
	/// <param name="source">The byte data to chunk.</param>
	/// <param name="options">The chunking options, or <see langword="null"/> to use <see cref="SpecJumpCdcOptions.Default4KB"/>.</param>
	/// <returns>A <see cref="ChunkEnumerator"/> for iterating through chunks.</returns>
	public static ChunkEnumerator Chunk(
		ReadOnlySpan<byte> source,
		SpecJumpCdcOptions? options = null)
	{
		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		return new ChunkEnumerator(source, opts);
	}

	/// <summary>
	/// Splits in-memory data into chunks along with their underlying memory slices.
	/// </summary>
	/// <param name="source">The memory buffer to chunk.</param>
	/// <param name="options">The chunking options, or <see langword="null"/> to use <see cref="SpecJumpCdcOptions.Default4KB"/>.</param>
	/// <returns>A <see cref="ChunkDataEnumerator"/> for iterating through chunks and their payloads.</returns>
	public static ChunkDataEnumerator ChunkData(
		ReadOnlyMemory<byte> source,
		SpecJumpCdcOptions? options = null)
	{
		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		return new ChunkDataEnumerator(source, opts);
	}

	/// <summary>
	/// Reads chunks sequentially from a stream.
	/// </summary>
	/// <param name="source">The stream to read.</param>
	/// <param name="options">The chunking options, or <see langword="null"/> to use <see cref="SpecJumpCdcOptions.Default4KB"/>.</param>
	/// <param name="bufferSize">The internal read buffer size in bytes, or 0 to use twice the maximum chunk size.</param>
	/// <returns>A sequence of <see cref="Chunk"/> instances read from the stream.</returns>
	public static IEnumerable<Chunk> Chunk(
		Stream source,
		SpecJumpCdcOptions? options = null,
		int bufferSize = 0)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (!source.CanRead)
			throw new ArgumentException("Stream must be readable.", nameof(source));

		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		int capacity = GetStreamBufferCapacity(in opts, bufferSize);
		return ChunkStreamCore(source, opts, capacity);
	}

	private static IEnumerable<Chunk> ChunkStreamCore(
		Stream source,
		SpecJumpCdcOptions opts,
		int capacity)
	{
		byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(capacity);

		try
		{
			int buffered = 0;
			long streamOffset = 0L;

			while (true)
			{
				int read = source.Read(rentedBuffer.AsSpan(buffered, capacity - buffered));
				buffered += read;
				bool eof = read == 0;

				int windowOffset = 0;

				while (true)
				{
					int remaining = buffered - windowOffset;
					if (remaining == 0 || (!eof && remaining < opts.MaxSize))
						break;

					int chunkSize = JumpChunking(rentedBuffer.AsSpan(windowOffset, remaining), in opts);
					if (chunkSize <= 0)
						break;

					yield return new Chunk(streamOffset + windowOffset, chunkSize);
					windowOffset += chunkSize;
				}

				streamOffset += windowOffset;

				if (eof)
					break;

				int unconsumed = buffered - windowOffset;
				if (windowOffset > 0 && unconsumed > 0)
					rentedBuffer.AsSpan(windowOffset, unconsumed).CopyTo(rentedBuffer);
				buffered = unconsumed;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedBuffer);
		}
	}

	/// <summary>
	/// Reads chunks and their byte payloads sequentially from a stream in a single pass.
	/// </summary>
	/// <param name="source">The stream to read.</param>
	/// <param name="options">The chunking options, or <see langword="null"/> to use <see cref="SpecJumpCdcOptions.Default4KB"/>.</param>
	/// <param name="bufferSize">The internal read buffer size in bytes, or 0 to use twice the maximum chunk size.</param>
	/// <returns>A sequence of <see cref="ChunkData"/> instances read from the stream.</returns>
	public static IEnumerable<ChunkData> ChunkData(
		Stream source,
		SpecJumpCdcOptions? options = null,
		int bufferSize = 0)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (!source.CanRead)
			throw new ArgumentException("Stream must be readable.", nameof(source));

		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		int capacity = GetStreamBufferCapacity(in opts, bufferSize);
		return ChunkDataStreamCore(source, opts, capacity);
	}

	private static IEnumerable<ChunkData> ChunkDataStreamCore(
		Stream source,
		SpecJumpCdcOptions opts,
		int capacity)
	{
		byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(capacity);

		try
		{
			int buffered = 0;
			long streamOffset = 0L;

			while (true)
			{
				int read = source.Read(rentedBuffer.AsSpan(buffered, capacity - buffered));
				buffered += read;
				bool eof = read == 0;

				int windowOffset = 0;

				while (true)
				{
					int remaining = buffered - windowOffset;
					if (remaining == 0 || (!eof && remaining < opts.MaxSize))
						break;

					int chunkSize = JumpChunking(rentedBuffer.AsSpan(windowOffset, remaining), in opts);
					if (chunkSize <= 0)
						break;

					yield return new ChunkData(
						streamOffset + windowOffset,
						chunkSize,
						rentedBuffer.AsMemory(windowOffset, chunkSize));
					windowOffset += chunkSize;
				}

				streamOffset += windowOffset;

				if (eof)
					break;

				int unconsumed = buffered - windowOffset;
				if (windowOffset > 0 && unconsumed > 0)
					rentedBuffer.AsSpan(windowOffset, unconsumed).CopyTo(rentedBuffer);
				buffered = unconsumed;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedBuffer);
		}
	}

	/// <summary>
	/// Reads chunks asynchronously from a stream.
	/// </summary>
	public static IAsyncEnumerable<Chunk> ChunkAsync(
		Stream source,
		SpecJumpCdcOptions? options = null,
		int bufferSize = 0,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (!source.CanRead)
			throw new ArgumentException("Stream must be readable.", nameof(source));

		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		int capacity = GetStreamBufferCapacity(in opts, bufferSize);
		return ChunkStreamAsyncCore(source, opts, capacity, cancellationToken);
	}

	private static async IAsyncEnumerable<Chunk> ChunkStreamAsyncCore(
		Stream source,
		SpecJumpCdcOptions opts,
		int capacity,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(capacity);

		try
		{
			int buffered = 0;
			long streamOffset = 0L;

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				int read = await source.ReadAsync(rentedBuffer.AsMemory(buffered, capacity - buffered), cancellationToken).ConfigureAwait(false);
				buffered += read;
				bool eof = read == 0;

				int windowOffset = 0;

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();

					int remaining = buffered - windowOffset;
					if (remaining == 0 || (!eof && remaining < opts.MaxSize))
						break;

					int chunkSize = JumpChunking(rentedBuffer.AsSpan(windowOffset, remaining), in opts);
					if (chunkSize <= 0)
						break;

					yield return new Chunk(streamOffset + windowOffset, chunkSize);
					windowOffset += chunkSize;
				}

				streamOffset += windowOffset;

				if (eof)
					break;

				int unconsumed = buffered - windowOffset;
				if (windowOffset > 0 && unconsumed > 0)
					rentedBuffer.AsSpan(windowOffset, unconsumed).CopyTo(rentedBuffer);
				buffered = unconsumed;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedBuffer);
		}
	}

	/// <summary>
	/// Reads chunks and payloads asynchronously from a stream in a single pass.
	/// </summary>
	public static IAsyncEnumerable<ChunkData> ChunkDataAsync(
		Stream source,
		SpecJumpCdcOptions? options = null,
		int bufferSize = 0,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (!source.CanRead)
			throw new ArgumentException("Stream must be readable.", nameof(source));

		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		int capacity = GetStreamBufferCapacity(in opts, bufferSize);
		return ChunkDataStreamAsyncCore(source, opts, capacity, cancellationToken);
	}

	private static async IAsyncEnumerable<ChunkData> ChunkDataStreamAsyncCore(
		Stream source,
		SpecJumpCdcOptions opts,
		int capacity,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(capacity);

		try
		{
			int buffered = 0;
			long streamOffset = 0L;

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				int read = await source.ReadAsync(rentedBuffer.AsMemory(buffered, capacity - buffered), cancellationToken).ConfigureAwait(false);
				buffered += read;
				bool eof = read == 0;

				int windowOffset = 0;

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();

					int remaining = buffered - windowOffset;
					if (remaining == 0 || (!eof && remaining < opts.MaxSize))
						break;

					int chunkSize = JumpChunking(rentedBuffer.AsSpan(windowOffset, remaining), in opts);
					if (chunkSize <= 0)
						break;

					yield return new ChunkData(
						streamOffset + windowOffset,
						chunkSize,
						rentedBuffer.AsMemory(windowOffset, chunkSize));
					windowOffset += chunkSize;
				}

				streamOffset += windowOffset;

				if (eof)
					break;

				int unconsumed = buffered - windowOffset;
				if (windowOffset > 0 && unconsumed > 0)
					rentedBuffer.AsSpan(windowOffset, unconsumed).CopyTo(rentedBuffer);
				buffered = unconsumed;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedBuffer);
		}
	}

	/// <summary>
	/// Reads chunks asynchronously from a pipeline reader without intermediate buffer copies.
	/// </summary>
	public static IAsyncEnumerable<Chunk> ChunkAsync(
		PipeReader reader,
		SpecJumpCdcOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reader);
		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		return ChunkPipeAsyncCore(reader, opts, cancellationToken);
	}

	private static async IAsyncEnumerable<Chunk> ChunkPipeAsyncCore(
		PipeReader reader,
		SpecJumpCdcOptions opts,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		long streamOffset = 0L;
		byte[]? scratchBuffer = null;

		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
				ReadOnlySequence<byte> buffer = result.Buffer;
				SequencePosition consumed = buffer.Start;
				SequencePosition examined = buffer.End;

				try
				{
					while (buffer.Length > 0)
					{
						cancellationToken.ThrowIfCancellationRequested();

						if (!result.IsCompleted && buffer.Length < opts.MaxSize)
							break;

						ReadOnlySpan<byte> span;
						int scanLength;

						if (buffer.FirstSpan.Length >= opts.MaxSize)
						{
							span = buffer.FirstSpan;
							scanLength = opts.MaxSize;
						}
						else if (buffer.IsSingleSegment)
						{
							span = buffer.FirstSpan;
							scanLength = span.Length;
						}
						else
						{
							scratchBuffer ??= ArrayPool<byte>.Shared.Rent(opts.MaxSize);
							scanLength = (int)Math.Min(buffer.Length, opts.MaxSize);
							buffer.Slice(0, scanLength).CopyTo(scratchBuffer);
							span = scratchBuffer.AsSpan(0, scanLength);
						}

						int chunkSize = JumpChunking(span.Slice(0, scanLength), in opts);
						if (chunkSize <= 0)
							break;

						yield return new Chunk(streamOffset, chunkSize);
						streamOffset += chunkSize;
						buffer = buffer.Slice(chunkSize);
						consumed = buffer.Start;
					}
				}
				finally
				{
					reader.AdvanceTo(consumed, examined);
				}

				if (result.IsCompleted)
					break;
			}
		}
		finally
		{
			if (scratchBuffer != null)
				ArrayPool<byte>.Shared.Return(scratchBuffer);
		}
	}

	/// <summary>
	/// Reads chunks and payloads asynchronously from a pipeline reader in a single pass.
	/// </summary>
	public static IAsyncEnumerable<ChunkData> ChunkDataAsync(
		PipeReader reader,
		SpecJumpCdcOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reader);
		SpecJumpCdcOptions opts = options ?? SpecJumpCdcOptions.Default4KB;
		return ChunkDataPipeAsyncCore(reader, opts, cancellationToken);
	}

	private static async IAsyncEnumerable<ChunkData> ChunkDataPipeAsyncCore(
		PipeReader reader,
		SpecJumpCdcOptions opts,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		long streamOffset = 0L;
		byte[]? scratchBuffer = null;

		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
				ReadOnlySequence<byte> buffer = result.Buffer;
				SequencePosition consumed = buffer.Start;
				SequencePosition examined = buffer.End;

				try
				{
					while (buffer.Length > 0)
					{
						cancellationToken.ThrowIfCancellationRequested();

						if (!result.IsCompleted && buffer.Length < opts.MaxSize)
							break;

						int chunkSize;
						ReadOnlyMemory<byte> chunkMemory;

						if (buffer.FirstSpan.Length >= opts.MaxSize)
						{
							ReadOnlyMemory<byte> first = buffer.First;
							chunkSize = JumpChunking(first.Span.Slice(0, opts.MaxSize), in opts);
							chunkMemory = first.Slice(0, chunkSize);
						}
						else if (buffer.IsSingleSegment)
						{
							ReadOnlyMemory<byte> first = buffer.First;
							chunkSize = JumpChunking(first.Span, in opts);
							chunkMemory = first.Slice(0, chunkSize);
						}
						else
						{
							scratchBuffer ??= ArrayPool<byte>.Shared.Rent(opts.MaxSize);
							int scanLength = (int)Math.Min(buffer.Length, opts.MaxSize);
							buffer.Slice(0, scanLength).CopyTo(scratchBuffer);
							chunkSize = JumpChunking(scratchBuffer.AsSpan(0, scanLength), in opts);
							chunkMemory = scratchBuffer.AsMemory(0, chunkSize);
						}

						if (chunkSize <= 0)
							break;

						yield return new ChunkData(streamOffset, chunkSize, chunkMemory);
						streamOffset += chunkSize;
						buffer = buffer.Slice(chunkSize);
						consumed = buffer.Start;
					}
				}
				finally
				{
					reader.AdvanceTo(consumed, examined);
				}

				if (result.IsCompleted)
					break;
			}
		}
		finally
		{
			if (scratchBuffer != null)
				ArrayPool<byte>.Shared.Return(scratchBuffer);
		}
	}

	private static int GetStreamBufferCapacity(in SpecJumpCdcOptions opts, int bufferSize)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);

		long readSize = bufferSize > 0 ? bufferSize : (long)opts.MaxSize * 2;
		long capacity = Math.Min(readSize + opts.MaxSize, Array.MaxLength);
		return (int)capacity;
	}
}
