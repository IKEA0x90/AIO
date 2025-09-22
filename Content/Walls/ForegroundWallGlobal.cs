using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using AIO.Content.Systems;

namespace AIO.Content.Walls {
    // Global hook to suppress vanilla wall drawing for foreground-painted walls
    public class ForegroundWallGlobal : GlobalWall {
        public override bool PreDraw(int i, int j, int type, SpriteBatch spriteBatch) {
            // If this wall is painted as foreground, skip vanilla draw (we'll draw it in front later)
            return !ForegroundWallPaintSystem.IsForeground(i, j);
        }

        public override void KillWall(int i, int j, int type, ref bool fail) {
            // Clean up paint mark when wall is removed
            if (!fail)
                ForegroundWallPaintSystem.ClearIfNoWall(i, j);
        }
    }
}
