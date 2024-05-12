#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Shared.Clothing.Components;
using Content.Shared.Item;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Benchmarks;

/// <summary>
/// Benchmarks for comparing the speed of various component fetching/lookup related methods, including directed event
/// subscriptions
/// </summary>
[Virtual]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EntityQueryBenchmark
{
    public const string Map = "Maps/atlas.yml";

    private TestPair _pair = default!;
    private IEntityManager _entMan = default!;
    private MapId _mapId = new(10);
    private EntityUid[] _items = default!;

    [GlobalSetup]
    public void Setup()
    {
        ProgramShared.PathOffset = "../../../../";
        PoolManager.Startup(typeof(QueryBenchSystem).Assembly);

        _pair = PoolManager.GetServerClient().GetAwaiter().GetResult();
        _entMan = _pair.Server.ResolveDependency<IEntityManager>();

        _pair.Server.ResolveDependency<IRobustRandom>().SetSeed(42);
        _pair.Server.WaitPost(() =>
        {
            var success = _entMan.System<MapLoaderSystem>().TryLoad(_mapId, Map, out _);
            if (!success)
                throw new Exception("Map load failed");
            _pair.Server.MapMan.DoMapInitialize(_mapId);
        }).GetAwaiter().GetResult();

        _items = new EntityUid[_entMan.Count<ItemComponent>()];
        var i = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<ItemComponent>();
        while (enumerator.MoveNext(out var uid, out _))
        {
            _items[i++] = uid;
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _pair.DisposeAsync();
        PoolManager.Shutdown();
    }

    #region TryComp

    /// <summary>
    /// Baseline TryComp benchmark. When the benchmark was created, around 40% of the items were clothing.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("TryComp")]
    public int TryComp()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponent(uid, out ClothingComponent? clothing))
                hashCode = HashCode.Combine(hashCode, clothing.GetHashCode());
        }
        return hashCode;
    }

    /// <summary>
    /// Variant of <see cref="TryComp"/> that is meant to always fail to get a component.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("TryComp")]
    public int TryCompFail()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponent(uid, out MapGridComponent? map))
                hashCode = HashCode.Combine(hashCode, map.GetHashCode());
        }
        return hashCode;
    }

    /// <summary>
    /// Variant of <see cref="TryComp"/> that is meant to always succeed getting a component.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("TryComp")]
    public int TryCompSucceed()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponent(uid, out ItemComponent? item))
                hashCode = HashCode.Combine(hashCode, item.GetHashCode());
        }
        return hashCode;
    }

    #endregion

    #region Enumeration

    /// <summary>
    /// Enumerate all entities with an item component.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Enumerator")]
    public int SingleEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<ItemComponent>();
        while (enumerator.MoveNext(out var item))
        {
            hashCode = HashCode.Combine(hashCode, item.GetHashCode());
        }

        return hashCode;
    }

    /// <summary>
    /// Enumerate all entities with both an item and clothing component.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Enumerator")]
    public int DoubleEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<ClothingComponent, ItemComponent>();
        while (enumerator.MoveNext(out _, out var item))
        {
            hashCode = HashCode.Combine(hashCode, item.GetHashCode());
        }

        return hashCode;
    }

    /// <summary>
    /// How long it takes to get/construct a single component enumerator.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GetEnumerator")]
    public AllEntityQueryEnumerator<ItemComponent> GetSingleEnumerator()
    {
        return _entMan.AllEntityQueryEnumerator<ItemComponent>();
    }

    /// <summary>
    /// How long it takes to get/construct a double component enumerator.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("GetEnumerator")]
    public AllEntityQueryEnumerator<ClothingComponent, ItemComponent> GetDoubleEnumerator()
    {
        return _entMan.AllEntityQueryEnumerator<ClothingComponent, ItemComponent>();
    }

    #endregion

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Events")]
    public int StructEvents()
    {
        var ev = new QueryBenchEvent();
        foreach (var uid in _items)
        {
            _entMan.EventBus.RaiseLocalEvent(uid, ref ev);
        }

        return ev.HashCode;
    }
}

[ByRefEvent]
public struct QueryBenchEvent
{
    public int HashCode;
}

public sealed class QueryBenchSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingComponent, QueryBenchEvent>(OnEvent);
    }

    private void OnEvent(EntityUid uid, ClothingComponent component, ref QueryBenchEvent args)
    {
        args.HashCode = HashCode.Combine(args.HashCode, component.GetHashCode());
    }
}
