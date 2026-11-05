namespace SpecJumpCdc;

/// <summary>
/// Represents a chunk identified by Speculative Jump CDC along with its underlying memory slice.
/// </summary>
/// <param name="Offset">The zero-based byte offset where the chunk starts in the source.</param>
/// <param name="Length">The length of the chunk in bytes.</param>
/// <param name="Memory">The memory slice containing the chunk payload.</param>
public readonly record struct ChunkData(long Offset, int Length, ReadOnlyMemory<byte> Memory)
{
	/// <summary>Returns a copy of the chunk payload as a byte array.</summary>
	public byte[] ToArray() => Memory.ToArray();

	/// <summary>Gets a span over the chunk payload.</summary>
	public ReadOnlySpan<byte> Span => Memory.Span;
}

