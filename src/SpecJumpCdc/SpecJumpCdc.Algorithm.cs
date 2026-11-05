using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpecJumpCdc;

public static partial class SpecJumpCdc
{
	/// <summary>
	/// Finds the next cut-point in the provided source buffer using the Speculative Jump CDC algorithm.
	/// </summary>
	/// <param name="src">The source byte data to chunk.</param>
	/// <param name="cMin">Minimum chunk size in bytes.</param>
	/// <param name="cMax">Maximum chunk size in bytes.</param>
	/// <param name="jumpLength">Speculative jump distance in bytes.</param>
	/// <param name="maskC">The cut-condition mask.</param>
	/// <param name="maskJ">The jump-condition mask (must be a subset of maskC).</param>
	/// <returns>The chunk length (cut-point relative to the start of <paramref name="src"/>).</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int JumpChunking(
		ReadOnlySpan<byte> src,
		int cMin,
		int cMax,
		int jumpLength,
		ulong maskC,
		ulong maskJ)
	{
		return JumpChunking(src, cMin, cMax, jumpLength, maskC, maskJ, maskC << 1, maskJ << 1);
	}

	/// <summary>
	/// Finds the next cut-point in the provided source buffer using the Speculative Jump CDC algorithm with pre-shifted masks.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int JumpChunking(
		ReadOnlySpan<byte> src,
		int cMin,
		int cMax,
		int jumpLength,
		ulong maskC,
		ulong maskJ,
		ulong maskCLs,
		ulong maskJLs)
	{
		int limit = Math.Min(cMax, src.Length);
		if (limit <= cMin)
			return limit;

		int i = cMin;
		ulong fp = 0UL;

		ref byte srcRef = ref MemoryMarshal.GetReference(src);
		ref ulong gearRef = ref MemoryMarshal.GetReference(Table.GearTable);
		ref ulong gearLsRef = ref MemoryMarshal.GetReference(Table.GearLsTable);

		unchecked
		{
			// Unrolled 6-byte block: requires 8 readable bytes in-bounds before 'limit'
			// so that Unsafe.ReadUnaligned<ulong> never reads past the buffer end.
			while (i + 8 <= limit)
			{
				ulong block = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref srcRef, (nint)(uint)i));
				if (!BitConverter.IsLittleEndian)
					block = BinaryPrimitives.ReverseEndianness(block);

				ulong g0 = Unsafe.Add(ref gearLsRef, (nint)(byte)block);
				ulong g1 = Unsafe.Add(ref gearRef, (nint)(byte)(block >> 8));
				ulong g2 = Unsafe.Add(ref gearLsRef, (nint)(byte)(block >> 16));
				ulong g3 = Unsafe.Add(ref gearRef, (nint)(byte)(block >> 24));
				ulong g4 = Unsafe.Add(ref gearLsRef, (nint)(byte)(block >> 32));
				ulong g5 = Unsafe.Add(ref gearRef, (nint)(byte)(block >> 40));

				// Pair 0: byte 0 (offset i)
				fp = (fp << 2) + g0;
				if ((fp & maskJLs) == 0)
				{
					if ((fp & maskCLs) == 0)
						return i;

					fp = 0UL;
					i += jumpLength + 1;
					continue;
				}

				// Pair 0: byte 1 (offset i + 1)
				fp += g1;
				if ((fp & maskJ) == 0)
				{
					if ((fp & maskC) == 0)
						return i + 1;

					fp = 0UL;
					i += jumpLength + 2;
					continue;
				}

				// Pair 1: byte 2 (offset i + 2)
				fp = (fp << 2) + g2;
				if ((fp & maskJLs) == 0)
				{
					if ((fp & maskCLs) == 0)
						return i + 2;

					fp = 0UL;
					i += jumpLength + 3;
					continue;
				}

				// Pair 1: byte 3 (offset i + 3)
				fp += g3;
				if ((fp & maskJ) == 0)
				{
					if ((fp & maskC) == 0)
						return i + 3;

					fp = 0UL;
					i += jumpLength + 4;
					continue;
				}

				// Pair 2: byte 4 (offset i + 4)
				fp = (fp << 2) + g4;
				if ((fp & maskJLs) == 0)
				{
					if ((fp & maskCLs) == 0)
						return i + 4;

					fp = 0UL;
					i += jumpLength + 5;
					continue;
				}

				// Pair 2: byte 5 (offset i + 5)
				fp += g5;
				if ((fp & maskJ) == 0)
				{
					if ((fp & maskC) == 0)
						return i + 5;

					fp = 0UL;
					i += jumpLength + 6;
					continue;
				}

				i += 6;
			}

			// Scalar remainder loop for the remaining bytes (< 8 bytes)
			while (i < limit)
			{
				byte b = Unsafe.Add(ref srcRef, (nint)(uint)i);
				fp = (fp << 1) + Unsafe.Add(ref gearRef, (nint)b);

				if ((fp & maskJ) == 0)
				{
					if ((fp & maskC) == 0)
						return i;

					fp = 0UL;
					i += jumpLength;
				}

				i++;
			}
		}

		return Math.Min(i, limit);
	}

	/// <summary>
	/// Finds the next cut-point in the provided source buffer using the configured options.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int JumpChunking(ReadOnlySpan<byte> src, in SpecJumpCdcOptions options)
	{
		return JumpChunking(src, options.MinSize, options.MaxSize, options.JumpLength, options.MaskC, options.MaskJ, options.MaskCLs, options.MaskJLs);
	}

	/// <summary>
	/// Diagnostic version of <see cref="JumpChunking(ReadOnlySpan{byte}, int, int, int, ulong, ulong)"/>
	/// that tracks the exact count of rolling hash evaluations performed during boundary searching.
	/// </summary>
	public static int JumpChunking(
		ReadOnlySpan<byte> src,
		int cMin,
		int cMax,
		int jumpLength,
		ulong maskC,
		ulong maskJ,
		out int hashEvaluations)
	{
		int limit = Math.Min(cMax, src.Length);
		if (limit <= cMin)
		{
			hashEvaluations = 0;
			return limit;
		}

		int i = cMin;
		ulong fp = 0UL;
		int evaluations = 0;

		ref byte srcRef = ref MemoryMarshal.GetReference(src);
		ref ulong gearRef = ref MemoryMarshal.GetReference(Table.GearTable);

		while (i < limit)
		{
			byte b = Unsafe.Add(ref srcRef, (nint)(uint)i);
			fp = unchecked((fp << 1) + Unsafe.Add(ref gearRef, (nint)b));
			evaluations++;

			if ((fp & maskJ) == 0)
			{
				if ((fp & maskC) == 0)
				{
					hashEvaluations = evaluations;
					return i;
				}

				fp = 0UL;
				i += jumpLength;
			}

			i++;
		}

		hashEvaluations = evaluations;
		return Math.Min(i, limit);
	}
}
