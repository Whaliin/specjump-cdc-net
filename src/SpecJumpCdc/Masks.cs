using System.Numerics;

namespace SpecJumpCdc;

/// <summary>
/// Precomputed and dynamically generated masks for Speculative Jump CDC.
/// In Speculative Jump CDC:
///   cOnes = log2(cAvg) - 1
///   jOnes = log2(cAvg) - 2
///   maskJ = maskC &amp; (maskC - 1)
/// </summary>
public static class Masks
{
	/// <summary>
	/// Reference 4 KB (cAvg = 4096) maskC from Algorithm 1 (11 set bits).
	/// </summary>
	public const ulong MaskC4KB = 0x0590003570000UL;

	/// <summary>
	/// Reference 4 KB (cAvg = 4096) maskJ from Algorithm 1 (10 set bits, lowest bit of maskC cleared).
	/// </summary>
	public const ulong MaskJ4KB = 0x0590003560000UL;

	/// <summary>
	/// Reference 8 KB (cAvg = 8192) maskC (12 set bits).
	/// </summary>
	public const ulong MaskC8KB = 0x0d90003570000UL;

	/// <summary>
	/// Reference 8 KB (cAvg = 8192) maskJ (11 set bits).
	/// </summary>
	public const ulong MaskJ8KB = 0x0d90003560000UL;

	/// <summary>
	/// Reference 16 KB (cAvg = 16384) maskC (13 set bits).
	/// </summary>
	public const ulong MaskC16KB = 0x0d90103570000UL;

	/// <summary>
	/// Reference 16 KB (cAvg = 16384) maskJ (12 set bits).
	/// </summary>
	public const ulong MaskJ16KB = 0x0d90103560000UL;

	/// <summary>
	/// Reference 32 KB (cAvg = 32768) maskC (14 set bits).
	/// </summary>
	public const ulong MaskC32KB = 0x0d90303570000UL;

	/// <summary>
	/// Reference 32 KB (cAvg = 32768) maskJ (13 set bits).
	/// </summary>
	public const ulong MaskJ32KB = 0x0d90303560000UL;

	/// <summary>
	/// Reference 64 KB (cAvg = 65536) maskC (15 set bits).
	/// </summary>
	public const ulong MaskC64KB = 0x0d90313570000UL;

	/// <summary>
	/// Reference 64 KB (cAvg = 65536) maskJ (14 set bits).
	/// </summary>
	public const ulong MaskJ64KB = 0x0d90313560000UL;

	/// <summary>
	/// Reference 128 KB (cAvg = 131072) maskC (16 set bits).
	/// </summary>
	public const ulong MaskC128KB = 0x0d90333570000UL;

	/// <summary>
	/// Reference 128 KB (cAvg = 131072) maskJ (15 set bits).
	/// </summary>
	public const ulong MaskJ128KB = 0x0d90333560000UL;

	/// <summary>
	/// Reference 256 KB (cAvg = 262144) maskC (17 set bits).
	/// </summary>
	public const ulong MaskC256KB = 0x0d90373570000UL;

	/// <summary>
	/// Reference 256 KB (cAvg = 262144) maskJ (16 set bits).
	/// </summary>
	public const ulong MaskJ256KB = 0x0d90373560000UL;

	// Ordered list of 32 bit positions to set for dynamic mask generation.
	// The first 11 positions correspond to MaskC4KB, with bit 16 at index 10 being the least-significant bit.
	private static readonly int[] BitPositions =
	[
		17, 18, 20, 22, 24, 25, 40, 43, 44, 46, // 10 bits: MaskJ4KB
		16,                                     // 11th bit: completes MaskC4KB (least significant set bit)
		47,                                     // 12th bit: MaskC8KB
		32,                                     // 13th bit: MaskC16KB
		33,                                     // 14th bit: MaskC32KB
		28,                                     // 15th bit: MaskC64KB
		29, 30, 31, 34, 35, 36, 37, 38, 39, 41, 42, 45, 48, 49, 50, 51
	];

	/// <summary>
	/// Generates maskC and maskJ for a given target average chunk size.
	/// </summary>
	/// <param name="cAvg">Target average chunk size in bytes (must be >= 256).</param>
	/// <param name="maskC">Output cut-point mask with log2(cAvg) - 1 set bits.</param>
	/// <param name="maskJ">Output jump mask with log2(cAvg) - 2 set bits.</param>
	public static void GenerateMasks(int cAvg, out ulong maskC, out ulong maskJ)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(cAvg, 256);

		int bits = BitOperations.Log2((uint)cAvg);
		int cOnes = bits - 1;
		int jOnes = bits - 2;

		if (cOnes < 1 || cOnes > BitPositions.Length)
			throw new ArgumentOutOfRangeException(nameof(cAvg), cAvg, $"Calculated cOnes ({cOnes}) must be between 1 and {BitPositions.Length}.");

		ulong c = 0UL;
		for (int i = 0; i < cOnes; i++)
		{
			c |= 1UL << BitPositions[i];
		}

		maskC = c;
		maskJ = c & (c - 1UL); // Clears the least significant set bit

		// Validate bitwise invariants
		if ((maskC & maskJ) != maskJ)
			throw new InvalidOperationException("Bitwise invariant violation: (maskC & maskJ) != maskJ");
		if (BitOperations.PopCount(maskC) - BitOperations.PopCount(maskJ) != 1)
			throw new InvalidOperationException("Bitwise invariant violation: PopCount difference must be 1.");
		if (BitOperations.PopCount(maskC) != cOnes)
			throw new InvalidOperationException($"Bitwise invariant violation: maskC PopCount {BitOperations.PopCount(maskC)} != {cOnes}.");
		if (BitOperations.PopCount(maskJ) != jOnes)
			throw new InvalidOperationException($"Bitwise invariant violation: maskJ PopCount {BitOperations.PopCount(maskJ)} != {jOnes}.");
	}
}

