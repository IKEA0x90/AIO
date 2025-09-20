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

            ProjectileID.Sets.TrailCacheLength[Projectile.type] = 10;
            ProjectileID.Sets.TrailingMode[Projectile.type] = 1; // 0 = simple trail, 1 = additive-style trail
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
            // Get texture
            Texture2D texture = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
            Vector2 origin = texture.Size() / 2f;

            // Draw old positions (trail)
            for (int i = 0; i < Projectile.oldPos.Length; i++) {
                Vector2 drawPos = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
                // Fade opacity as trail gets older
                float opacity = (Projectile.oldPos.Length - i) / (float)Projectile.oldPos.Length;

                // Bright green like Chlorophyte
                Color trailColor = Color.LimeGreen * opacity;

                Main.spriteBatch.Draw(texture, drawPos, null, trailColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);
            }

            // Draw main projectile on top
            Main.spriteBatch.Draw(texture, Projectile.Center - Main.screenPosition, null, Color.White, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);

            return false; // we've handled all drawing
        }

        public override void AI() {
            float maxDetectRadius = 400f;

            // Add short delay before homing activates
            if (DelayTimer < 10) {
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

            /**
            if (Main.rand.NextBool(3)) { // 1-in-3 chance per tick
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GreenTorch);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].scale = 1.2f;
            }
            */

            // If we don't have a valid target, just keep going straight
            if (HomingTarget == null)
                return;

            float length = Projectile.velocity.Length();
            Vector2 toTarget = HomingTarget.Center - Projectile.Center;

            // --------- HOMING SMOOTHING METHODS ---------

            // OPTION 1: Angle clamp (vanilla-like, predictable turns)
            // Rotates projectile velocity toward target by a max of X degrees per frame
            /**
            float targetAngle = Projectile.AngleTo(HomingTarget.Center);
            Projectile.velocity = Projectile.velocity.ToRotation()
                .AngleTowards(targetAngle, MathHelper.ToRadians(3f)) // 3 degrees per tick
                .ToRotationVector2() * length;
            */

            // OPTION 2: Velocity Lerp (smoother, more "heat-seeking")
            // Interpolates between current and desired velocity
            float homingStrength = 0.5f; // larger = tighter turning
            Vector2 desiredVelocity = toTarget.SafeNormalize(Vector2.Zero) * length;
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, homingStrength);

            Projectile.rotation = Projectile.velocity.ToRotation();
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
