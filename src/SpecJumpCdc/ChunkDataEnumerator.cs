namespace SpecJumpCdc;

/// <summary>
/// Enumerates chunks and their underlying memory payloads over a contiguous buffer without heap allocations.
/// </summary>
public ref struct ChunkDataEnumerator
{
	private readonly ReadOnlyMemory<byte> _source;
	private readonly SpecJumpCdcOptions _options;
	private int _offset;
	private ChunkData _current;

	internal ChunkDataEnumerator(ReadOnlyMemory<byte> source, SpecJumpCdcOptions options)
	{
		_source = source;
		_options = options;
		_offset = 0;
		_current = default;
	}

	/// <summary>Returns this enumerator for foreach iteration.</summary>
	public readonly ChunkDataEnumerator GetEnumerator() => this;

	/// <summary>Gets the chunk and payload at the current position of the enumerator.</summary>
	public readonly ChunkData Current => _current;

	/// <summary>Advances the enumerator to the next chunk in the source.</summary>
	/// <returns><see langword="true"/> if a chunk was found; <see langword="false"/> if the end of the source was reached.</returns>
	public bool MoveNext()
	{
		int remaining = _source.Length - _offset;
		if (remaining <= 0)
			return false;

		int chunkSize = SpecJumpCdc.JumpChunking(_source.Span.Slice(_offset, remaining), in _options);
		if (chunkSize <= 0)
			return false;

		_current = new ChunkData(_offset, chunkSize, _source.Slice(_offset, chunkSize));
		_offset += chunkSize;
		return true;
	}

	/// <summary>Collects all remaining chunks into a <see cref="List{ChunkData}"/>.</summary>
	public List<ChunkData> ToList()
	{
		List<ChunkData> list = [];
		while (MoveNext())
			list.Add(_current);
		return list;
	}

	/// <summary>Collects all remaining chunks into an array.</summary>
	public ChunkData[] ToArray() => ToList().ToArray();
}
