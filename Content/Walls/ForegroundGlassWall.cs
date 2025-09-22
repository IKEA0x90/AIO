using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Graphics;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace YourMod.Content.Walls
{
    // The wall: clones glass behavior, but skips the vanilla draw.
    public class ForegroundGlassWall : ModWall
    {
        public override void SetStaticDefaults()
        {
            // Clone vanilla Glass wall behavior (housing, etc.)
            Main.wallHouse[Type] = Main.wallHouse[WallID.Glass];
            DustType = DustID.Glass;
            AddMapEntry(new Color(200, 245, 255));
            // Use vanilla sound and drop behavior by default (no need to override)
        }

        // Prevent vanilla wall from being drawn behind tiles
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        {
            return false;
        }
    }

    // System which draws our foreground glass after entities
    public class ForegroundGlassWallSystem : ModSystem
    {
        private GlassRenderer _renderer;

        public override void Load()
        {
            if (!Main.dedServ)
            {
                _renderer = new GlassRenderer();
            }
        }

        public override void Unload()
        {
            _renderer?.Dispose();
            _renderer = null;
        }

        // Insert draw layer right before mouse text so it's over players but under UI text
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            if (Main.dedServ) return;

            int index = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (index != -1)
            {
                layers.Insert(index, new LegacyGameInterfaceLayer(
                    "YourMod: Foreground Glass Wall",
                    delegate
                    {
                        // Only run on clients
                        if (Main.netMode == NetmodeID.Server) return true;

                        _renderer?.Draw();
                        return true;
                    },
                    InterfaceScaleType.Game)
                );
            }
        }
    }

    // Renderer that simulates a glass shader using a screen capture + noise-based distortion.
    internal class GlassRenderer : IDisposable
    {
        // configurable parameters (tweak for look/perf)
        private const int TilePixelSize = 32; // wall textures/frame size (vanilla uses 32x32 frames for walls)
        private readonly float DistortionStrength = 6f; // how much background shifts (px)
        private readonly float DistortionScale = 0.0125f; // noise sampling scale
        private readonly float SpecularStrength = 0.45f;
        private readonly float EdgeDarkness = 0.35f;
        private readonly int NoiseSize = 256;

        private RenderTarget2D sceneCapture;
        private Texture2D noiseTexture;
        private Texture2D roundGradient; // small circular gradient for highlights
        private int lastBackbufferW = 0, lastBackbufferH = 0;

        // Reuse these rectangles to avoid allocations
        private Rectangle sourceRect = new Rectangle();
        private Rectangle destRect = new Rectangle();

        public GlassRenderer()
        {
            CreateNoiseTexture();
            CreateGradientTexture();
        }

        private void EnsureRenderTarget()
        {
            int w = Main.screenWidth;
            int h = Main.screenHeight;
            if (sceneCapture == null || w != lastBackbufferW || h != lastBackbufferH)
            {
                sceneCapture?.Dispose();
                GraphicsDevice gd = Main.graphics.GraphicsDevice;
                // Use SurfaceFormat.Color for compatibility
                sceneCapture = new RenderTarget2D(gd, Math.Max(1, w), Math.Max(1, h), false, SurfaceFormat.Color, DepthFormat.None);
                lastBackbufferW = w;
                lastBackbufferH = h;
            }
        }

        private void CreateNoiseTexture()
        {
            // procedural grayscale noise
            int size = NoiseSize;
            noiseTexture?.Dispose();
            noiseTexture = new Texture2D(Main.graphics.GraphicsDevice, size, size, false, SurfaceFormat.Color);
            Random rand = new Random(12345); // deterministic seed for consistent look across clients
            Color[] data = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Perlin-like feel by combining a few random values could be added, but
                    // simple smooth noise is fine; keep values in mid-range for subtle distortion.
                    int v = (int)(128 + (rand.NextDouble() - 0.5) * 64); // ~[96,160]
                    v = Math.Max(0, Math.Min(255, v));
                    data[y * size + x] = new Color(v, v, v, v);
                }
            }
            noiseTexture.SetData(data);
        }

        private void CreateGradientTexture()
        {
            // make a small radial gradient (white center -> transparent edges) for specular highlights
            int size = 64;
            roundGradient?.Dispose();
            roundGradient = new Texture2D(Main.graphics.GraphicsDevice, size, size, false, SurfaceFormat.Color);
            Color[] data = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.Length();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    float t = MathHelper.Clamp(1f - (d / (size * 0.5f)), 0f, 1f);
                    // pow for sharper falloff
                    t = (float)Math.Pow(t, 2.2f);
                    Color c = new Color(255, 255, 255, (int)(255 * t));
                    data[y * size + x] = c;
                }
            }
            roundGradient.SetData(data);
        }

        // Capture the current backbuffer into sceneCapture to use for refracting the background
        private void CaptureScene()
        {
            EnsureRenderTarget();
            GraphicsDevice gd = Main.graphics.GraphicsDevice;

            // set render target
            gd.SetRenderTarget(sceneCapture);

            // Draw the current backbuffer into the render target. Terraria doesn't expose a direct "copy backbuffer"
            // but we can force-draw the current screen by sampling Main.screenTarget if available.
            // Main.screenTarget contains the game's rendered world contents (before UI draws) in recent tML versions.
            // Fallback approach: clear and let the game have already drawn the world; but proper approach:
            SpriteBatch sb = Main.spriteBatch;
            sb.End();

            // If tModLoader/Vanilla has Main.screenTarget available (most modern versions), use it.
            // Otherwise, draw nothing because copying is not possible; we still get a reasonable visual via lighting.
            Texture2D source = Main.screenTarget; // safe to reference; might be null on some versions
            sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity);

            if (source != null)
            {
                // source is already world-space; draw it fully
                sb.Draw(source, new Rectangle(0, 0, sceneCapture.Width, sceneCapture.Height), Color.White);
            }
            else
            {
                // Fallback: clear to transparent black; we still draw highlights & refracted lighting from Lighting.GetColor calls
                sb.GraphicsDevice.Clear(Color.Transparent);
            }

            sb.End();

            // reset render target
            gd.SetRenderTarget(null);

            // resume typical batch usage (caller will set proper batch)
        }

        private float SampleNoise(float worldX, float worldY, float time)
        {
            // Sample noise texture at scaled coordinates using bilinear-like sampling via GetData would be expensive.
            // Instead, map to noise tex coords and fetch via GetData? That's slow. So we'll calculate offset from simple functions:
            // Use sin/cos combos for cheap pseudo-noise that is deterministic and fast.
            float s = (float)(Math.Sin(worldX * 0.023f + time * 0.8f) + Math.Cos(worldY * 0.019f - time * 0.7f));
            return (s * 0.5f + 0.5f); // [0,1]
        }

        // Main draw entry: called once per frame
        public void Draw()
        {
            if (Main.netMode == NetmodeID.Server) return;

            // Step 1: capture the current scene into sceneCapture
            // This must be done before we draw our foreground tiles so we can sample the correct background.
            CaptureScene();

            SpriteBatch sb = Main.spriteBatch;

            // Begin world-space drawing (GameViewMatrix) so coordinates are in screen pixels relative to world
            sb.End();
            sb.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.Default,
                RasterizerState.CullCounterClockwise,
                null,
                Main.GameViewMatrix.TransformationMatrix
            );

            // Determine visible tile range (only iterate onscreen tiles)
            int startX = (int)(Main.screenPosition.X / 16f) - 2;
            int endX = (int)((Main.screenPosition.X + Main.screenWidth) / 16f) + 2;
            int startY = (int)(Main.screenPosition.Y / 16f) - 2;
            int endY = (int)((Main.screenPosition.Y + Main.screenHeight) / 16f) + 2;

            startX = Utils.Clamp(startX, 0, Main.maxTilesX - 1);
            endX = Utils.Clamp(endX, 0, Main.maxTilesX - 1);
            startY = Utils.Clamp(startY, 0, Main.maxTilesY - 1);
            endY = Utils.Clamp(endY, 0, Main.maxTilesY - 1);

            // get the wall texture region for glass (we will not use wall framing logic here; glass already uses simple frames)
            Texture2D wallTexture = TextureAssets.Wall[WallID.Glass].Value;

            // time for animation
            float time = (float)Main.GlobalTimeWrappedHourly;

            // precompute some values
            Vector2 screenPos = Main.screenPosition;

            // iterate visible tiles
            for (int i = startX; i <= endX; i++)
            {
                for (int j = startY; j <= endY; j++)
                {
                    Tile tile = Main.tile[i, j];
                    if (tile == null || tile.WallType != ModContent.WallType<ForegroundGlassWall>())
                        continue;

                    // world pixel position of this tile
                    float worldX = i * 16f;
                    float worldY = j * 16f;
                    Vector2 drawPos = new Vector2(worldX, worldY);

                    // compute simple noise-based distortion offset (cheap)
                    float n = SampleNoise(worldX * DistortionScale, worldY * DistortionScale, time);
                    // map noise to [-1,1]
                    float nd = (n - 0.5f) * 2f;
                    Vector2 offset = new Vector2(nd * DistortionStrength, (float)Math.Sin(worldY * 0.01f + time) * (DistortionStrength * 0.3f));

                    // source rectangle into the captured scene (in screen coords)
                    // convert drawPos to screen-space
                    Vector2 screenSpace = drawPos - screenPos;
                    int sx = (int)(screenSpace.X + offset.X);
                    int sy = (int)(screenSpace.Y + offset.Y);
                    int srcW = TilePixelSize;
                    int srcH = TilePixelSize;

                    // clamp source rect inside scene capture
                    sx = Math.Max(0, Math.Min(sx, sceneCapture.Width - srcW));
                    sy = Math.Max(0, Math.Min(sy, sceneCapture.Height - srcH));
                    sourceRect.X = sx;
                    sourceRect.Y = sy;
                    sourceRect.Width = srcW;
                    sourceRect.Height = srcH;

                    // dest rect in world-space where we'll draw that sampled region (16x16 world tile = 32 px texture)
                    destRect.X = (int)worldX;
                    destRect.Y = (int)worldY;
                    destRect.Width = 16 * 2; // because vanilla wall texture frames use 32 px per tile; world tile is 16px, texture frame 32 => scale 0.5
                    destRect.Height = 16 * 2;

                    // Because Terraria wall textures are 32x32 per tile-frame but world tile size is 16,
                    // we use a direct mapping where the destination is 16px world size, but to avoid blurry results,
                    // we draw the src 32px region into a 16px area using PointClamp sampler earlier.
                    // To keep consistent sizing, we draw with dest width/height = 16 (world pixels), but because we use Main.GameViewMatrix, scaling might occur; keep simple:
                    // We will draw at world tile scale (16x16) to match crispness.
                    destRect.Width = 16;
                    destRect.Height = 16;

                    // Draw the refracted background (sample from sceneCapture)
                    sb.Draw(sceneCapture, destRect, sourceRect, Color.White * 0.95f);

                    // Apply tint based on lighting at tile
                    Color lightColor = Lighting.GetColor(i, j);
                    // multiply to simulate glass tint / transmission
                    sb.Draw(wallTexture, destRect, new Rectangle(tile.WallFrameX, tile.WallFrameY, TilePixelSize, TilePixelSize), lightColor * 0.5f);

                    // Draw edge darkening (simulate thickness)
                    sb.Draw(wallTexture, destRect, new Rectangle(tile.WallFrameX, tile.WallFrameY, TilePixelSize, TilePixelSize), Color.Black * EdgeDarkness);

                    // Draw a faint specular highlight using gradient, offsetted so highlight moves with time (gives suggestion of curvature)
                    float highlightScale = 0.9f + (float)Math.Sin((worldX + worldY) * 0.001f + time * 1.4f) * 0.08f;
                    Vector2 highlightPos = new Vector2(worldX + 4 + (nd * 1.5f), worldY + 2 - (nd * 0.5f));
                    Rectangle highlightDest = new Rectangle((int)highlightPos.X, (int)highlightPos.Y, (int)(roundGradient.Width * 0.18f * highlightScale), (int)(roundGradient.Height * 0.18f * highlightScale));
                    sb.Draw(roundGradient, highlightDest, Color.White * SpecularStrength * 0.8f);

                    // Optional: subtle frame overlay (small alpha)
                    sb.Draw(wallTexture, destRect, new Rectangle(tile.WallFrameX, tile.WallFrameY, TilePixelSize, TilePixelSize), Color.White * 0.12f);
                }
            }

            // finish world draw
            sb.End();

            // Resume UI scaling batch so other UI renders normally
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
        }

        public void Dispose()
        {
            sceneCapture?.Dispose();
            sceneCapture = null;
            noiseTexture?.Dispose();
            noiseTexture = null;
            roundGradient?.Dispose();
            roundGradient = null;
        }
    }
}
