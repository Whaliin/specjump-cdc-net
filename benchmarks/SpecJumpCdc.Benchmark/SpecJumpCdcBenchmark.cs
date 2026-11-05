using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;

namespace SpecJumpCdc.Benchmark;

[MemoryDiagnoser]
public class SpecJumpCdcBenchmark
{
	private byte[] _data = [];
	private MemoryStream _memoryStream = null!;
	private Pipe _pipeFastCdc = null!;
	private Pipe _pipeSpecJump = null!;

	private static readonly global::FastCdc.FastCdcOptions FastCdcOptions16KV2020 = new(8192, 16384, 32768, global::FastCdc.FastCdcFormat.V2020);
	private static readonly global::FastCdc.FastCdcOptions FastCdcOptions32KV2020 = global::FastCdc.FastCdcOptions.Default32KB;
	private static readonly global::FastCdc.FastCdcOptions FastCdcOptions64KV2020 = global::FastCdc.FastCdcOptions.Default64KB;
	private static readonly global::FastCdc.FastCdcOptions FastCdcOptions128KV2020 = new(65536, 131072, 262144, global::FastCdc.FastCdcFormat.V2020);
	private static readonly global::FastCdc.FastCdcOptions FastCdcOptions256KV2020 = new(131072, 262144, 524288, global::FastCdc.FastCdcFormat.V2020);

	private static readonly SpecJumpCdcOptions SpecJumpOptions16K = SpecJumpCdcOptions.Default16KB;
	private static readonly SpecJumpCdcOptions SpecJumpOptions32K = SpecJumpCdcOptions.Default32KB;
	private static readonly SpecJumpCdcOptions SpecJumpOptions64K = SpecJumpCdcOptions.Default64KB;
	private static readonly SpecJumpCdcOptions SpecJumpOptions128K = SpecJumpCdcOptions.Default128KB;
	private static readonly SpecJumpCdcOptions SpecJumpOptions256K = SpecJumpCdcOptions.Default256KB;

	[Params("tsunami.tiff", "shakespeare.txt")]
	public string FixtureName { get; set; } = "tsunami.tiff";

	[GlobalSetup]
	public void Setup()
	{
		string path = ResolveFixturePath(FixtureName);
		_data = File.ReadAllBytes(path);
		_memoryStream = new MemoryStream(_data, writable: false);
	}

	[IterationSetup(Target = nameof(Benchmark16K_FastCDC_PipeReader))]
	public void SetupPipeFastCdc()
	{
		_pipeFastCdc = new Pipe();
		_data.AsSpan().CopyTo(_pipeFastCdc.Writer.GetSpan(_data.Length));
		_pipeFastCdc.Writer.Advance(_data.Length);
		_pipeFastCdc.Writer.Complete();
	}

	[IterationSetup(Target = nameof(Benchmark16K_SpecJumpCDC_PipeReader))]
	public void SetupPipeSpecJump()
	{
		_pipeSpecJump = new Pipe();
		_data.AsSpan().CopyTo(_pipeSpecJump.Writer.GetSpan(_data.Length));
		_pipeSpecJump.Writer.Advance(_data.Length);
		_pipeSpecJump.Writer.Complete();
	}

	private static string ResolveFixturePath(string filename)
	{
		string direct = Path.Combine("fixtures", filename);
		if (File.Exists(direct)) return direct;

		string appBase = Path.Combine(AppContext.BaseDirectory, "fixtures", filename);
		if (File.Exists(appBase)) return appBase;

		string projectDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixtures", filename);
		if (File.Exists(projectDir)) return Path.GetFullPath(projectDir);

		return direct;
	}

	// ------------------------------------------------------------------------
	// 16 KB In-Memory Slicing
	// ------------------------------------------------------------------------
	[Benchmark(Baseline = true)]
	public void Benchmark16K_FastCDC_V2020()
	{
		foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.Chunk(_data, FastCdcOptions16KV2020)) { }
	}

	[Benchmark]
	public void Benchmark16K_SpecJumpCDC()
	{
		foreach (Chunk _ in SpecJumpCdc.Chunk(_data, SpecJumpOptions16K)) { }
	}

	// ------------------------------------------------------------------------
	// 32 KB In-Memory Slicing
	// ------------------------------------------------------------------------
	[Benchmark]
	public void Benchmark32K_FastCDC_V2020()
	{
		foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.Chunk(_data, FastCdcOptions32KV2020)) { }
	}

	[Benchmark]
	public void Benchmark32K_SpecJumpCDC()
	{
		foreach (Chunk _ in SpecJumpCdc.Chunk(_data, SpecJumpOptions32K)) { }
	}

	// ------------------------------------------------------------------------
	// 64 KB In-Memory Slicing
	// ------------------------------------------------------------------------
	[Benchmark]
	public void Benchmark64K_FastCDC_V2020()
	{
		foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.Chunk(_data, FastCdcOptions64KV2020)) { }
	}

	[Benchmark]
	public void Benchmark64K_SpecJumpCDC()
	{
		foreach (Chunk _ in SpecJumpCdc.Chunk(_data, SpecJumpOptions64K)) { }
	}

	// ------------------------------------------------------------------------
	// 128 KB In-Memory Slicing
	// ------------------------------------------------------------------------
	[Benchmark]
	public void Benchmark128K_FastCDC_V2020()
	{
		foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.Chunk(_data, FastCdcOptions128KV2020)) { }
	}

	[Benchmark]
	public void Benchmark128K_SpecJumpCDC()
	{
		foreach (Chunk _ in SpecJumpCdc.Chunk(_data, SpecJumpOptions128K)) { }
	}

	// ------------------------------------------------------------------------
	// 256 KB In-Memory Slicing
	// ------------------------------------------------------------------------
	[Benchmark]
	public void Benchmark256K_FastCDC_V2020()
	{
		foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.Chunk(_data, FastCdcOptions256KV2020)) { }
	}

	[Benchmark]
	public void Benchmark256K_SpecJumpCDC()
	{
		foreach (Chunk _ in SpecJumpCdc.Chunk(_data, SpecJumpOptions256K)) { }
	}

	// ------------------------------------------------------------------------
	// Materialization (ToList / ToArray)
	// ------------------------------------------------------------------------
	[Benchmark]
	public List<global::FastCdc.Chunk> Benchmark16K_FastCDC_ToList()
	{
		return global::FastCdc.FastCdc.Chunk(_data, FastCdcOptions16KV2020).ToList();
	}

	[Benchmark]
	public List<Chunk> Benchmark16K_SpecJumpCDC_ToList()
	{
		return SpecJumpCdc.Chunk(_data, SpecJumpOptions16K).ToList();
	}

	[Benchmark]
	public global::FastCdc.Chunk[] Benchmark16K_FastCDC_ToArray()
	{
		return global::FastCdc.FastCdc.Chunk(_data, FastCdcOptions16KV2020).ToArray();
	}

	[Benchmark]
	public Chunk[] Benchmark16K_SpecJumpCDC_ToArray()
	{
		return SpecJumpCdc.Chunk(_data, SpecJumpOptions16K).ToArray();
	}

	// ------------------------------------------------------------------------
	// Streams
	// ------------------------------------------------------------------------
	[Benchmark]
	public void Benchmark16K_FastCDC_SyncStream()
	{
		_memoryStream.Position = 0;
		foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.Chunk(_memoryStream, FastCdcOptions16KV2020)) { }
	}

	[Benchmark]
	public void Benchmark16K_SpecJumpCDC_SyncStream()
	{
		_memoryStream.Position = 0;
		foreach (Chunk _ in SpecJumpCdc.Chunk(_memoryStream, SpecJumpOptions16K)) { }
	}

	[Benchmark]
	public async Task Benchmark16K_FastCDC_AsyncStream()
	{
		_memoryStream.Position = 0;
		await foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.ChunkAsync(_memoryStream, FastCdcOptions16KV2020)) { }
	}

	[Benchmark]
	public async Task Benchmark16K_SpecJumpCDC_AsyncStream()
	{
		_memoryStream.Position = 0;
		await foreach (Chunk _ in SpecJumpCdc.ChunkAsync(_memoryStream, SpecJumpOptions16K)) { }
	}

	// ------------------------------------------------------------------------
	// Pipelines
	// ------------------------------------------------------------------------
	[Benchmark]
	public async Task Benchmark16K_FastCDC_PipeReader()
	{
		await foreach (global::FastCdc.Chunk _ in global::FastCdc.FastCdc.ChunkAsync(_pipeFastCdc.Reader, FastCdcOptions16KV2020)) { }
	}

	[Benchmark]
	public async Task Benchmark16K_SpecJumpCDC_PipeReader()
	{
		await foreach (Chunk _ in SpecJumpCdc.ChunkAsync(_pipeSpecJump.Reader, SpecJumpOptions16K)) { }
	}
}
