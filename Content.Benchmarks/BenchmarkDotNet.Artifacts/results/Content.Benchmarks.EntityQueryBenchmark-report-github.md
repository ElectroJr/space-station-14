```

BenchmarkDotNet v0.13.12, Windows 10 (10.0.19042.1081/20H2/October2020Update)
AMD Ryzen 7 3800X, 1 CPU, 16 logical and 8 physical cores
.NET SDK 8.0.203
  [Host]     : .NET 8.0.3 (8.0.324.11423), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.3 (8.0.324.11423), X64 RyuJIT AVX2


```
| Method              | Categories    | Mean         | Error     | StdDev    | Ratio | RatioSD |
|-------------------- |-------------- |-------------:|----------:|----------:|------:|--------:|
| SingleEnumerator    | Enumerator    | 234,946.3 ns | 124.02 ns | 109.94 ns |     ? |       ? |
| DoubleEnumerator    | Enumerator    |  33,727.0 ns |  50.44 ns |  47.18 ns |     ? |       ? |
|                     |               |              |           |           |       |         |
| StructEvents        | Events        | 477,189.2 ns | 636.05 ns | 594.96 ns |  1.00 |    0.00 |
|                     |               |              |           |           |       |         |
| GetSingleEnumerator | GetEnumerator |     177.7 ns |   0.56 ns |   0.52 ns |  1.00 |    0.00 |
| GetDoubleEnumerator | GetEnumerator |     188.3 ns |   0.64 ns |   0.60 ns |  1.06 |    0.00 |
|                     |               |              |           |           |       |         |
| TryComp             | TryComp       | 113,485.8 ns | 128.74 ns | 107.50 ns |  1.00 |    0.00 |
| TryCompFail         | TryComp       |  88,929.7 ns |  43.92 ns |  38.93 ns |  0.78 |    0.00 |
| TryCompSucceed      | TryComp       | 274,630.7 ns | 256.89 ns | 214.51 ns |  2.42 |    0.00 |
