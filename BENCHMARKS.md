# Benchmark results

Current measurements are listed first. Historical measurements and the generator buffering experiment are retained below for context. Results describe the recorded versions, hardware, and inputs.

## Current implementation (2026-09-30)

These runs use case-sensitive invariant-culture comparison, positive integers, worker-side parsing, and portions limited to 100,000 lines.

### Settings and method

| Setting | Value |
| --- | --- |
| CPU | Intel Core i7-12700H, 20 logical processors |
| Memory | Approximately 31.7 GiB available to the runtime |
| OS / .NET | Windows x64 10.0.26200; SDK 10.0.401, runtime 10.0.12; Release |
| Storage | Input, output, and temporary files on C:, under the repository's `artifacts` directory |
| Generator | Seed 42; 20% minimum repeated share; 2 copies per group; ASCII texts 16–128 characters; shuffle buffer 8 MB |
| Sorter | `--chunk-lines 100000 --max-merge-files 32 --max-line-size 1`; 1, 2, or 8 workers |

- Each application starts in a fresh process. Wall time includes process startup and exit, ordinary file writes, and final buffered close. No explicit durable flush, cache reset, GC tuning, or warm-up was added.
- The initial 1 GiB batch ran in worker order **1, 2, 2, 1, 1, 2**. A later batch added three runs with 8 workers. Time and throughput in the summary use the median of three runs per worker count; memory is the highest observed peak across those runs.
- The 10 GiB configurations use one run each: first 2 workers, then 1, followed by 8 in the later batch. These single runs are a size check, not a repeated statistical comparison.
- The 8-worker batch uses the same application sources and binaries, checked against SHA-256 snapshots. Inputs were regenerated with the same seed and settings and checked against the original file hashes. It was a separate batch, not interleaved with the earlier worker configurations.
- Verification and full-file SHA-256 hashing happen outside the timed sort. Each result is verified before deletion; a source is retained across repeated sorts within each batch.
- Peak working set is the process's OS-reported peak, sampled every 200 ms while it runs. It includes runtime and GC memory. The final fraction of a second may fall between samples.
- OS caching stays enabled. Sources are hashed before sorting, and verification reads source and output after each run. Results therefore include warm-cache effects; background activity is not isolated. The repository resides under OneDrive. No storage-device model was obtained.
- Temporary disk usage comes from the application's count of completed chunks and the staged result; it excludes the retained source file. Large generated files are removed after successful verification; logs and measurement metadata are retained locally.

### Generation

Initial generation timings, with identical options apart from the target size. The later 8-worker batch regenerated the same inputs for its sort measurements.

| Target | Actual bytes | Records | Wall time | MiB/s | Peak working set |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 GiB | 1,073,741,909 | 12,800,764 | 10.457 s | 97.9 | 62.9 MiB |
| 10 GiB | 10,737,418,303 | 128,000,669 | 103.053 s | 99.4 | 62.7 MiB |

### Sorting

Throughput is actual input bytes divided by wall time, expressed in MiB/s.

| Input | Workers | Wall time | MiB/s | Peak working set | Initial chunks | Merge passes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 GiB | 1 | 30.067 s | 34.1 | 178.7 MiB | 129 | 2 |
| 1 GiB | 2 | 24.794 s | 41.3 | 240.8 MiB | 129 | 2 |
| 1 GiB | 8 | 20.718 s | 49.4 | 623.0 MiB | 129 | 2 |
| 10 GiB | 1 | 377.747 s | 27.1 | 181.2 MiB | 1,281 | 3 |
| 10 GiB | 2 | 316.112 s | 32.4 | 244.4 MiB | 1,281 | 3 |
| 10 GiB | 8 | 278.301 s | 36.8 | 648.0 MiB | 1,281 | 3 |

For 1 GiB, two workers reduced median elapsed time by **17.5%**, with a higher observed memory peak. The line-count limit produced the same number of chunks for both configurations. Peak temporary plus staging storage was **2,147,483,818 bytes** for every 1 GiB run, approximately twice the source size.

Eight workers reduced the 1 GiB median by another **16.4%** relative to two workers, while the highest observed memory peak increased from 240.8 to **623.0 MiB** (about **2.6 times**). Chunk and merge-pass counts stayed the same.

All three 10 GiB runs used an extra merge pass and **21,474,836,606 bytes** of peak temporary plus staging storage. Two workers took **16.3% less time** than one; eight workers took another **12.0% less time** than two in these single runs. The observed memory peaks were 181.2, 244.4, and 648.0 MiB for 1, 2, and 8 workers respectively, close to their 1 GiB peaks.

Increasing the worker count affects chunk creation; merge groups remain sequential. Eight workers allow up to ten portions in flight, compared with four for two workers, which increases retained working data.

<details>
<summary>Individual 1 GiB sort runs, in execution order</summary>

| Order | Workers | Wall time | Peak working set |
| --- | ---: | ---: | ---: |
| 1 | 1 | 29.956 s | 174.7 MiB |
| 2 | 2 | 24.481 s | 240.8 MiB |
| 3 | 2 | 24.794 s | 234.1 MiB |
| 4 | 1 | 30.759 s | 178.7 MiB |
| 5 | 1 | 30.067 s | 175.0 MiB |
| 6 | 2 | 24.860 s | 236.9 MiB |
| 7 | 8 | 20.348 s | 623.0 MiB |
| 8 | 8 | 21.108 s | 612.3 MiB |
| 9 | 8 | 20.718 s | 608.3 MiB |

</details>

### Correctness

All nine 1 GiB sorts skipped zero records and passed the independent verifier: invariant-culture/numeric ordering, equal record counts, and matching SHA-256 multiset fingerprints. Verification took 5.606–5.775 seconds per run. All nine sorted files also had the same full-file SHA-256:

```text
E89C15721C11325B73E474588B19F7D21DDB5E0C41C0532DA662AF160FCF0F90
```

The multiset fingerprint is probabilistic evidence of preservation; small integration tests compare complete record sets directly.

All three 10 GiB results passed verification of all **128,000,669 records**, with zero skipped records. Verification took **55.097–56.488 seconds**, outside sorting time (55.414 seconds after the 8-worker run). All three sorted files had the same full-file SHA-256:

```text
C00597AB365E1E5FFC27555A89CED8BFAA181B46A10E462B382CAF7C5BF8082A
```

### Commands

After a Release build, create an empty `data` directory and run:

```text
dotnet 3deyeLargeFile.Generator/bin/Release/net10.0/3deyeLargeFile.Generator.dll --output data/input.txt --size 1024 --repeated-percent 20 --copies-per-text 2 --shuffle-buffer-size 8 --min-text-length 16 --max-text-length 128 --seed 42
dotnet 3deyeLargeFile.Sorter/bin/Release/net10.0/3deyeLargeFile.Sorter.dll --input data/input.txt --output data/sorted.txt --chunk-lines 100000 --workers 2 --max-merge-files 32 --max-line-size 1 --temp-dir data
dotnet tools/3deyeLargeFile.Verifier/bin/Release/net10.0/3deyeLargeFile.Verifier.dll data/input.txt data/sorted.txt
```

Use `--size 10240` for 10 GiB and change `--workers` to `1` or `8` for the other configurations. Each output path must be unused. Verification is separate from timing; measure process wall time and peak working set externally.

Approximately 100 GB has not been run: C: had about 81 GiB free before the measurements, insufficient for that source plus temporary/output files. Linux and other input distributions have not been measured.

## Historical end-to-end measurements (2026-09-24)

These historical runs used ordinal text ordering and allowed zero-valued numbers. The current version uses case-sensitive invariant culture, positive integers, and skips invalid source records with diagnostics. Use the current measurements above for the present implementation; timings from these versions are not directly comparable.

The current generator creates random texts on demand with a configurable minimum repeated share and intended copy count, accepts random text collisions, and shuffles records within a bounded buffer. These measurements used the earlier fixed vocabulary; their data distribution and generation timings do not describe the new generator.

The current sorter limits portions by line count and decodes and parses them in its workers. These measurements used producer-side parsing and an automatic memory plan. They do not establish the performance of the current pipeline.

### Environment and method

- Windows x64, OS build 10.0.26200.
- Intel Core i7-12700H, 20 logical processors; approximately 31.7 GiB available to the runtime.
- .NET SDK 10.0.401, runtime 10.0.12; Release builds.
- Input, temporary files, and output used the same C: filesystem. The physical storage model was not recorded.
- Fresh application process for each measurement. Wall time includes process startup and shutdown.
- Peak working set was read from the operating system's process peak counter at 200 ms intervals.
- Files were generated immediately before sorting. OS caches were not flushed; the machine was not isolated from background work. The second 1 GiB sort may benefit from a warmer cache.
- Generator: seed 42, 10,000 distinct vocabulary entries, ASCII texts 16-128 characters, numbers 0-999999999.
- Measured sorter settings: 64 MiB working memory, 1 KiB maximum record size, at most 32 files merged at once. The reduced line limit fits every generated record and permits 32 merge readers within this working-data budget.

### Observations

| Requested size | Actual bytes | Records |
| --- | ---: | ---: |
| 1 GiB | 1,073,741,906 | 12,841,136 |
| 10 GiB | 10,737,418,247 | 128,412,782 |

| Operation | Size | Workers | Wall time | Peak working set | Initial chunks | Merge passes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Generate | 1 GiB | 1 | 1.94 s | 42.9 MiB | - | - |
| Generate | 10 GiB | 1 | 17.44 s | 44.3 MiB | - | - |
| Sort | 1 GiB | 1 | 18.14 s | 156.6 MiB | 203 | 2 |
| Sort | 1 GiB | 2 | 15.41 s | 156.5 MiB | 271 | 2 |
| Sort | 10 GiB | 2 | 200.52 s | 161.2 MiB | 2705 | 3 |

Peak temporary plus staging-file occupancy was 2,147,483,812 bytes for the 1 GiB runs and 21,474,836,494 bytes for the 10 GiB run. This excludes the source file.

The 64 MiB option budgets retained algorithm data. Runtime memory, allocation/GC behavior, stacks, and other process overhead explain the larger measured working set. Increasing input size tenfold did not increase observed peak working set proportionally.

More workers divide the shared budget into smaller portions, producing more initial chunks. The 10 GiB measurement also needed an additional merge pass. Worker count and memory budget should therefore be tuned together using the target machine and data distribution.

### Correctness checks

The optional `tools/3deyeLargeFile.Verifier` utility independently reads the source and result using `StreamReader` and its own parser. For these historical runs, it checked:

1. Every adjacent pair of output records follows ordinal-text/numeric ordering.
2. Source and output record counts agree.
3. Four modular 64-bit sums of each logical record's SHA-256 digest agree, preserving sensitivity to duplicate multiplicity.

The fingerprint is probabilistic evidence of multiset preservation, not a mathematical proof. Small integration tests compare full results against an independent in-memory reference and explicitly test spelling and duplicate preservation.

Both large-file verifications passed:

| Size | Verified records | Verification time |
| --- | ---: | ---: |
| 1 GiB | 12,841,136 | 5.30 s |
| 10 GiB | 128,412,782 | 53.23 s |

The two 1 GiB sorted files also had identical full-file SHA-256 hashes:

```text
0F5E4DA4FCF51FA45187447C88D81F1433685F695F759CC33A214D750C69D5A1
```

Verification runs were separate from the timed sorts. The verifier is a benchmark helper, not a replacement for the sorter's strict format validation or configurable resource limits.

### Reproduction notes

The historical sorter used a byte-budget option and a 1 KiB record limit that are no longer exposed by the CLI. Its generator also used a fixed vocabulary. The commands in the current measurements section target today's implementation and cannot reproduce the historical timings or output hashes.

### Limits of this evidence

- Approximately 100 GB has not been run; the local disk did not have enough free space for that input plus temporary/output data.
- Linux has not been exercised in this environment.
- Only one generated data distribution and one measured repetition per configuration are represented. No claim is made about cold-cache performance, other hardware, or a universal optimal worker count.
- Generated benchmark files are disposable; the source code and these measurements are retained.

## Generator buffering experiment (2026-09-30)

The current generator was compared with a temporary prototype that fills a second buffer while the first is shuffled and written. Both variants performed identical generation, batch partitioning, shuffling, UTF-8 encoding, and real file writes. The sequential implementation was retained: the experiment did not establish a reliable general speedup from overlapping these operations.

### Method

- Intel Core i7-12700H, 20 logical processors; Windows 10.0.26200; .NET 10.0.12, SDK 10.0.401; Release build and default runtime GC configuration.
- Each file targeted 1,024 MiB (1 GiB), with seed 42 and ASCII text lengths 16-128. Two profiles used `--repeated-percent 20 --copies-per-text 2` and `--repeated-percent 80 --copies-per-text 20`.
- Each buffer had an 8 MiB estimated string-storage budget. Sequential execution reused one buffer; overlapping execution reused exactly two buffers, transferred through bounded queues to one dedicated writer thread. The latter allowed approximately 16 MiB of retained strings, plus list storage and runtime overhead.
- Both experimental variants shared their buffer-fill and shuffle/write methods. The existing `RecordSource` and `ShuffledWriter` were also measured as a reference, with the same counters and progress timer checks as `FileGenerator`.
- Five runs per variant and profile, thirty measured runs in total. Variant order rotated each round. Each sample used a fresh process and a new file on C:.
- Each process warmed its selected variant by writing 32 MiB to `Stream.Null`, then performed a full GC outside the timed region. The measured run wrote to a real file using `TextFile.CreateWriter`, its 16,384-character StreamWriter buffer, strict UTF-8, and LF endings.
- Elapsed time includes file creation, generation, final StreamWriter flush, and close. It excludes process startup, warm-up, hash verification, cleanup, and staging-file publication.
- A final `FileStream.Flush(true)` requested durable flushing before each file was verified and removed. Its duration was measured separately and is excluded from the main table, matching the application's usual completion behavior.

### Results

Values are medians of five runs. Lower elapsed time is better.

| Repeated percent / copies | Sequential prototype | Two-buffer prototype | Change in elapsed time | Existing ShuffledWriter |
| --- | ---: | ---: | ---: | ---: |
| 20 / 2 | 11.526 s | 11.069 s | -3.96% | 11.397 s |
| 80 / 20 | 5.353 s | 6.122 s | +14.37% | 5.284 s |

The sequential prototype was within 1.4% of the existing writer's median. Against the existing writer, the two-buffer prototype took 2.9% less time for 20/2 and 15.9% more time for 80/20.

The small 20/2 gain was variable: sequential runs ranged from 11.388 to 11.640 s, while overlapping runs ranged from 10.820 to 12.090 s. The 80/20 slowdown was consistent: sequential runs ranged from 5.228 to 5.440 s, while overlapping runs ranged from 5.919 to 6.366 s.

Including the final durable flush, sequential versus overlapping medians were 11.764 versus 11.304 s for 20/2 and 5.674 versus 6.439 s for 80/20. The direction of the results was unchanged.

| Profile | Sequential peak working set | Two-buffer peak working set | Sequential Gen2 collections | Two-buffer Gen2 collections |
| --- | ---: | ---: | ---: | ---: |
| 20 / 2 | 62.99 MiB | 71.29 MiB | 115 | 150 |
| 80 / 20 | 65.60 MiB | 73.96 MiB | 7 | 138 |

These are medians of process peaks and collection counts. The additional live buffer increased memory use and was accompanied by more GC work. Collection counts alone do not establish the exact cause of the slowdown.

The sequential text-writing phase took a median 1.775 s for 20/2 and 1.690 s for 80/20. These timings include encoding, filesystem calls, scheduling delays, and GC pauses; they do not isolate physical disk wait. Generation pauses during this phase, but overlapping it also changes object lifetimes and runtime costs.

### Verification and limits

All variants and repetitions for each profile produced the same output size and SHA-256 hash. Six preliminary 64 MiB runs also passed this comparison. Hash verification was outside the timed region.

| Profile | Bytes | Records | SHA-256 |
| --- | ---: | ---: | --- |
| 20 / 2 | 1,073,741,909 | 12,800,764 | `1FBF8538B8C948197DDDA8C5C08798326B0BCF9B15FCAE8B491FFF17A54C80FF` |
| 80 / 20 | 1,073,741,852 | 12,798,412 | `88BB7B56383E37617EE4A0AFF0981EF06C6E835ACE67A6C0B27D6C3E91CACF73` |

OS caching remained enabled, and the workstation was not isolated from background work. The final durable flush did not make this an uncached device-throughput benchmark. No CPU affinity or GC tuning was applied. These observations cover this prototype and 1 GiB files on this machine; they do not establish behavior for 100 GiB files or other storage.

The temporary benchmark project, scripts, raw logs, and generated files were removed after recording these results. The application keeps its existing sequential `ShuffledWriter` with the default 8 MiB buffer.
