using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Content.Projectiles {
    public class AImmoProjectile : ModProjectile {
        // Wrap target storage in ai[0] (offset by +1 so 0 = no target)
        private NPC HomingTarget {
            get => Projectile.ai[0] == 0 ? null : Main.npc[(int)Projectile.ai[0] - 1];
            set => Projectile.ai[0] = value == null ? 0 : value.whoAmI + 1;
        }

        // ai[1] is used as a delay timer before homing starts
        public ref float DelayTimer => ref Projectile.ai[1];

        public override void SetStaticDefaults() {
            // Keep consistent with vanilla: Cultists resist homing projectiles
            ProjectileID.Sets.CultistIsResistantTo[Projectile.type] = true;

            // Cache a longer trail and use a trailing mode suited for custom drawing
            ProjectileID.Sets.TrailCacheLength[Projectile.type] = 30;
            ProjectileID.Sets.TrailingMode[Projectile.type] = 2; // cache exact positions for smoother custom trails
        }

        public override void SetDefaults() {
            Projectile.width = 4;
            Projectile.height = 4;

            Projectile.aiStyle = 1;
            Projectile.friendly = true;

            Projectile.penetrate = 1;
            Projectile.light = 0.5f;

            Projectile.alpha = 255;
            Projectile.extraUpdates = 8;

            Projectile.scale = 1.2f;
            Projectile.timeLeft = 1200;

            Projectile.extraUpdates = 8;
            Projectile.DamageType = DamageClass.Ranged;

            AIType = ProjectileID.Bullet; // Copy bullet base movement
        }

        public override bool PreDraw(ref Color lightColor) {
            // Additive trail similar to vanilla Chlorophyte bullet
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            Vector2 origin = texture.Size() / 2f;

            // Switch to additive for the trail only
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                Main.GameViewMatrix.TransformationMatrix
            );

            for (int i = 1; i < Projectile.oldPos.Length; i++) {
                Vector2 pos = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
                float t = i / (float)Projectile.oldPos.Length; // 0 (head) -> 1 (tail)

                // Bright yellow-green near head fading to green tail
                Color col = Color.Lerp(new Color(26, 238, 199), new Color(0, 240, 255), t) * (1f - t) * 0.9f;

                // Slightly taper trail width
                float scale = Projectile.scale * MathHelper.Lerp(0.6f, 1.2f, 1f - t);

                Main.spriteBatch.Draw(texture, pos, null, col, Projectile.rotation, origin, scale, SpriteEffects.None, 0f);
            }

            // Restore normal blend state and draw the main projectile on top
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                Main.GameViewMatrix.TransformationMatrix
            );

            Main.spriteBatch.Draw(texture, Projectile.Center - Main.screenPosition, null, Color.White, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);

            return false; // we've handled all drawing
        }

        public override void AI() {
            float maxDetectRadius = 1000f;

            // Add short delay before homing activates
            if (DelayTimer < 5) {
                DelayTimer += 1;
                return;
            }

            // The owning player is responsible for target selection
            if (Projectile.owner == Main.myPlayer) {
                if (HomingTarget == null || !IsValidTarget(HomingTarget)) {
                    HomingTarget = FindLowestLifeNPC(maxDetectRadius);
                    Projectile.netUpdate = true; // sync to clients
                }
            }

            // If we don't have a valid target, just keep going straight
            if (HomingTarget == null)
                return;

            float length = Projectile.velocity.Length();
            Vector2 toTarget = HomingTarget.Center - Projectile.Center;

            // OPTION 1: Angle clamp (vanilla-like, predictable turns)
            float targetAngle = Projectile.AngleTo(HomingTarget.Center);
            Projectile.velocity = Projectile.velocity.ToRotation()
                .AngleTowards(targetAngle, MathHelper.ToRadians(3f)) // 3 degrees per tick
                .ToRotationVector2() * length;
        }

        private NPC FindLowestLifeNPC(float maxDetectDistance) {
            NPC chosen = null;
            float lowestLife = float.MaxValue;
            float sqrMaxDetect = maxDetectDistance * maxDetectDistance;

            foreach (NPC npc in Main.ActiveNPCs) {
                if (IsValidTarget(npc)) {
                    float sqrDistance = Vector2.DistanceSquared(Projectile.Center, npc.Center);
                    if (sqrDistance < sqrMaxDetect && npc.life < lowestLife) {
                        lowestLife = npc.life;
                        chosen = npc;
                    }
                }
            }

            return chosen;
        }

        private bool IsValidTarget(NPC target) {
            // Same rules as ExampleHomingProjectile, but tweaked for lowest-HP targeting
            return target.CanBeChasedBy()
                && !target.friendly
                && target.life > 0
                && Collision.CanHit(Projectile.Center, 1, 1, target.position, target.width, target.height);
        }
    }
}
