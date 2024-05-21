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
public class BenchSingle
{
    public const string Map = "Maps/atlas.yml";
    private TestPair _pair = default!;
    private EntityManager _entMan = default!;
    private MapId _mapId = new(10);
    private EntityUid[] _items = default!;
    private EntityQuery<TransformComponent> _query;

    private EntityUid _ent;

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

        _ent = _items[_items.Length / 2];
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _pair.DisposeAsync();
        PoolManager.Shutdown();
    }

    [Benchmark(Baseline = true)]
    public TransformComponent? QueryTryComp()
    {
        _query.TryGetComponent(_ent, out var xform);
        return xform;
    }

    [Benchmark]
    public TransformComponent? TryComp()
    {
        _entMan.TryGetComponent(_ent, out TransformComponent? xform);
        return xform;
    }

    [Benchmark]
    public TransformComponent? TryCompGeneric()
    {
        _entMan.TryGetComponent<TransformComponent>(_ent, out var xform);
        return xform;
    }

    [Benchmark]
    public TransformComponent? TryCompGenericIfElse()
    {
        _entMan.TryGetComponentIfElse<TransformComponent>(_ent, out var xform);
        return xform;
    }

    [Benchmark]
    public ItemComponent? TryCompGenericItem()
    {
        _entMan.TryGetComponent<ItemComponent>(_ent, out var item);
        return item;
    }

    [Benchmark]
    public ItemComponent? TryCompGenericItemIfElse()
    {
        _entMan.TryGetComponentIfElse<ItemComponent>(_ent, out var item);
        return item;
    }
}
