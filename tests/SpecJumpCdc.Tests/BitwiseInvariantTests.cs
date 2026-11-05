using System.Numerics;

namespace SpecJumpCdc.Tests;

public class BitwiseInvariantTests
{
	[Theory]
	[InlineData(4096, Masks.MaskC4KB, Masks.MaskJ4KB, 11, 10)]
	[InlineData(8192, Masks.MaskC8KB, Masks.MaskJ8KB, 12, 11)]
	[InlineData(16384, Masks.MaskC16KB, Masks.MaskJ16KB, 13, 12)]
	[InlineData(32768, Masks.MaskC32KB, Masks.MaskJ32KB, 14, 13)]
	[InlineData(65536, Masks.MaskC64KB, Masks.MaskJ64KB, 15, 14)]
	[InlineData(131072, Masks.MaskC128KB, Masks.MaskJ128KB, 16, 15)]
	[InlineData(262144, Masks.MaskC256KB, Masks.MaskJ256KB, 17, 16)]
	public void PrecomputedMasks_SatisfyAllBitwiseInvariants(int cAvg, ulong maskC, ulong maskJ, int expectedCOnes, int expectedJOnes)
	{
		// 1. Invariant: (maskC & maskJ) == maskJ (maskJ is a strict bitwise subset of maskC)
		Assert.Equal(maskJ, maskC & maskJ);

		// 2. Invariant: PopCount difference is exactly 1
		int popC = BitOperations.PopCount(maskC);
		int popJ = BitOperations.PopCount(maskJ);
		Assert.Equal(1, popC - popJ);

		// 3. Expected bit counts: log2(cAvg) - 1 and log2(cAvg) - 2
		Assert.Equal(BitOperations.Log2((uint)cAvg) - 1, expectedCOnes);
		Assert.Equal(BitOperations.Log2((uint)cAvg) - 2, expectedJOnes);
		Assert.Equal(expectedCOnes, popC);
		Assert.Equal(expectedJOnes, popJ);

		// 4. maskJ is derived by clearing the least-significant set bit from maskC
		Assert.Equal(maskJ, maskC & (maskC - 1UL));
	}

	[Fact]
	public void Algorithm1_Constants_MatchPaperSpecification()
	{
		// Reference constants from Algorithm 1 of the paper:
		// maskC = 0x590003570000 (11 non-zero bits)
		// maskJ = 0x590003560000 (10 non-zero bits)
		const ulong expectedMaskC = 0x0590003570000UL;
		const ulong expectedMaskJ = 0x0590003560000UL;

		Assert.Equal(expectedMaskC, Masks.MaskC4KB);
		Assert.Equal(expectedMaskJ, Masks.MaskJ4KB);

		Assert.Equal(11, BitOperations.PopCount(Masks.MaskC4KB));
		Assert.Equal(10, BitOperations.PopCount(Masks.MaskJ4KB));
		Assert.Equal(Masks.MaskJ4KB, Masks.MaskC4KB & Masks.MaskJ4KB);
		Assert.Equal(Masks.MaskJ4KB, Masks.MaskC4KB & (Masks.MaskC4KB - 1UL));
	}

	[Theory]
	[InlineData(256)]
	[InlineData(512)]
	[InlineData(1024)]
	[InlineData(2048)]
	[InlineData(4096)]
	[InlineData(8192)]
	[InlineData(16384)]
	[InlineData(32768)]
	[InlineData(65536)]
	[InlineData(131072)]
	[InlineData(262144)]
	[InlineData(524288)]
	[InlineData(1048576)]
	public void DynamicMaskGeneration_SatisfiesBitwiseInvariantsAcrossPowersOfTwo(int cAvg)
	{
		Masks.GenerateMasks(cAvg, out ulong maskC, out ulong maskJ);

		int expectedCOnes = BitOperations.Log2((uint)cAvg) - 1;
		int expectedJOnes = BitOperations.Log2((uint)cAvg) - 2;

		Assert.Equal(expectedCOnes, BitOperations.PopCount(maskC));
		Assert.Equal(expectedJOnes, BitOperations.PopCount(maskJ));
		Assert.Equal(maskJ, maskC & maskJ);
		Assert.Equal(1, BitOperations.PopCount(maskC) - BitOperations.PopCount(maskJ));
		Assert.Equal(maskJ, maskC & (maskC - 1UL));
	}

	[Fact]
	public void OptionsConstructor_AssertsBitwiseInvariants()
	{
		// Invalid: maskJ has a bit not present in maskC
		ulong invalidSubsetMaskC = 0b0011UL;
		ulong invalidSubsetMaskJ = 0b0100UL;
		Assert.Throws<ArgumentException>(() => new SpecJumpCdcOptions(4096, 512, 8192, 2048, invalidSubsetMaskC, invalidSubsetMaskJ));

		// Invalid: difference in popcount is not 1 (difference is 2)
		ulong invalidPopDiffMaskC = 0b0111UL; // 3 bits
		ulong invalidPopDiffMaskJ = 0b0001UL; // 1 bit
		Assert.Throws<ArgumentException>(() => new SpecJumpCdcOptions(4096, 512, 8192, 2048, invalidPopDiffMaskC, invalidPopDiffMaskJ));
	}
}
