namespace SpecJumpCdc;

/// <summary>
/// Enumerates chunks over a contiguous in-memory span without heap allocations.
/// </summary>
public ref struct ChunkEnumerator
{
	private readonly ReadOnlySpan<byte> _source;
	private readonly SpecJumpCdcOptions _options;
	private int _offset;
	private Chunk _current;

	internal ChunkEnumerator(ReadOnlySpan<byte> source, SpecJumpCdcOptions options)
	{
		_source = source;
		_options = options;
		_offset = 0;
		_current = default;
	}

	/// <summary>Returns this enumerator for foreach iteration.</summary>
	public readonly ChunkEnumerator GetEnumerator() => this;

	/// <summary>Gets the chunk at the current position of the enumerator.</summary>
	public readonly Chunk Current => _current;

	/// <summary>Advances the enumerator to the next chunk in the source.</summary>
	/// <returns><see langword="true"/> if a chunk was found; <see langword="false"/> if the end of the source was reached.</returns>
	public bool MoveNext()
	{
		int remaining = _source.Length - _offset;
		if (remaining <= 0)
			return false;

		int chunkSize = SpecJumpCdc.JumpChunking(_source.Slice(_offset, remaining), in _options);
		if (chunkSize <= 0)
			return false;

		_current = new Chunk(_offset, chunkSize);
		_offset += chunkSize;
		return true;
	}

	/// <summary>Collects all remaining chunks into a <see cref="List{Chunk}"/>.</summary>
	public List<Chunk> ToList()
	{
		List<Chunk> list = [];
		while (MoveNext())
			list.Add(_current);
		return list;
	}

	/// <summary>Collects all remaining chunks into an array.</summary>
	public Chunk[] ToArray() => ToList().ToArray();
}
