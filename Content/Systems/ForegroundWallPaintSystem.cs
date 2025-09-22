using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace AIO.Content.Systems {
    // Stores which wall tiles are painted as foreground, handles drawing and net sync
    public class ForegroundWallPaintSystem : ModSystem {
        // Positions of painted foreground walls
        public static HashSet<Point16> ForegroundWalls { get; private set; } = new();

        public override void OnWorldLoad() {
            ForegroundWalls = new HashSet<Point16>();
        }

        public override void OnWorldUnload() {
            ForegroundWalls?.Clear();
            ForegroundWalls = null;
        }

        public override void SaveWorldData(TagCompound tag) {
            if (ForegroundWalls != null && ForegroundWalls.Count > 0) {
                var arr = new List<int>(ForegroundWalls.Count * 2);
                foreach (var p in ForegroundWalls) {
                    arr.Add(p.X);
                    arr.Add(p.Y);
                }
                tag["AIO_ForegroundWalls"] = arr;
            }
        }

        public override void LoadWorldData(TagCompound tag) {
            ForegroundWalls = new HashSet<Point16>();
            if (tag.TryGet("AIO_ForegroundWalls", out List<int> arr)) {
                for (int i = 0; i + 1 < arr.Count; i += 2) {
                    int x = arr[i];
                    int y = arr[i + 1];
                    if (x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY) {
                        var t = Framing.GetTileSafely(x, y);
                        if (t.WallType > 0) {
                            ForegroundWalls.Add(new Point16((short)x, (short)y));
                        }
                    }
                }
            }
        }

        public override void NetSend(BinaryWriter writer) {
            writer.Write(ForegroundWalls?.Count ?? 0);
            if (ForegroundWalls != null) {
                foreach (var p in ForegroundWalls) {
                    writer.Write(p.X);
                    writer.Write(p.Y);
                }
            }
        }

        public override void NetReceive(BinaryReader reader) {
            int count = reader.ReadInt32();
            ForegroundWalls = new HashSet<Point16>();
            for (int k = 0; k < count; k++) {
                short x = reader.ReadInt16();
                short y = reader.ReadInt16();
                if (x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY) {
                    var t = Framing.GetTileSafely(x, y);
                    if (t.WallType > 0)
                        ForegroundWalls.Add(new Point16(x, y));
                }
            }
        }

        public static bool IsForeground(int i, int j) => ForegroundWalls != null && ForegroundWalls.Contains(new Point16((short)i, (short)j));

        public static void SetForeground(int i, int j, bool value) {
            if (ForegroundWalls == null)
                ForegroundWalls = new HashSet<Point16>();

            var p = new Point16((short)i, (short)j);
            if (value)
                ForegroundWalls.Add(p);
            else
                ForegroundWalls.Remove(p);
        }

        public static void ClearIfNoWall(int i, int j) {
            if (ForegroundWalls == null)
                return;
            if (!WorldGen.InWorld(i, j))
                return;
            Tile t = Framing.GetTileSafely(i, j);
            if (t.WallType == 0)
                ForegroundWalls.Remove(new Point16((short)i, (short)j));
        }

        public override void ModifyInterfaceLayers(System.Collections.Generic.List<GameInterfaceLayer> layers) {
            //int index = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Inventory"));
            int index = 0;
            if (index != -1) {
                layers.Insert(index, new LegacyGameInterfaceLayer(
                    "AIO: Foreground Painted Walls",
                    delegate {
                        DrawForegroundWalls(Main.spriteBatch);
                        return true;
                    },
                    InterfaceScaleType.Game)
                );
            }
        }

        private void DrawForegroundWalls(SpriteBatch spriteBatch) {
            if (ForegroundWalls == null || ForegroundWalls.Count == 0)
                return;

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

            // Visible range for performance
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
                    if (!IsForeground(i, j))
                        continue;

                    Tile tile = Main.tile[i, j];
                    if (tile == null || tile.WallType <= 0)
                        continue;

                    // Ensure the texture for this wall type is loaded (works for vanilla and modded walls)
                    Main.instance.LoadWall(tile.WallType);
                    Texture2D wallTexture = TextureAssets.Wall[tile.WallType].Value;

                    Color color = Lighting.GetColor(i, j); // Apply vanilla lighting
                    Vector2 pos = new Vector2(i * 16, j * 16) - Main.screenPosition;

                    // Use the tile's wall framing info
                    Rectangle frame = new Rectangle(tile.WallFrameX, tile.WallFrameY, 32, 32);

                    spriteBatch.Draw(wallTexture, pos, frame, color);
                }
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
        }

        // Networking helpers
        internal enum PacketType : byte { SetPaint }

        public static void SendPaintChange(int i, int j, bool value) {
            if (Main.netMode == NetmodeID.SinglePlayer) {
                SetForeground(i, j, value);
                return;
            }

            ModPacket packet = ModContent.GetInstance<AIO>().GetPacket();
            packet.Write((byte)PacketType.SetPaint);
            packet.Write((short)i);
            packet.Write((short)j);
            packet.Write(value);
            packet.Send();
        }

        public static void HandlePacket(BinaryReader reader, int whoAmI) {
            PacketType type = (PacketType)reader.ReadByte();
            switch (type) {
                case PacketType.SetPaint: {
                    int i = reader.ReadInt16();
                    int j = reader.ReadInt16();
                    bool value = reader.ReadBoolean();

                    SetForeground(i, j, value);

                    if (Main.netMode == NetmodeID.Server) {
                        // Broadcast to other clients
                        ModPacket packet = ModContent.GetInstance<AIO>().GetPacket();
                        packet.Write((byte)PacketType.SetPaint);
                        packet.Write((short)i);
                        packet.Write((short)j);
                        packet.Write(value);
                        packet.Send(ignoreClient: whoAmI);
                    }
                    break;
                }
            }
        }
    }
}
