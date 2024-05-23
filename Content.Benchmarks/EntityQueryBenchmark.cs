#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CommunityToolkit.HighPerformance;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Shared.Clothing.Components;
using Content.Shared.Doors.Components;
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
    private EntityManager _entMan = default!;
    private MapId _mapId = new(10);
    private EntityUid[] _items = default!;
    private BenchEnt[] _itemEnts = default!;
    private World _world = default!;
    public int Version = 0;

    [GlobalSetup]
    public void Setup()
    {
        ProgramShared.PathOffset = "../../../../";
        PoolManager.Startup(typeof(QueryBenchSystem).Assembly);

        _pair = PoolManager.GetServerClient().GetAwaiter().GetResult();
        _entMan = _pair.Server.ResolveDependency<EntityManager>();

        _pair.Server.ResolveDependency<IRobustRandom>().SetSeed(42);
        _pair.Server.WaitPost(() =>
        {
            var success = _entMan.System<MapLoaderSystem>().TryLoad(_mapId, Map, out _);
            if (!success)
                throw new Exception("Map load failed");
            _pair.Server.MapMan.DoMapInitialize(_mapId);
        }).GetAwaiter().GetResult();

        _world = _entMan._world;

        _items = new EntityUid[_entMan.Count<ItemComponent>()];
        _itemEnts = new BenchEnt[_entMan.Count<ItemComponent>()];
        var i = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<ItemComponent>();
        while (enumerator.MoveNext(out var uid, out _))
        {
            _items[i] = uid;
            _itemEnts[i++] = new BenchEnt(uid, _world);
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
    /// Variant of <see cref="TryComp"/> that uses cached archetype/chunk information to get a component.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("TryComp")]
    public int TryCompCached()
    {
        var hashCode = 0;
        foreach (ref var ent in _itemEnts.AsSpan())
        {
            if (TryGet(ref ent, out ClothingComponent? clothing))
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

    [Benchmark]
    [BenchmarkCategory("Item Enumerator")]
    public int SingleItemEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<ItemComponent>();
        while (enumerator.MoveNext(out var item))
        {
            hashCode = HashCode.Combine(hashCode, item.GetHashCode());
        }

        return hashCode;
    }

    [Benchmark]
    [BenchmarkCategory("Item Enumerator")]
    public int DoubleItemEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<ClothingComponent, ItemComponent>();
        while (enumerator.MoveNext(out _, out var item))
        {
            hashCode = HashCode.Combine(hashCode, item.GetHashCode());
        }

        return hashCode;
    }

    [Benchmark]
    [BenchmarkCategory("Item Enumerator")]
    public int TripleItemEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<ClothingComponent, ItemComponent, TransformComponent>();
        while (enumerator.MoveNext(out _, out _, out var xform))
        {
            hashCode = HashCode.Combine(hashCode, xform.GetHashCode());
        }

        return hashCode;
    }

    [Benchmark]
    [BenchmarkCategory("Airlock Enumerator")]
    public int SingleAirlockEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<AirlockComponent>();
        while (enumerator.MoveNext(out var airlock))
        {
            hashCode = HashCode.Combine(hashCode, airlock.GetHashCode());
        }

        return hashCode;
    }

    [Benchmark]
    [BenchmarkCategory("Airlock Enumerator")]
    public int DoubleAirlockEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<AirlockComponent, DoorComponent>();
        while (enumerator.MoveNext(out _, out var door))
        {
            hashCode = HashCode.Combine(hashCode, door.GetHashCode());
        }

        return hashCode;
    }

    [Benchmark]
    [BenchmarkCategory("Airlock Enumerator")]
    public int TripleAirlockEnumerator()
    {
        var hashCode = 0;
        var enumerator = _entMan.AllEntityQueryEnumerator<AirlockComponent, DoorComponent, TransformComponent>();
        while (enumerator.MoveNext(out _, out _, out var xform))
        {
            hashCode = HashCode.Combine(hashCode, xform.GetHashCode());
        }

        return hashCode;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet<T>(ref BenchEnt ent, [NotNullWhen(true)] out T? comp) where T : IComponent, new()
    {
        // TODO
        // Has any entity been added to or removed from the archetype (or the specific slot?) such that the cached data
        // in the struct is invalidated?
        // if (ent.Version != ent.Archetype.Version);
        if (ent.Version != Version)
            ent = new BenchEnt(ent.Uid, _world);

        var compId = Arch.Core.Utils.Component<T>.ComponentType.Id;
        if (compId >= ent.CompIndices.Length)
        {
            comp = default;
            return false;
        }

        var compIndex = ent.CompIndices.DangerousGetReferenceAt(compId);
        if (compIndex == -1)
        {
            comp = default;
            return false;
        }

        var array = Unsafe.As<T[]>(ent.Comps.DangerousGetReferenceAt(compIndex));
        comp = array[ent.EntIndex];
        return true;
    }
}

[ByRefEvent]
public struct QueryBenchEvent
{
    public int HashCode;
}

/// <summary>
/// Entity struct that caches archetype / chunk information in order to try speed up component retrieval
/// </summary>
public readonly struct BenchEnt
{
    // If assuming an "entity" struct like this gets passed around for faster try-comps, we need to ensure that
    // ReSharper disable once UnassignedReadonlyField
    public readonly int Version;

    public readonly Array[] Comps;
    public readonly int[] CompIndices;
    public readonly int EntIndex;
    public readonly Archetype Archetype;
    public readonly EntityUid Uid;

    public BenchEnt(EntityUid uid, World world)
    {
        Uid = uid;
        var slots = world.GetSlots();
        (Archetype, (EntIndex, var chunkIndex)) = slots[uid.Id];
        var chunk = Archetype.Chunks[chunkIndex];
        Comps = chunk.Components;
        CompIndices = chunk.ComponentIdToArrayIndex;
        // Version = Archetype.Version;
    }
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
