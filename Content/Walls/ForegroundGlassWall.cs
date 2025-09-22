using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AIO.Content.Walls {
    public class ForegroundGlassWall : ModWall {
        public override void SetStaticDefaults() {
            // Clone vanilla Glass Wall stats
            Main.wallHouse[Type] = Main.wallHouse[WallID.Glass];
            DustType = DustID.Glass;
            AddMapEntry(new Color(200, 245, 255));
        }

        // Skip vanilla wall rendering (behind players)
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch) {
            return false;
        }
    }

    public class ForegroundGlassWallSystem : ModSystem {
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) {
            int index = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (index != -1) {
                layers.Insert(index, new LegacyGameInterfaceLayer(
                    "YourMod: Foreground Glass Wall",
                    delegate {
                        DrawForegroundWalls(Main.spriteBatch);
                        return true;
                    },
                    InterfaceScaleType.Game)
                );
            }
        }

        private void DrawForegroundWalls(SpriteBatch spriteBatch) {
            spriteBatch.End();
            spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.Default,
                RasterizerState.CullCounterClockwise,
                null,
                Main.GameViewMatrix.TransformationMatrix
            );

            Texture2D wallTexture = TextureAssets.Wall[WallID.Glass].Value;

            // Calculate visible tile range for performance
            int startX = (int)(Main.screenPosition.X / 16f) - 1;
            int endX = (int)((Main.screenPosition.X + Main.screenWidth) / 16f) + 2;
            int startY = (int)(Main.screenPosition.Y / 16f) - 1;
            int endY = (int)((Main.screenPosition.Y + Main.screenHeight) / 16f) + 2;

            startX = Utils.Clamp(startX, 0, Main.maxTilesX);
            endX = Utils.Clamp(endX, 0, Main.maxTilesX);
            startY = Utils.Clamp(startY, 0, Main.maxTilesY);
            endY = Utils.Clamp(endY, 0, Main.maxTilesY);

            for (int i = startX; i < endX; i++) {
                for (int j = startY; j < endY; j++) {
                    Tile tile = Main.tile[i, j];
                    if (tile == null || tile.WallType != ModContent.WallType<ForegroundGlassWall>())
                        continue;

                    Color color = Lighting.GetColor(i, j); // Apply vanilla lighting
                    Vector2 pos = new Vector2(i * 16, j * 16) - Main.screenPosition;
                    Rectangle frame = new Rectangle(tile.WallFrameX, tile.WallFrameY, 32, 32);

                    spriteBatch.Draw(wallTexture, pos, frame, color);
                }
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
        }
    }
}
