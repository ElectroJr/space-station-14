#nullable enable
using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Server.Mind;
#if !DEBUG
using Robust.Shared;
#endif
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Benchmarks;

// This benchmark checks the performance of client-side movement events
// This is intended as an implicit test of the sprite & light lookup trees, which are probably the main bottleneck for
// clients, due to their recursive nature.

[Virtual]
public class MoveEventBenchmark
{

    private TestPair _pair = default!;
    private SharedTransformSystem _sys = default!;

    private Entity<TransformComponent, MetaDataComponent> _player;
    private EntityUid _grid;
    private EntityUid _map;
    private EntityCoordinates _gridCoord;
    private EntityCoordinates _gridCoord2;
    private EntityCoordinates _mapCoords;

    [GlobalSetup]
    public void Setup()
    {
#if !DEBUG
        ProgramShared.PathOffset = "../../../../";
#endif
        PoolManager.Startup();
        SetupAsync().Wait();
    }

    private async Task SetupAsync()
    {
        _pair = await PoolManager.GetServerClient(new() {Connected = true});
        _sys = _pair.Client.System<SharedTransformSystem>();
        var entMan = _pair.Client.EntMan;

        var map = await _pair.CreateTestMap();
        EntityUid ent = default;
        // Spawn a complex player entity containing several nested entities.
        await _pair.Server.WaitPost(() =>
        {
            var mind = _pair.Server.System<MindSystem>();
            ent = _pair.Server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            _pair.Server.ConsoleHost.ExecuteCommand($"setoutfit {_pair.Server.EntMan.GetNetEntity(ent)} CaptainGear");
            mind.ControlMob(_pair.Player!.UserId, ent);
        });

        await _pair.RunTicksSync(20);

        // Check that the client has been set up and is attached to the entity.
        var nuid = _pair.Server.EntMan.GetNetEntity(ent);
        var uid = entMan.GetEntity(nuid);
        _player = (uid, entMan.TransformQuery.Comp(uid), entMan.MetaQuery.Comp(uid));
        entMan.EntityExists(_player);
        var total = 0;
        CountChildren(ref total, _player);
        if (total < 50)
            throw new Exception($"Failed to equip outfit? Only {total} entities.");

        _grid = map.CGridUid;
        _map = map.CMapUid;
        _gridCoord = map.CGridCoords;
        _gridCoord2 = map.CGridCoords.Offset(new(0.2f, 0.2f));
        _mapCoords = _sys.ToCoordinates(map.MapCoords.Offset(new(2, 2)));

        // Check that we can change positions, and that they result in us being parented to the correct entity.
        _sys.SetCoordinates(_player, _gridCoord2);
        if (_player.Comp1.ParentUid != _grid || !_player.Comp1._localPosition.EqualsApprox(_gridCoord2.Position))
            throw new Exception("Failed to set position");

        _sys.SetCoordinates(_player, _mapCoords);
        if (_player.Comp1.ParentUid != _map || !_player.Comp1._localPosition.EqualsApprox(_mapCoords.Position))
            throw new Exception("Failed to set position");

        _sys.SetCoordinates(_player, _gridCoord);
        if (_player.Comp1.ParentUid != _grid || !_player.Comp1._localPosition.EqualsApprox(_gridCoord.Position))
            throw new Exception("Failed to set position");

        await _pair.RunTicksSync(20);
    }

    private void CountChildren(ref int total, EntityUid uid)
    {
        var xform = _pair.Client.EntMan.GetComponent<TransformComponent>(uid);
        total += xform.ChildCount;
        foreach (var child in xform._children)
        {
            CountChildren(ref total, child);
        }
    }

    [Benchmark(Baseline = true)]
    public void MoveSameParent()
    {
        // Not using WaitPost or whatever could theoretically break things
        // But this method really shouldn't be accessing thread local shit, so it should be fine.
        _sys.SetCoordinates(_player, _gridCoord);
        _sys.SetCoordinates(_player, _gridCoord2);
    }

    [Benchmark]
    public void MoveNewParent()
    {
        // Not using WaitPost or whatever could theoretically break things
        // But this method really shouldn't be accessing thread local shit, so it should be fine.
        _sys.SetCoordinates(_player, _gridCoord);
        _sys.SetCoordinates(_player, _mapCoords);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _pair.DisposeAsync();
        PoolManager.Shutdown();
    }
}
