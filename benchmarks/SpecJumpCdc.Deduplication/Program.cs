using System.Diagnostics;
using System.Globalization;
using System.IO.Hashing;
using System.Text;
using FastCdc;

namespace SpecJumpCdc.Deduplication;

public static class Program
{
	public static void Main(string[] args)
	{
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string outputDir = Path.Combine("Deduplication.Artifacts", timestamp);
		Directory.CreateDirectory(outputDir);

		List<DeduplicationResult> results = [];

		// Target presets to evaluate
		(string label, int avgSize)[] presets =
		[
			("4 KB", 4096),
			("8 KB", 8192),
			("16 KB", 16384),
			("32 KB", 32768),
			("64 KB", 65536),
			("128 KB", 131072),
			("256 KB", 262144)
		];

		// Load base fixtures
		string shakespearePath = ResolveFixturePath("shakespeare.txt");
		string tsunamiPath = ResolveFixturePath("tsunami.tiff");

		byte[] shakespeare = File.ReadAllBytes(shakespearePath);
		byte[] tsunami = File.ReadAllBytes(tsunamiPath);

		// Generate multi-version incremental datasets
		List<byte[]> shakespeareVersions = GenerateFileVersions(shakespeare);
		List<byte[]> tsunamiVersions = GenerateFileVersions(tsunami);

		// Workload A: Incremental Versions of Text (shakespeare.txt)
		foreach ((string label, int avgSize) in presets)
		{
			EvaluatePair(
				"Shakespeare (Multi-Version: 5 revisions)",
				label,
				avgSize,
				shakespeareVersions,
				results);
		}

		// Workload B: Incremental Versions of Binary (tsunami.tiff)
		foreach ((string label, int avgSize) in presets)
		{
			EvaluatePair(
				"Tsunami TIFF (Multi-Version: 5 revisions)",
				label,
				avgSize,
				tsunamiVersions,
				results);
		}

		// Workload C: Pure Identical File Deduplication (5 exact copies)
		List<byte[]> identicalCopies = [shakespeare, shakespeare, shakespeare, shakespeare, shakespeare];
		EvaluatePair(
			"Shakespeare (5 Identical Copies)",
			"16 KB",
			16384,
			identicalCopies,
			results);

		// Optional Custom Path
		if (args.Length > 0 && File.Exists(args[0]))
		{
			byte[] customFile = File.ReadAllBytes(args[0]);
			List<byte[]> customVersions = GenerateFileVersions(customFile);
			string customName = Path.GetFileName(args[0]);

			foreach ((string label, int avgSize) in presets)
			{
				EvaluatePair(
					$"{customName} (Multi-Version: 5 revisions)",
					label,
					avgSize,
					customVersions,
					results);
			}
		}

		// Write Markdown report
		string mdReportPath = Path.Combine(outputDir, "deduplication-report.md");
		WriteMarkdownReport(mdReportPath, results);

		// Write CSV report
		string csvReportPath = Path.Combine(outputDir, "deduplication-report.csv");
		WriteCsvReport(csvReportPath, results);
	}

	private static void EvaluatePair(
		string datasetName,
		string presetLabel,
		int avgSize,
		IReadOnlyList<byte[]> versions,
		List<DeduplicationResult> results)
	{
		DeduplicationResult fastCdcResult = EvaluateFastCdc(datasetName, presetLabel, avgSize, versions);
		DeduplicationResult specJumpResult = EvaluateSpecJump(datasetName, presetLabel, avgSize, versions, fastCdcResult.DeduplicationRatio);

		results.Add(fastCdcResult);
		results.Add(specJumpResult);
	}

	private static DeduplicationResult EvaluateFastCdc(
		string datasetName,
		string presetLabel,
		int avgSize,
		IReadOnlyList<byte[]> versions)
	{
		int minSize = avgSize <= 4096 ? 512 : avgSize / 2;
		int maxSize = avgSize * 2;
		FastCdcOptions options = new(minSize, avgSize, maxSize, FastCdcFormat.V2020);

		HashSet<UInt128> uniqueChunks = [];
		long totalInputBytes = 0;
		long uniqueStoredBytes = 0;
		int totalChunks = 0;

		Stopwatch sw = Stopwatch.StartNew();

		foreach (byte[] version in versions)
		{
			totalInputBytes += version.Length;

			foreach (global::FastCdc.Chunk chunk in global::FastCdc.FastCdc.Chunk(version, options))
			{
				totalChunks++;
				ReadOnlySpan<byte> chunkBytes = version.AsSpan((int)chunk.Offset, chunk.Length);

				UInt128 hash = ComputeChunkHash(chunkBytes);
				if (uniqueChunks.Add(hash))
				{
					uniqueStoredBytes += chunk.Length;
				}
			}
		}

		sw.Stop();

		return new DeduplicationResult
		{
			Dataset = datasetName,
			Preset = presetLabel,
			Algorithm = "FastCDC V2020",
			TotalInputBytes = totalInputBytes,
			TotalChunks = totalChunks,
			UniqueChunks = uniqueChunks.Count,
			UniqueStoredBytes = uniqueStoredBytes,
			ElapsedMs = sw.Elapsed.TotalMilliseconds,
			BaselineDer = null
		};
	}

	private static DeduplicationResult EvaluateSpecJump(
		string datasetName,
		string presetLabel,
		int avgSize,
		IReadOnlyList<byte[]> versions,
		double baselineDer)
	{
		SpecJumpCdcOptions options = new(avgSize);

		HashSet<UInt128> uniqueChunks = [];
		long totalInputBytes = 0;
		long uniqueStoredBytes = 0;
		int totalChunks = 0;

		Stopwatch sw = Stopwatch.StartNew();

		foreach (byte[] version in versions)
		{
			totalInputBytes += version.Length;

			foreach (global::SpecJumpCdc.Chunk chunk in global::SpecJumpCdc.SpecJumpCdc.Chunk(version, options))
			{
				totalChunks++;
				ReadOnlySpan<byte> chunkBytes = version.AsSpan((int)chunk.Offset, chunk.Length);

				UInt128 hash = ComputeChunkHash(chunkBytes);
				if (uniqueChunks.Add(hash))
				{
					uniqueStoredBytes += chunk.Length;
				}
			}
		}

		sw.Stop();

		return new DeduplicationResult
		{
			Dataset = datasetName,
			Preset = presetLabel,
			Algorithm = "SpecJumpCDC",
			TotalInputBytes = totalInputBytes,
			TotalChunks = totalChunks,
			UniqueChunks = uniqueChunks.Count,
			UniqueStoredBytes = uniqueStoredBytes,
			ElapsedMs = sw.Elapsed.TotalMilliseconds,
			BaselineDer = baselineDer
		};
	}

	private static UInt128 ComputeChunkHash(ReadOnlySpan<byte> data)
	{
		byte[] hashBytes = XxHash128.Hash(data);
		return BitConverter.ToUInt128(hashBytes, 0);
	}

	private static List<byte[]> GenerateFileVersions(byte[] original)
	{
		List<byte[]> versions = [original];

		// Revision 1: Insert 128 bytes at 15% offset (boundary shift test)
		byte[] rev1 = new byte[original.Length + 128];
		int split1 = (int)(original.Length * 0.15);
		Array.Copy(original, 0, rev1, 0, split1);
		for (int i = 0; i < 128; i++) rev1[split1 + i] = (byte)(i ^ 0xAA);
		Array.Copy(original, split1, rev1, split1 + 128, original.Length - split1);
		versions.Add(rev1);

		// Revision 2: Delete 256 bytes at 45% offset from rev1
		int split2 = (int)(rev1.Length * 0.45);
		byte[] rev2 = new byte[rev1.Length - 256];
		Array.Copy(rev1, 0, rev2, 0, split2);
		Array.Copy(rev1, split2 + 256, rev2, split2, rev1.Length - (split2 + 256));
		versions.Add(rev2);

		// Revision 3: Mutate 200 bytes in-place at 70% offset of rev2
		byte[] rev3 = (byte[])rev2.Clone();
		int split3 = (int)(rev3.Length * 0.70);
		for (int i = 0; i < 200; i++) rev3[split3 + i] ^= 0x55;
		versions.Add(rev3);

		// Revision 4: Append 512 bytes to the end of rev3
		byte[] rev4 = new byte[rev3.Length + 512];
		Array.Copy(rev3, 0, rev4, 0, rev3.Length);
		for (int i = 0; i < 512; i++) rev4[rev3.Length + i] = (byte)(i & 0xFF);
		versions.Add(rev4);

		return versions;
	}

	private static string ResolveFixturePath(string filename)
	{
		string direct = Path.Combine("fixtures", filename);
		if (File.Exists(direct)) return direct;

		string appBase = Path.Combine(AppContext.BaseDirectory, "fixtures", filename);
		if (File.Exists(appBase)) return appBase;

		string projectDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SpecJumpCdc.Benchmark", "fixtures", filename);
		if (File.Exists(projectDir)) return Path.GetFullPath(projectDir);

		return direct;
	}

	private static void WriteMarkdownReport(string filePath, List<DeduplicationResult> results)
	{
		IFormatProvider inv = CultureInfo.InvariantCulture;
		StringBuilder sb = new();
		sb.AppendLine("# Content-Defined Chunking Deduplication Benchmark Report");
		sb.AppendLine();
		sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine();
		sb.AppendLine("| Dataset | Preset | Algorithm | Total Chunks | Unique Chunks | Total Input | Unique Stored | Actual Avg Chunk | DER | Space Saving | DER Deviation | Throughput |");
		sb.AppendLine("| :--- | :---: | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

		foreach (DeduplicationResult r in results)
		{
			string deviationStr = r.BaselineDer.HasValue
				? $"{r.DerDeviationPercent.ToString("+#0.00;-#0.00;0.00", inv)}%"
				: "Baseline";

			sb.AppendLine($"| {r.Dataset} | {r.Preset} | **{r.Algorithm}** | {r.TotalChunks.ToString("N0", inv)} | {r.UniqueChunks.ToString("N0", inv)} | {FormatBytes(r.TotalInputBytes)} | {FormatBytes(r.UniqueStoredBytes)} | {FormatBytes((long)r.ActualAvgChunkSize)} | **{r.DeduplicationRatio.ToString("F2", inv)}x** | {r.SpaceSavingPercent.ToString("F2", inv)}% | {deviationStr} | {r.ThroughputMBs.ToString("N0", inv)} MB/s |");
		}

		sb.AppendLine();
		sb.AppendLine("### Validation Note");
		sb.AppendLine("- **DER (Deduplication Ratio)**: Total Input Bytes / Unique Stored Bytes. Higher is better.");
		sb.AppendLine("- **DER Deviation**: Relative deviation in deduplication ratio compared to FastCDC V2020. Research paper asserts deviation must remain < 1.00%.");

		File.WriteAllText(filePath, sb.ToString());
	}

	private static void WriteCsvReport(string filePath, List<DeduplicationResult> results)
	{
		IFormatProvider inv = CultureInfo.InvariantCulture;
		StringBuilder sb = new();
		sb.AppendLine("Dataset,Preset,Algorithm,TotalChunks,UniqueChunks,TotalInputBytes,UniqueStoredBytes,DuplicateBytes,ActualAvgChunkSize,DeduplicationRatio,SpaceSavingPercent,DerDeviationPercent,ElapsedMs,ThroughputMBs");

		foreach (DeduplicationResult r in results)
		{
			string deviationStr = r.BaselineDer.HasValue ? r.DerDeviationPercent.ToString("F4", inv) : "0.0000";
			sb.AppendLine($"\"{r.Dataset}\",\"{r.Preset}\",\"{r.Algorithm}\",{r.TotalChunks},{r.UniqueChunks},{r.TotalInputBytes},{r.UniqueStoredBytes},{r.DuplicateBytes},{r.ActualAvgChunkSize.ToString("F1", inv)},{r.DeduplicationRatio.ToString("F4", inv)},{r.SpaceSavingPercent.ToString("F4", inv)},{deviationStr},{r.ElapsedMs.ToString("F2", inv)},{r.ThroughputMBs.ToString("F2", inv)}");
		}

		File.WriteAllText(filePath, sb.ToString());
	}

	private static string FormatBytes(long bytes)
	{
		IFormatProvider inv = CultureInfo.InvariantCulture;
		return bytes switch
		{
			>= 1024 * 1024 * 1024 => $"{((double)bytes / (1024 * 1024 * 1024)).ToString("F2", inv)} GB",
			>= 1024 * 1024 => $"{((double)bytes / (1024 * 1024)).ToString("F2", inv)} MB",
			>= 1024 => $"{((double)bytes / 1024).ToString("F2", inv)} KB",
			_ => $"{bytes} B"
		};
	}
}

public sealed class DeduplicationResult
{
	public required string Dataset { get; init; }
	public required string Preset { get; init; }
	public required string Algorithm { get; init; }
	public required long TotalInputBytes { get; init; }
	public required int TotalChunks { get; init; }
	public required int UniqueChunks { get; init; }
	public required long UniqueStoredBytes { get; init; }
	public required double ElapsedMs { get; init; }
	public required double? BaselineDer { get; init; }

	public long DuplicateBytes => TotalInputBytes - UniqueStoredBytes;
	public double ActualAvgChunkSize => TotalChunks > 0 ? (double)TotalInputBytes / TotalChunks : 0;
	public double DeduplicationRatio => UniqueStoredBytes > 0 ? (double)TotalInputBytes / UniqueStoredBytes : 1.0;
	public double SpaceSavingPercent => TotalInputBytes > 0 ? (1.0 - (double)UniqueStoredBytes / TotalInputBytes) * 100.0 : 0.0;
	public double DerDeviationPercent => BaselineDer.HasValue && BaselineDer.Value > 0
		? ((DeduplicationRatio - BaselineDer.Value) / BaselineDer.Value) * 100.0
		: 0.0;
	public double ThroughputMBs => ElapsedMs > 0 ? (TotalInputBytes / (1024.0 * 1024.0)) / (ElapsedMs / 1000.0) : 0.0;
}
