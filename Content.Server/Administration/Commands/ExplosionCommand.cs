using Content.Server.Explosion;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System.Collections.Generic;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Fun)]
    public sealed class ExplosionCommand : IConsoleCommand
    {
        public string Command => "explosion";
        public string Description => "Train go boom";
        public string Help => "Usage: explosion (spawn|preview|clear) <x> <y> <gridId> <intensity> [slope] [maxIntensity] [angle] [spread] [distance]\n" +
                              "If the first argument is 'Spawn', this will create an explosion. Prev will generate a preview overlay and clear will remove the overlay.\n" +
                              "The last three arguments are only required for directional explosions.";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var player = shell.Player as IPlayerSession;

            if (args.Length >= 1 && args[0] == "clear")
            {
                if (player?.AttachedEntity == null)
                {
                    shell.WriteLine("You must have an attached entity.");
                    return;
                }
                EntitySystem.Get<ExplosionDebugOverlaySystem>().ClearPreview(player);
                return;
            }

            if (args[0] != "spawn" && args[0] != "preview")
            {
                shell.WriteLine("Invalid argument.");
                return;
            }

            if (args.Length < 5)
                return;

            // try parse required arguments
            if (!int.TryParse(args[1], out var x) || !int.TryParse(args[2], out var y) ||
                !int.TryParse(args[3], out var id) || !float.TryParse(args[4], out var intensity))
            {
                return;
            }

            var gridId = new GridId(id);
            Vector2i tile = (x, y);

            float slope = 1;
            if (args.Length > 5 && !float.TryParse(args[5], out slope))
                return;

            float maxIntensity = 50;
            if (args.Length > 6 && !float.TryParse(args[6], out maxIntensity))
                return;

            bool directedExplosion = args.Length > 7;
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

            var excluded = !directedExplosion
                ? new HashSet<Vector2i>()
                : explosionSystem.GetDirectionalRestriction(gridId, tile, Angle.FromDegrees(angle), spread, distance);

            if (args[0] == "spawn")
            {
                EntitySystem.Get<ExplosionSystem>().SpawnExplosion(gridId, tile, intensity, slope, maxIntensity, excluded);
                return;
            }

            if (player?.AttachedEntity == null)
            {
                shell.WriteLine("You must have an attached entity.");
                return;
            }

            EntitySystem.Get<ExplosionDebugOverlaySystem>().Preview(player, gridId, tile, intensity, slope, maxIntensity, excluded);
        }
    }
}
