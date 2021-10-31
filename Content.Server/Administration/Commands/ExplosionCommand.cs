using Content.Server.Explosion;
using Content.Shared.Administration;
using Content.Shared.Explosion;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Fun)] // for the admin. Not so much for anyone else.
    public sealed class ExplosionCommand : IConsoleCommand
    {
        public string Command => "explosion";
        public string Description => "Train go boom";
        public string Help => "Usage: explosion <x> <y> <intensity> [mapId] [slope] [maxIntensity] [prototypeId] [angle] [spread] [distance]";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 3)
            {
                shell.WriteLine("Not enough arguments.");
                return;
            }

            // try parse required arguments
            if (!float.TryParse(args[0], out var x) ||
                !float.TryParse(args[1], out var y) ||
                !float.TryParse(args[2], out var intensity))
            {
                shell.WriteLine("Failed to parse arguments");
                return;
            }

            int id = 1;
            if (args.Length > 3 && !int.TryParse(args[3], out id))
                return;

            var mapId = new MapId(id);
            MapCoordinates coords = new((x, y), mapId);

            float slope = 1;
            if (args.Length > 4 && !float.TryParse(args[4], out slope))
                return;

            float maxIntensity = 50;
            if (args.Length > 5 && !float.TryParse(args[5], out maxIntensity))
                return;


            ExplosionPrototype? type;
            var protoMan = IoCManager.Resolve<IPrototypeManager>();
            if (args.Length > 6)
            {
                if (!protoMan.TryIndex(args[6], out type))
                    return;
            }
            else
            {
                // no prototype was specified, so lets default to whichever one was defined first
                type = protoMan.EnumeratePrototypes<ExplosionPrototype>().First();
            }

            float angle = 0;
            if (args.Length > 7 && !float.TryParse(args[7], out angle))
                return;

            float spread = 60;
            if (args.Length > 8 && !float.TryParse(args[8], out spread))
                return;

            float distance = 5;
            if (args.Length > 9 && !float.TryParse(args[9], out distance))
                return;

            var explosionSystem = EntitySystem.Get<ExplosionSystem>();

            var excluded = (args.Length > 7)
                ? explosionSystem.GetDirectionalRestriction(coords, Angle.FromDegrees(angle), spread, distance)
                : new HashSet<Vector2i>();

            explosionSystem.QueueExplosion(coords, type.ID, intensity, slope, maxIntensity, excluded);
        }
    }
}
