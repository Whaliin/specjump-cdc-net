using System.Numerics;

namespace SpecJumpCdc;

/// <summary>
/// Configuration options for Speculative Jump CDC chunking.
/// </summary>
public readonly record struct SpecJumpCdcOptions
{
	/// <summary>Default minimum chunk size in bytes (512 B).</summary>
	public const int DefaultMin = 512;

	/// <summary>Default average chunk size in bytes (4 KB).</summary>
	public const int DefaultAvg = 4096;

	/// <summary>Default preset for ~4 KB average chunks (512 B min, 4 KB avg, 8 KB max, 2 KB jump).</summary>
	public static SpecJumpCdcOptions Default4KB { get; } = new(4096, 512, 8192, 2048, Masks.MaskC4KB, Masks.MaskJ4KB);

	/// <summary>Default preset for ~8 KB average chunks (4 KB min, 8 KB avg, 16 KB max, 4 KB jump).</summary>
	public static SpecJumpCdcOptions Default8KB { get; } = new(8192, 4096, 16384, 4096, Masks.MaskC8KB, Masks.MaskJ8KB);

	/// <summary>Default preset for ~16 KB average chunks (8 KB min, 16 KB avg, 32 KB max, 8 KB jump).</summary>
	public static SpecJumpCdcOptions Default16KB { get; } = new(16384, 8192, 32768, 8192, Masks.MaskC16KB, Masks.MaskJ16KB);

	/// <summary>Default preset for ~32 KB average chunks (16 KB min, 32 KB avg, 64 KB max, 16 KB jump).</summary>
	public static SpecJumpCdcOptions Default32KB { get; } = new(32768, 16384, 65536, 16384, Masks.MaskC32KB, Masks.MaskJ32KB);

	/// <summary>Default preset for ~64 KB average chunks (32 KB min, 64 KB avg, 128 KB max, 32 KB jump).</summary>
	public static SpecJumpCdcOptions Default64KB { get; } = new(65536, 32768, 131072, 32768, Masks.MaskC64KB, Masks.MaskJ64KB);

	/// <summary>Default preset for ~128 KB average chunks (64 KB min, 128 KB avg, 256 KB max, 64 KB jump).</summary>
	public static SpecJumpCdcOptions Default128KB { get; } = new(131072, 65536, 262144, 65536, Masks.MaskC128KB, Masks.MaskJ128KB);

	/// <summary>Default preset for ~256 KB average chunks (128 KB min, 256 KB avg, 512 KB max, 128 KB jump).</summary>
	public static SpecJumpCdcOptions Default256KB { get; } = new(262144, 131072, 524288, 131072, Masks.MaskC256KB, Masks.MaskJ256KB);

	/// <summary>Minimum allowed size for any generated chunk.</summary>
	public int MinSize { get; }

	/// <summary>Target average size for generated chunks.</summary>
	public int AvgSize { get; }

	/// <summary>Maximum allowed size for any generated chunk.</summary>
	public int MaxSize { get; }

	/// <summary>Speculative jump distance in bytes when the jump condition is satisfied.</summary>
	public int JumpLength { get; }

	/// <summary>Mask used for detecting cut-points (fp &amp; MaskC == 0).</summary>
	public ulong MaskC { get; }

	/// <summary>Mask used for triggering speculative jumps (fp &amp; MaskJ == 0).</summary>
	public ulong MaskJ { get; }

	/// <summary>Left-shifted MaskC (MaskC &lt;&lt; 1) used for the first byte of each pair in unrolled blocks.</summary>
	public ulong MaskCLs { get; }

	/// <summary>Left-shifted MaskJ (MaskJ &lt;&lt; 1) used for the first byte of each pair in unrolled blocks.</summary>
	public ulong MaskJLs { get; }

	/// <summary>
	/// Initializes a new <see cref="SpecJumpCdcOptions"/> instance with the specified chunk size bounds and masks.
	/// </summary>
	/// <param name="avgSize">Target average chunk size in bytes (defaults to 4096).</param>
	/// <param name="minSize">Minimum chunk size in bytes (defaults to 512).</param>
	/// <param name="maxSize">Maximum chunk size in bytes (defaults to 2 * avgSize).</param>
	/// <param name="jumpLength">Speculative jump distance in bytes (defaults to avgSize / 2).</param>
	/// <param name="maskC">Optional custom cut mask. If omitted, dynamically generated based on avgSize.</param>
	/// <param name="maskJ">Optional custom jump mask. If omitted, dynamically derived by clearing a single set bit from maskC.</param>
	/// <exception cref="ArgumentOutOfRangeException">Thrown if any size parameter is non-positive.</exception>
	/// <exception cref="ArgumentException">Thrown if size constraints or mask bitwise invariants are violated.</exception>
	public SpecJumpCdcOptions(
		int avgSize = DefaultAvg,
		int? minSize = null,
		int? maxSize = null,
		int? jumpLength = null,
		ulong? maskC = null,
		ulong? maskJ = null)
	{
		int resolvedMin = minSize ?? (avgSize <= 4096 ? 512 : avgSize / 2);
		ArgumentOutOfRangeException.ThrowIfLessThan(resolvedMin, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(avgSize, resolvedMin);

		int resolvedMax = maxSize ?? (avgSize * 2);
		if (resolvedMax < avgSize)
			throw new ArgumentException("Maximum chunk size must not be less than average chunk size.", nameof(maxSize));

		int resolvedJump = jumpLength ?? (avgSize / 2);
		ArgumentOutOfRangeException.ThrowIfLessThan(resolvedJump, 1);
		if (resolvedJump >= resolvedMax)
			throw new ArgumentException("Jump distance must be smaller than maximum chunk size.", nameof(jumpLength));

		ulong resolvedMaskC;
		ulong resolvedMaskJ;

		if (maskC.HasValue && maskJ.HasValue)
		{
			resolvedMaskC = maskC.Value;
			resolvedMaskJ = maskJ.Value;
		}
		else if (maskC.HasValue)
		{
			resolvedMaskC = maskC.Value;
			resolvedMaskJ = resolvedMaskC & (resolvedMaskC - 1UL);
		}
		else
		{
			Masks.GenerateMasks(avgSize, out resolvedMaskC, out resolvedMaskJ);
		}

		// Bitwise invariant checks:
		// 1. (maskC & maskJ) == maskJ
		if ((resolvedMaskC & resolvedMaskJ) != resolvedMaskJ)
			throw new ArgumentException("Bitwise invariant violation: (maskC & maskJ) must equal maskJ.", nameof(maskJ));

		// 2. PopCount(maskC) - PopCount(maskJ) == 1
		int popC = BitOperations.PopCount(resolvedMaskC);
		int popJ = BitOperations.PopCount(resolvedMaskJ);
		if (popC - popJ != 1)
			throw new ArgumentException($"Bitwise invariant violation: PopCount(maskC) [{popC}] - PopCount(maskJ) [{popJ}] must equal 1.", nameof(maskC));

		MinSize = resolvedMin;
		AvgSize = avgSize;
		MaxSize = resolvedMax;
		JumpLength = resolvedJump;
		MaskC = resolvedMaskC;
		MaskJ = resolvedMaskJ;
		MaskCLs = resolvedMaskC << 1;
		MaskJLs = resolvedMaskJ << 1;
	}
}

