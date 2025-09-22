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

        // AI state variables stored in localAI
        private ref float PreviousDistanceToTarget => ref Projectile.localAI[0];
        private ref float MissDetectionTimer => ref Projectile.localAI[1];
        private ref float ConfidenceLevel => ref Projectile.localAI[2];
        
        // Track target's previous position and velocity for better prediction
        private Vector2 lastTargetPosition;
        private Vector2 lastTargetVelocity;
        private Vector2 predictedTargetAcceleration;
        private int targetTrackingFrames;
        
        // AI behavior states
        private enum AIState {
            Seeking,      // Looking for target and initial approach
            Tracking,     // Actively tracking with high confidence
            Correcting,   // Detected potential miss, correcting course
            Recovering    // Lost target or missed, trying to recover
        }
        
        private AIState currentState = AIState.Seeking;

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
                    
                    // Change trail color based on AI state
                    Color baseColor = currentState switch {
                        AIState.Seeking => new Color(26, 238, 199),      // Cyan-green
                        AIState.Tracking => new Color(0, 255, 100),      // Bright green (confident)
                        AIState.Correcting => new Color(255, 150, 0),    // Orange (correcting)
                        AIState.Recovering => new Color(255, 50, 50),    // Red (lost)
                        _ => new Color(26, 238, 199)
                    };
                    
                    Color col = Color.Lerp(baseColor, new Color(0, 240, 255), trailT) * (1f - trailT) * 0.9f;

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
            const float maxDetectRadius = 12000f;
            const float minSpeed = 2f;
            const float maxSpeed = 32f;
            const float baseAcceleration = 0.3f;
            
            // Add short delay before homing activates
            if (DelayTimer < 5) {
                DelayTimer += 1;
                if (Projectile.velocity == Vector2.Zero) {
                    Projectile.velocity = new Vector2(8f, 0).RotatedBy(Projectile.rotation);
                }
                return;
            }

            // The owning player is responsible for target selection
            if (Projectile.owner == Main.myPlayer) {
                if (HomingTarget == null || !IsValidTarget(HomingTarget)) {
                    HomingTarget = FindOptimalTarget(maxDetectRadius);
                    if (HomingTarget != null) {
                        // Reset tracking when new target acquired
                        targetTrackingFrames = 0;
                        currentState = AIState.Seeking;
                        ConfidenceLevel = 0.3f;
                    }
                    Projectile.netUpdate = true;
                }
            }

            // No target behavior
            if (HomingTarget == null) {
                currentState = AIState.Recovering;
                HandleNoTarget();
                return;
            }

            // Update target tracking data
            UpdateTargetTracking();
            
            // AI decision making based on current situation
            AnalyzeSituation();
            
            // Execute behavior based on current state
            switch (currentState) {
                case AIState.Seeking:
                    HandleSeeking(minSpeed, maxSpeed, baseAcceleration);
                    break;
                case AIState.Tracking:
                    HandleTracking(minSpeed, maxSpeed, baseAcceleration);
                    break;
                case AIState.Correcting:
                    HandleCorrecting(minSpeed, maxSpeed, baseAcceleration);
                    break;
                case AIState.Recovering:
                    HandleRecovering(minSpeed, maxSpeed, baseAcceleration);
                    break;
            }

            // Update rotation to match velocity direction
            Projectile.rotation = Projectile.velocity.ToRotation();
        }

        private void UpdateTargetTracking() {
            if (HomingTarget == null) return;
            
            Vector2 currentTargetPos = HomingTarget.Center;
            Vector2 currentTargetVel = HomingTarget.velocity;
            
            if (targetTrackingFrames > 0) {
                // Calculate target acceleration for better prediction
                Vector2 velocityChange = currentTargetVel - lastTargetVelocity;
                predictedTargetAcceleration = Vector2.Lerp(predictedTargetAcceleration, velocityChange, 0.3f);
            }
            
            lastTargetPosition = currentTargetPos;
            lastTargetVelocity = currentTargetVel;
            targetTrackingFrames++;
        }

        private void AnalyzeSituation() {
            if (HomingTarget == null) return;
            
            float distanceToTarget = Vector2.Distance(Projectile.Center, HomingTarget.Center);
            
            // Miss detection - check if we're getting further from target
            if (PreviousDistanceToTarget > 0) {
                if (distanceToTarget > PreviousDistanceToTarget + 2f) {
                    MissDetectionTimer++;
                    if (MissDetectionTimer > 8) { // Detected potential miss
                        if (currentState == AIState.Tracking) {
                            currentState = AIState.Correcting;
                            ConfidenceLevel = Math.Max(0.1f, ConfidenceLevel - 0.4f);
                        }
                    }
                } else {
                    MissDetectionTimer = Math.Max(0, MissDetectionTimer - 1);
                    if (distanceToTarget < PreviousDistanceToTarget - 5f) {
                        // Getting closer, increase confidence
                        ConfidenceLevel = Math.Min(1f, ConfidenceLevel + 0.05f);
                    }
                }
            }
            
            PreviousDistanceToTarget = distanceToTarget;
            
            // State transitions based on analysis
            if (currentState == AIState.Seeking && ConfidenceLevel > 0.6f) {
                currentState = AIState.Tracking;
            } else if (currentState == AIState.Correcting && ConfidenceLevel > 0.4f && MissDetectionTimer <= 2) {
                currentState = AIState.Tracking;
            } else if (distanceToTarget > 2000f && currentState != AIState.Recovering) {
                currentState = AIState.Recovering;
                ConfidenceLevel = 0.2f;
            }
        }

        private void HandleNoTarget() {
            // Maintain current velocity but slow down gradually
            float currentSpeed = Projectile.velocity.Length();
            float targetSpeed = Math.Max(4f, currentSpeed * 0.98f);
            
            if (currentSpeed > 0) {
                Projectile.velocity = Vector2.Normalize(Projectile.velocity) * targetSpeed;
            }
        }

        private void HandleSeeking(float minSpeed, float maxSpeed, float baseAcceleration) {
            Vector2 interceptPoint = CalculateInterceptPoint(2f); // Conservative prediction
            Vector2 toIntercept = interceptPoint - Projectile.Center;
            
            float distanceToTarget = toIntercept.Length();
            float currentSpeed = Projectile.velocity.Length();
            
            // Moderate speed during seeking
            float targetSpeed = MathHelper.Lerp(8f, 18f, Math.Min(distanceToTarget / 600f, 1f));
            
            // Smooth acceleration
            float acceleration = baseAcceleration * 0.8f;
            if (currentSpeed < targetSpeed) {
                currentSpeed = Math.Min(currentSpeed + acceleration, targetSpeed);
            } else if (currentSpeed > targetSpeed) {
                currentSpeed = Math.Max(currentSpeed - acceleration, targetSpeed);
            }
            
            // Moderate turning rate during seeking
            Vector2 desiredVelocity = Vector2.Normalize(toIntercept) * currentSpeed;
            float turnRate = 0.12f;
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;
        }

        private void HandleTracking(float minSpeed, float maxSpeed, float baseAcceleration) {
            Vector2 interceptPoint = CalculateInterceptPoint(4f); // Aggressive prediction
            Vector2 toIntercept = interceptPoint - Projectile.Center;
            
            float distanceToTarget = toIntercept.Length();
            float currentSpeed = Projectile.velocity.Length();
            
            // High speed when confident and tracking
            float baseTargetSpeed = MathHelper.Lerp(maxSpeed * 0.7f, maxSpeed, ConfidenceLevel);
            float targetSpeed = Math.Min(baseTargetSpeed, distanceToTarget / 10f + 8f); // Slow down when very close
            
            // Fast acceleration when tracking
            float acceleration = baseAcceleration * (1f + ConfidenceLevel);
            if (currentSpeed < targetSpeed) {
                currentSpeed = Math.Min(currentSpeed + acceleration, targetSpeed);
            }
            
            // High turning rate when confident
            Vector2 desiredVelocity = Vector2.Normalize(toIntercept) * currentSpeed;
            float turnRate = 0.25f * (0.5f + ConfidenceLevel * 0.5f);
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;
        }

        private void HandleCorrecting(float minSpeed, float maxSpeed, float baseAcceleration) {
            Vector2 interceptPoint = CalculateInterceptPoint(1.5f); // Conservative prediction
            Vector2 toIntercept = interceptPoint - Projectile.Center;
            
            float currentSpeed = Projectile.velocity.Length();
            
            // Slow down significantly to correct course
            float targetSpeed = Math.Min(currentSpeed, minSpeed + 6f);
            currentSpeed = Math.Max(currentSpeed - baseAcceleration * 1.5f, targetSpeed);
            
            // Very aggressive turning to correct
            Vector2 desiredVelocity = Vector2.Normalize(toIntercept) * currentSpeed;
            float turnRate = 0.35f; // Very high turn rate
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;
            
            // Reset miss detection timer as we're actively correcting
            MissDetectionTimer = Math.Max(0, MissDetectionTimer - 2);
        }

        private void HandleRecovering(float minSpeed, float maxSpeed, float baseAcceleration) {
            if (HomingTarget != null) {
                Vector2 toTarget = HomingTarget.Center - Projectile.Center;
                float distanceToTarget = toTarget.Length();
                
                // Slow approach while recovering
                float currentSpeed = Projectile.velocity.Length();
                float targetSpeed = Math.Min(10f, distanceToTarget / 100f + 4f);
                
                if (currentSpeed < targetSpeed) {
                    currentSpeed = Math.Min(currentSpeed + baseAcceleration * 0.5f, targetSpeed);
                } else {
                    currentSpeed = Math.Max(currentSpeed - baseAcceleration * 0.5f, targetSpeed);
                }
                
                // Gentle turning during recovery
                Vector2 desiredVelocity = Vector2.Normalize(toTarget) * currentSpeed;
                float turnRate = 0.08f;
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
                Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;
                
                // Gradually rebuild confidence
                ConfidenceLevel = Math.Min(0.5f, ConfidenceLevel + 0.01f);
                
                if (distanceToTarget < 800f) {
                    currentState = AIState.Seeking;
                }
            }
        }

        private Vector2 CalculateInterceptPoint(float predictionStrength) {
            if (HomingTarget == null || targetTrackingFrames < 3) {
                return HomingTarget?.Center ?? Projectile.Center;
            }
            
            Vector2 targetPos = HomingTarget.Center;
            Vector2 targetVel = HomingTarget.velocity;
            
            // Calculate time to intercept using iterative approach
            float projectileSpeed = Projectile.velocity.Length();
            if (projectileSpeed < 1f) projectileSpeed = 8f; // fallback speed
            
            Vector2 relativePos = targetPos - Projectile.Center;
            float timeToIntercept = relativePos.Length() / projectileSpeed;
            
            // Iterative refinement for better accuracy
            for (int i = 0; i < 3; i++) {
                Vector2 predictedPos = targetPos + targetVel * timeToIntercept * predictionStrength;
                
                // Include acceleration prediction for more advanced targets
                if (targetTrackingFrames > 10) {
                    predictedPos += predictedTargetAcceleration * timeToIntercept * timeToIntercept * 0.5f * predictionStrength;
                }
                
                float newDistance = Vector2.Distance(Projectile.Center, predictedPos);
                timeToIntercept = newDistance / projectileSpeed;
            }
            
            Vector2 finalPredictedPos = targetPos + targetVel * timeToIntercept * predictionStrength;
            
            // Add acceleration component
            if (targetTrackingFrames > 10) {
                finalPredictedPos += predictedTargetAcceleration * timeToIntercept * timeToIntercept * 0.5f * predictionStrength;
            }
            
            // Wall avoidance check
            if (!Collision.CanHitLine(Projectile.Center, 0, 0, finalPredictedPos, 0, 0)) {
                // Find alternative path around obstacles
                Vector2 toTarget = Vector2.Normalize(finalPredictedPos - Projectile.Center);
                Vector2 perpendicular = new Vector2(-toTarget.Y, toTarget.X);
                
                Vector2 leftPath = finalPredictedPos + perpendicular * 80f;
                Vector2 rightPath = finalPredictedPos - perpendicular * 80f;
                
                bool leftClear = Collision.CanHitLine(Projectile.Center, 0, 0, leftPath, 0, 0);
                bool rightClear = Collision.CanHitLine(Projectile.Center, 0, 0, rightPath, 0, 0);
                
                if (leftClear && !rightClear) {
                    finalPredictedPos = leftPath;
                } else if (rightClear && !leftClear) {
                    finalPredictedPos = rightPath;
                }
                // If neither or both are clear, stick with original prediction
            }
            
            return finalPredictedPos;
        }

        private NPC FindOptimalTarget(float maxDetectDistance) {
            NPC chosen = null;
            float bestScore = float.MinValue;
            float sqrMaxDetect = maxDetectDistance * maxDetectDistance;

            foreach (NPC npc in Main.ActiveNPCs) {
                if (IsValidTarget(npc)) {
                    float sqrDistance = Vector2.DistanceSquared(Projectile.Center, npc.Center);
                    if (sqrDistance < sqrMaxDetect) {
                        // Scoring system considering multiple factors
                        float distance = (float)Math.Sqrt(sqrDistance);
                        float distanceScore = 1f - (distance / maxDetectDistance); // Closer = better
                        
                        float healthScore = 1f - (npc.life / (float)npc.lifeMax); // Lower health = better
                        
                        // Prefer targets that are easier to intercept (slower or predictable movement)
                        float velocityMagnitude = npc.velocity.Length();
                        float mobilityScore = 1f - Math.Min(velocityMagnitude / 20f, 1f); // Slower = better
                        
                        // Line of sight bonus
                        float losScore = Collision.CanHitLine(Projectile.Center, 0, 0, npc.Center, 0, 0) ? 1f : 0.3f;
                        
                        // Combined score with weights
                        float totalScore = distanceScore * 0.4f + healthScore * 0.3f + mobilityScore * 0.2f + losScore * 0.1f;
                        
                        if (totalScore > bestScore) {
                            bestScore = totalScore;
                            chosen = npc;
                        }
                    }
                }
            }

            return chosen;
        }

        private bool IsValidTarget(NPC target) {
            return target.CanBeChasedBy()
                && !target.friendly
                && target.life > 0
                && target.active;
        }

        public override void OnKill(int timeLeft) {
            // This code and the similar code above in OnTileCollide spawn dust from the tiles collided with. SoundID.Item10 is the bounce sound you hear.
            Collision.HitTiles(Projectile.position + Projectile.velocity, Projectile.velocity, Projectile.width, Projectile.height);
            SoundEngine.PlaySound(SoundID.Item10, Projectile.position);
        }
    }
}
