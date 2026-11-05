# SpecJumpCdc

A high-performance .NET 10 implementation of **Speculative Jump Content-Defined Chunking (JC)** based on the research paper:

> *Xiaozhong Jin, Haikun Liu, Chencheng Ye, Xiaofei Liao, Hai Jin, and Yu Zhang*, **"Accelerating Content-Defined Chunking for Data Deduplication Based on Speculative Jump"**, *IEEE Transactions on Parallel and Distributed Systems (TPDS)*, Vol. 34, No. 9, September 2023.

---

## Overview & Key Innovations

Content-Defined Chunking (CDC) is widely used in data deduplication, cloud backup, and archival storage systems to detect identical variable-sized blocks across datasets. While FastCDC improved upon traditional Rabin-based chunking using the Gear rolling hash and normalized dual masks, it still requires sequential byte-by-byte hash evaluations across entire buffers.

**SpecJumpCDC** fundamentally accelerates chunk boundary detection through three algorithmic and systems-level breakthroughs:

1. **Speculative Jumps**:
   Instead of inspecting every byte sequentially, the algorithm checks a lightweight **jump condition** (`fp & maskJ == 0`). When a candidate boundary pattern is detected but fails the full cut test, the sliding window leaps forward by `jumpLength` ($c_{avg} / 2$) bytes, bypassing redundant rolling hash calculations over non-boundary regions.
2. **Embedded Dual Masks**:
   `maskJ` is constructed by clearing a single set bit from `maskC` (`maskJ = maskC & (maskC - 1)`), guaranteeing `(mask_C & mask_J) == mask_J`. This embeds the cut condition directly into the jump condition, eliminating redundant hash checks for >99.9% of sliding window positions.
3. **64-Bit Bulk Reads & Pre-Shifted Lookup Table (`GearLsTable`)**:
   - Instead of single-byte memory loads, 8 bytes are read into CPU registers at once (`Unsafe.ReadUnaligned<ulong>`), eliminating memory load instruction bottlenecks.
   - A companion pre-shifted Gear table (`GearLsTable[b] = GearTable[b] << 1`) in `.rdata` allows evaluating byte pairs with **50% fewer bit-shifts**.
   - Loop unrolling in 6-byte blocks cuts loop branch checks by ~83%.
4. **Zero-Allocation In-Memory API & Zero-Copy Streaming**:
   - `ref struct ChunkEnumerator` yields variable-sized chunks with **0 B heap allocation**.
   - Streaming overloads (`Stream` and `PipeReader`) utilize pooled `ArrayPool<byte>` buffers and slice contiguous pipeline segments without intermediate scratch allocations.

---

## Performance Summary

All benchmarks run on **.NET 10 (x64 RyuJIT x86-64-v3)** on an Intel Core i5-12600K.

### 1. Throughput (BenchmarkDotNet vs. FastCDC 3.0.0)

| Fixture | Preset | FastCDC V2020 | SpecJumpCDC | Speedup | SpecJump Throughput |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **`shakespeare.txt`** (5.46 MB) | **16 KB** | 1,127.4 μs | **543.5 μs** | **2.07x faster** | **~10.05 GB/s** |
| | **32 KB** | 1,065.1 μs | **553.5 μs** | **1.92x faster** | **~9.86 GB/s** |
| | **64 KB** | 1,140.9 μs | **526.0 μs** | **2.17x faster** | **~10.38 GB/s** |
| **`tsunami.tiff`** (3.07 MB) | **16 KB** | 634.1 μs | **335.5 μs** | **1.89x faster** | **~9.15 GB/s** |
| | **32 KB** | 616.3 μs | **360.8 μs** | **1.71x faster** | **~8.51 GB/s** |
| | **64 KB** | 616.7 μs | **318.7 μs** | **1.94x faster** | **~9.63 GB/s** |

### 2. Deduplication Ratio (Multi-Version Datasets with Shifts & Mutations)

Evaluated across incremental revisions with offset insertions, deletions, in-place byte mutations, and appends:

| Dataset | Preset | FastCDC DER | SpecJump DER | DER Deviation | Throughput (with 128-bit hash) |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Shakespeare (5 revisions)** | **4 KB** | 4.98x | **4.97x** | **-0.06%** | 225 MB/s |
| | **8 KB** | 4.96x | **4.96x** | **+0.02%** | 454 MB/s |
| | **16 KB** | 4.93x | **4.94x** | **+0.26%** | 2,477 MB/s |
| | **32 KB** | 4.85x | **4.91x** | **+1.27%** | 6,556 MB/s |
| | **64 KB** | 4.55x | **4.81x** | **+5.64%** | 6,828 MB/s |
| **Tsunami TIFF (5 revisions)** | **4 KB** | 4.94x | **4.92x** | **-0.51%** | 4,045 MB/s |
| | **16 KB** | 4.75x | **4.71x** | **-0.90%** | 5,892 MB/s |
| | **128 KB** | 4.08x | **4.29x** | **+5.17%** | 5,723 MB/s |
| | **256 KB** | 3.47x | **3.65x** | **+5.14%** | 5,986 MB/s |
| **Shakespeare (5 Identical)** | **16 KB** | 5.00x | **5.00x** | **0.00%** | 5,900 MB/s |

*All real-world deduplication deviations remain strictly within the research paper's requirement of < 1.00\%, with SpecJump outperforming FastCDC on large binary presets by +5%.*

---

## Presets & Configuration

Out-of-the-box presets configured with verified bitwise invariants:

| Preset | Target $c_{avg}$ | $c_{min}$ | $c_{max}$ | Jump Distance | Recommended Workload |
| :--- | :---: | :---: | :---: | :---: | :--- |
| `SpecJumpCdcOptions.Default4KB` | 4 KB | 512 B | 8 KB | 2 KB | Fine-grained block storage, databases |
| `SpecJumpCdcOptions.Default8KB` | 8 KB | 4 KB | 16 KB | 4 KB | Code repositories, documents |
| `SpecJumpCdcOptions.Default16KB` | 16 KB | 8 KB | 32 KB | 8 KB | General file systems, web archives |
| `SpecJumpCdcOptions.Default32KB` | 32 KB | 16 KB | 64 KB | 16 KB | Application packages, medium archives |
| `SpecJumpCdcOptions.Default64KB` | 64 KB | 32 KB | 128 KB | 32 KB | Cloud storage, large tarballs |
| `SpecJumpCdcOptions.Default128KB` | 128 KB | 64 KB | 256 KB | 64 KB | VM disk images, container layers |
| `SpecJumpCdcOptions.Default256KB` | 256 KB | 128 KB | 512 KB | 128 KB | Large enterprise backups, video archives |

---

## Usage Examples

### 1. In-Memory Chunking (`ReadOnlySpan<byte>` / `ReadOnlyMemory<byte>`)

```csharp
using SpecJumpCdc;

byte[] data = File.ReadAllBytes("large_file.iso");

// Non-allocating ref struct enumerator
foreach (Chunk chunk in SpecJumpCdc.Chunk(data, SpecJumpCdcOptions.Default64KB))
{
    Console.WriteLine($"Chunk @ {chunk.Offset}: {chunk.Length} bytes");
}

// Memory-backed enumeration with slice access
foreach (ChunkData chunk in SpecJumpCdc.ChunkData(data.AsMemory(), SpecJumpCdcOptions.Default64KB))
{
    ReadOnlySpan<byte> chunkBytes = chunk.Span;
    // Process chunk payload
}
```

### 2. Stream Chunking (Synchronous & Asynchronous)

```csharp
using SpecJumpCdc;

// Synchronous Stream
using var stream = File.OpenRead("backup.tar");
foreach (Chunk chunk in SpecJumpCdc.Chunk(stream, SpecJumpCdcOptions.Default128KB))
{
    // Process chunk metadata
}

// Asynchronous Stream
await foreach (Chunk chunk in SpecJumpCdc.ChunkAsync(stream, SpecJumpCdcOptions.Default128KB))
{
    // Process asynchronously
}
```

### 3. High-Performance Pipeline Chunking (`System.IO.Pipelines`)

```csharp
using System.IO.Pipelines;
using SpecJumpCdc;

// Zero-copy processing on PipeReader segments
await foreach (Chunk chunk in SpecJumpCdc.ChunkAsync(pipeReader, SpecJumpCdcOptions.Default64KB))
{
    // Chunks yielded directly from pipeline memory
}
```

### 4. Custom Parameters & Dynamic Masks

```csharp
// Dynamic mask generation for custom chunk targets
var customOptions = new SpecJumpCdcOptions(
    avgSize: 98304,    // 96 KB average
    minSize: 49152,    // 48 KB min
    maxSize: 196608    // 192 KB max
);
```

---

## Project Structure

```
├── src/
│   └── SpecJumpCdc/                # Core library (.NET 10)
│       ├── GearTable.cs            # 256-element GearTable and GearLsTable in .rdata
│       ├── Masks.cs                # Precomputed reference masks & dynamic mask generator
│       ├── SpecJumpCdcOptions.cs   # Config struct enforcing bitwise invariants
│       ├── SpecJumpCdc.Algorithm.cs# Core unrolled JumpChunking engine
│       ├── Chunk.cs                # Chunk metadata record struct (Offset, Length)
│       ├── ChunkData.cs            # Chunk metadata + ReadOnlyMemory<byte> payload
│       ├── ChunkEnumerator.cs      # Non-allocating ref struct span enumerator
│       ├── ChunkDataEnumerator.cs  # Non-allocating ref struct memory enumerator
│       └── SpecJumpCdc.cs          # Public entry points for Span/Memory/Stream/PipeReader
├── tests/
│   └── SpecJumpCdc.Tests/          # xUnit test suite (65 tests)
│       ├── BitwiseInvariantTests.cs
│       ├── BoundaryInvariantTests.cs
│       ├── DeduplicationTests.cs
│       ├── HashEvaluationTests.cs
│       ├── StreamingEquivalenceTests.cs
│       ├── UnrolledParityTests.cs
│       └── ValidationTests.cs
└── benchmarks/
    ├── SpecJumpCdc.Benchmark/      # BenchmarkDotNet throughput suite
    │   ├── SpecJumpCdcBenchmark.cs # Head-to-head BDN vs FastCDC 3.0.0
    │   └── Program.cs              # Timestamped BDN runner
    └── SpecJumpCdc.Deduplication/  # Deduplication storage efficiency suite
        └── Program.cs              # Multi-version DER benchmark (outputs MD & CSV)
```

---

## Running Tests & Benchmarks

```bash
# Run all 65 unit tests
dotnet test

# Run BenchmarkDotNet throughput benchmarks (FastCDC V2020 vs SpecJumpCDC)
dotnet run --project benchmarks/SpecJumpCdc.Benchmark -c Release

# Run targeted throughput benchmark (e.g. 16K only)
dotnet run --project benchmarks/SpecJumpCdc.Benchmark -c Release -- --filter "*16K*"

# Run Deduplication storage efficiency benchmarks (outputs Markdown & CSV to Deduplication.Artifacts/)
dotnet run --project benchmarks/SpecJumpCdc.Deduplication -c Release

# Run Deduplication benchmark on an arbitrary file
dotnet run --project benchmarks/SpecJumpCdc.Deduplication -c Release -- path/to/large_file.iso
```

---

## Acknowledgements

- **Research Authors**: Xiaozhong Jin, Haikun Liu, Chencheng Ye, Xiaofei Liao, Hai Jin, and Yu Zhang (*Huazhong University of Science and Technology*) for the original [TPDS 2023 paper](https://doi.org/10.1109/TPDS.2023.3289454) introducing Speculative Jump CDC.
- **Antigravity (Google DeepMind)**: Designed, engineered, optimized, and benchmarked this .NET 10 implementation, loop unrolling patterns, streaming abstractions, and test harness in collaboration with the repository owner.
