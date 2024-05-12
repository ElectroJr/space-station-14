```

BenchmarkDotNet v0.13.12, Windows 10 (10.0.19042.1081/20H2/October2020Update)
AMD Ryzen 7 3800X, 1 CPU, 16 logical and 8 physical cores
.NET SDK 8.0.203
  [Host]     : .NET 8.0.3 (8.0.324.11423), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.3 (8.0.324.11423), X64 RyuJIT AVX2


```
| Method                  | Categories         | Mean       | Error     | StdDev    | Ratio | RatioSD |
|------------------------ |------------------- |-----------:|----------:|----------:|------:|--------:|
| SingleAirlockEnumerator | Airlock Enumerator |   6.116 μs | 0.0087 μs | 0.0081 μs |     ? |       ? |
| DoubleAirlockEnumerator | Airlock Enumerator |   7.020 μs | 0.0038 μs | 0.0034 μs |     ? |       ? |
| TripleAirlockEnumerator | Airlock Enumerator |   8.487 μs | 0.0086 μs | 0.0077 μs |     ? |       ? |
|                         |                    |            |           |           |       |         |
| StructEvents            | Events             | 476.205 μs | 0.6870 μs | 0.6426 μs |  1.00 |    0.00 |
|                         |                    |            |           |           |       |         |
| SingleItemEnumerator    | Item Enumerator    | 234.402 μs | 0.0744 μs | 0.0622 μs |     ? |       ? |
| DoubleItemEnumerator    | Item Enumerator    |  33.445 μs | 0.0940 μs | 0.0879 μs |     ? |       ? |
| TripleItemEnumerator    | Item Enumerator    |  42.084 μs | 0.0828 μs | 0.0775 μs |     ? |       ? |
|                         |                    |            |           |           |       |         |
| TryComp                 | TryComp            | 116.498 μs | 0.1344 μs | 0.1257 μs |  1.00 |    0.00 |
| TryCompCached           | TryComp            |   8.823 μs | 0.0222 μs | 0.0197 μs |  0.08 |    0.00 |
| TryCompFail             | TryComp            |  89.373 μs | 0.0379 μs | 0.0316 μs |  0.77 |    0.00 |
| TryCompSucceed          | TryComp            | 272.554 μs | 0.1699 μs | 0.1589 μs |  2.34 |    0.00 |
