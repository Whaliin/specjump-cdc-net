namespace SpecJumpCdc;

/// <summary>
/// Represents a chunk identified by Speculative Jump CDC.
/// </summary>
/// <param name="Offset">The zero-based byte offset where the chunk starts in the source.</param>
/// <param name="Length">The length of the chunk in bytes.</param>
public readonly record struct Chunk(long Offset, int Length);

