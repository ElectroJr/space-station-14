#nullable enable
using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Shared.Item;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Benchmarks;

/// <summary>
/// Benchmarks for comparing the speed of various component fetching/lookup related methods, including directed event
/// subscriptions
/// </summary>
[Virtual]
public class BenchMultiple
{
    public const string Map = "Maps/atlas.yml";
    private TestPair _pair = default!;
    private EntityManager _entMan = default!;
    private MapId _mapId = new(10);
    private EntityUid[] _items = default!;
    private EntityQuery<TransformComponent> _query;

    [GlobalSetup]
    public void Setup()
    {
        ProgramShared.PathOffset = "../../../../";
        PoolManager.Startup();

        _pair = PoolManager.GetServerClient().GetAwaiter().GetResult();
        _entMan = (EntityManager)_pair.Server.ResolveDependency<IEntityManager>();
        _query = _entMan.GetEntityQuery<TransformComponent>();

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

    [Benchmark(Baseline = true)]
    public int QueryTryComp()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_query.TryGetComponent(uid, out var xform))
                hashCode = HashCode.Combine(hashCode, xform.GetHashCode());
        }
        return hashCode;
    }

    [Benchmark]
    public int TryComp()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponent(uid, out TransformComponent? xform))
                hashCode = HashCode.Combine(hashCode, xform.GetHashCode());
        }
        return hashCode;
    }

    [Benchmark]
    public int TryCompGeneric()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponent<TransformComponent>(uid, out var xform))
                hashCode = HashCode.Combine(hashCode, xform.GetHashCode());
        }
        return hashCode;
    }

    [Benchmark]
    public int TryCompGenericIfElse()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponentIfElse<TransformComponent>(uid, out var xform))
                hashCode = HashCode.Combine(hashCode, xform.GetHashCode());
        }
        return hashCode;
    }

    [Benchmark]
    public int TryCompGenericItem()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponent<ItemComponent>(uid, out var item))
                hashCode = HashCode.Combine(hashCode, item.GetHashCode());
        }
        return hashCode;
    }

    [Benchmark]
    public int TryCompGenericItemIfElse()
    {
        var hashCode = 0;
        foreach (var uid in _items)
        {
            if (_entMan.TryGetComponentIfElse<ItemComponent>(uid, out var item))
                hashCode = HashCode.Combine(hashCode, item.GetHashCode());
        }
        return hashCode;
    }
}
