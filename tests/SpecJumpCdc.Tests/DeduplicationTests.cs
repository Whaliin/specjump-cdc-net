using System.Security.Cryptography;

namespace SpecJumpCdc.Tests;

public class DeduplicationTests
{
	[Fact]
	public void SpecJumpCdc_ResistsBoundaryShift_OnInsertedData()
	{
		// Create base dataset (1 MB)
		byte[] baseData = new byte[1024 * 1024];
		RandomNumberGenerator.Fill(baseData);

		// Create modified dataset: insert 17 bytes near the beginning (at offset 1000)
		byte[] modifiedData = new byte[baseData.Length + 17];
		Array.Copy(baseData, 0, modifiedData, 0, 1000);
		byte[] inserted = new byte[17];
		RandomNumberGenerator.Fill(inserted);
		Array.Copy(inserted, 0, modifiedData, 1000, 17);
		Array.Copy(baseData, 1000, modifiedData, 1017, baseData.Length - 1000);

		SpecJumpCdcOptions options = SpecJumpCdcOptions.Default4KB;

		List<ChunkData> baseChunks = SpecJumpCdc.ChunkData(baseData, options).ToList();
		List<ChunkData> modChunks = SpecJumpCdc.ChunkData(modifiedData, options).ToList();

		// Hash every chunk's byte payload with SHA256 to simulate deduplication
		HashSet<string> seenHashes = [];
		foreach (ChunkData chunk in baseChunks)
		{
			byte[] hash = SHA256.HashData(chunk.Span);
			seenHashes.Add(Convert.ToHexString(hash));
		}

		long duplicateBytes = 0;
		foreach (ChunkData chunk in modChunks)
		{
			byte[] hash = SHA256.HashData(chunk.Span);
			if (seenHashes.Contains(Convert.ToHexString(hash)))
			{
				duplicateBytes += chunk.Length;
			}
		}

		double dedupRatio = (double)duplicateBytes / modifiedData.Length;

		// Almost all data after the insertion point should resynchronize and match
		// Dedup ratio should easily be > 95%
		Assert.True(dedupRatio > 0.95, $"Expected >95% dedup ratio after boundary shift, got {dedupRatio:P2}");
	}
}

