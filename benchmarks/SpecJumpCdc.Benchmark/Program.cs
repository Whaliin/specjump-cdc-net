using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using SpecJumpCdc.Benchmark;

string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
string artifactsPath = Path.Combine("BenchmarkDotNet.Artifacts", timestamp);

var fastJob = Job.Default
	.WithWarmupCount(3)
	.WithIterationCount(6);

var config = DefaultConfig.Instance
	.AddJob(fastJob)
	.WithArtifactsPath(artifactsPath)
	.WithOptions(ConfigOptions.DontOverwriteResults);

BenchmarkRunner.Run<SpecJumpCdcBenchmark>(config, args);
