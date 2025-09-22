using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Audio;
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

            // Cache more positions for smoother pixel trail
            ProjectileID.Sets.TrailCacheLength[Projectile.type] = 60;
            ProjectileID.Sets.TrailingMode[Projectile.type] = 2; // cache exact positions for smoother custom trails
        }

        public override void SetDefaults() {
            Projectile.width = 4;
            Projectile.height = 4;

            Projectile.aiStyle = 0;
            Projectile.friendly = true;

            Projectile.penetrate = 1;
            Projectile.light = 0.5f;

            Projectile.alpha = 255;
            Projectile.extraUpdates = 0;

            Projectile.scale = 1.2f;
            Projectile.timeLeft = 10000;

            Projectile.DamageType = DamageClass.Ranged;
        }

        public override bool PreDraw(ref Color lightColor) {
            // Draw interpolated pixel trail to fill gaps
            Texture2D pixelTexture = TextureAssets.MagicPixel.Value;
            
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

            // Draw interpolated trail between positions to fill gaps
            for (int i = 0; i < Projectile.oldPos.Length - 1; i++) {
                if (Projectile.oldPos[i] == Vector2.Zero || Projectile.oldPos[i + 1] == Vector2.Zero) continue;
                
                Vector2 start = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
                Vector2 end = Projectile.oldPos[i + 1] + Projectile.Size / 2f - Main.screenPosition;
                
                float distance = Vector2.Distance(start, end);
                int steps = Math.Max(1, (int)(distance / 2f)); // 2 pixel steps
                
                for (int j = 0; j <= steps; j++) {
                    float stepT = j / (float)steps;
                    Vector2 pos = Vector2.Lerp(start, end, stepT);
                    
                    float trailT = (i + stepT) / (float)Projectile.oldPos.Length; // Overall trail position
                    
                    // Bright cyan-green near head fading to transparent
                    Color col = Color.Lerp(new Color(26, 238, 199), new Color(0, 240, 255), trailT) * (1f - trailT) * 0.9f;

                    // Draw individual pixels
                    Rectangle pixelRect = new Rectangle((int)pos.X - 1, (int)pos.Y - 1, 2, 2);
                    Main.spriteBatch.Draw(pixelTexture, pixelRect, col);
                }
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

            // Draw the main bullet as a bright pixel
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            Vector2 origin = texture.Size() / 2f;
            Main.spriteBatch.Draw(texture, Projectile.Center - Main.screenPosition, null, Color.White, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);

            return false; // we've handled all drawing
        }

        public override void AI() {
            float maxDetectRadius = 10000f;
            float initialSpeed = 6f; // Start slow
            float maxSpeed = 24f; // Maximum speed when accelerating
            float accelerationRate = 0.5f; // How fast to accelerate

            // Add short delay before homing activates
            if (DelayTimer < 5) { // Longer delay for better initial movement
                DelayTimer += 1;
                // Move straight during delay at initial speed
                if (Projectile.velocity == Vector2.Zero) {
                    Projectile.velocity = new Vector2(initialSpeed, 0).RotatedBy(Projectile.rotation);
                }
                return;
            }

            // The owning player is responsible for target selection
            if (Projectile.owner == Main.myPlayer) {
                if (HomingTarget == null || !IsValidTarget(HomingTarget)) {
                    HomingTarget = FindLowestLifeNPC(maxDetectRadius);
                    Projectile.netUpdate = true; // sync to clients
                }
            }

            // Get current speed once for reuse
            float currentSpeed = Projectile.velocity.Length();

            // If we don't have a valid target, maintain current velocity at initial speed
            if (HomingTarget == null) {
                if (currentSpeed != initialSpeed) {
                    Projectile.velocity = Vector2.Normalize(Projectile.velocity) * initialSpeed;
                }
                Projectile.rotation = Projectile.velocity.ToRotation();
                return;
            }

            // Calculate distance to target for speed adjustment
            float distanceToTarget = Vector2.Distance(Projectile.Center, HomingTarget.Center);
            
            // Accelerate based on having a target and distance
            float targetSpeed = MathHelper.Lerp(maxSpeed, initialSpeed, Math.Min(distanceToTarget / 400f, 1f)); // Slower when very close
            
            if (currentSpeed < targetSpeed) {
                currentSpeed = Math.Min(currentSpeed + accelerationRate, targetSpeed);
            }

            // Improved targeting with wall avoidance
            Vector2 toTarget = HomingTarget.Center - Projectile.Center;
            
            // Check if direct path is blocked by walls
            bool directPathBlocked = !Collision.CanHitLine(
                Projectile.Center, 0, 0,
                HomingTarget.Center, 0, 0
            );
            
            Vector2 desiredDirection;
            if (directPathBlocked) {
                // Try to find a path around obstacles
                Vector2 normalizedToTarget = Vector2.Normalize(toTarget);
                Vector2 perpendicular = new Vector2(-normalizedToTarget.Y, normalizedToTarget.X);
                
                // Test both sides to find clearer path
                Vector2 leftTest = Projectile.Center + (normalizedToTarget + perpendicular * 0.5f) * 100f;
                Vector2 rightTest = Projectile.Center + (normalizedToTarget - perpendicular * 0.5f) * 100f;
                
                bool leftClear = Collision.CanHitLine(Projectile.Center, 0, 0, leftTest, 0, 0);
                bool rightClear = Collision.CanHitLine(Projectile.Center, 0, 0, rightTest, 0, 0);
                
                if (leftClear && !rightClear) {
                    desiredDirection = Vector2.Normalize(normalizedToTarget + perpendicular * 0.3f);
                } else if (rightClear && !leftClear) {
                    desiredDirection = Vector2.Normalize(normalizedToTarget - perpendicular * 0.3f);
                } else {
                    // If both or neither are clear, use direct path
                    desiredDirection = normalizedToTarget;
                }
            } else {
                // Direct path is clear
                desiredDirection = Vector2.Normalize(toTarget);
            }
            
            // Predict target movement for better accuracy
            Vector2 predictedPosition = HomingTarget.Center + HomingTarget.velocity * 15f;
            Vector2 toPredicted = Vector2.Normalize(predictedPosition - Projectile.Center);
            
            // Blend direct and predicted targeting
            desiredDirection = Vector2.Normalize(Vector2.Lerp(desiredDirection, toPredicted, 0.6f));
            
            Vector2 desiredVelocity = desiredDirection * currentSpeed;
            
            // Smoothly turn towards target with aggressive turning
            float turnRate = 0.2f; // Very aggressive turning
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
            
            // Ensure consistent speed
            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;
            
            // Update rotation to match velocity direction
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
            // Enhanced target validation with better line of sight check
            return target.CanBeChasedBy()
                && !target.friendly
                && target.life > 0
                && target.active
                && Collision.CanHitLine(
                    Projectile.Center, 0, 0,
                    target.Center, 0, 0
                );
        }

        public override void OnKill(int timeLeft) {
            // This code and the similar code above in OnTileCollide spawn dust from the tiles collided with. SoundID.Item10 is the bounce sound you hear.
            Collision.HitTiles(Projectile.position + Projectile.velocity, Projectile.velocity, Projectile.width, Projectile.height);
            SoundEngine.PlaySound(SoundID.Item10, Projectile.position);
        }
    }
}
