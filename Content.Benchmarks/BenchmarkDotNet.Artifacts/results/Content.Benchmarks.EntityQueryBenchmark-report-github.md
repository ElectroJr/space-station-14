```

BenchmarkDotNet v0.13.12, Windows 10 (10.0.19042.1081/20H2/October2020Update)
AMD Ryzen 7 3800X, 1 CPU, 16 logical and 8 physical cores
.NET SDK 8.0.203
  [Host]     : .NET 8.0.3 (8.0.324.11423), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.3 (8.0.324.11423), X64 RyuJIT AVX2

Categories=TryComp  

```
| Method  | Mean     | Error    | StdDev   | Ratio |
|-------- |---------:|---------:|---------:|------:|
| TryComp | 33.42 μs | 0.110 μs | 0.097 μs |  1.00 |
