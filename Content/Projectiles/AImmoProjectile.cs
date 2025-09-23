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

        // Smoothed velocities/accelerations to avoid impulsive jumps (fixes ground dive bug)
        private Vector2 smoothedTargetVelocity;
        private Vector2 smoothedTargetAcceleration;

        // AI behavior states
        private enum AIState {
            Seeking,      // Looking for target and initial approach
            Tracking,     // Actively tracking with high confidence
            Correcting,   // Detected potential miss, correcting course
            Recovering    // Lost target or missed, trying to recover
        }

        private AIState currentState = AIState.Seeking;

        // Tunables for prediction & avoidance
        private const float PredictionSmoothing = 0.08f; // Exponential smoothing factor (lower = less reactive)
        private const float AccelSmoothing = 0.10f;
        private const float MaxVerticalPredictionPerSec = 600f; // applied as a velocity clamp in prediction
        private const float WorldGravity = 0.35f; // typical NPC gravity approximation per tick
        private const float SimStepTicks = 3f; // simulation timestep (ticks) for target motion prediction

        public override void SetStaticDefaults() {
            ProjectileID.Sets.CultistIsResistantTo[Projectile.type] = true;
            ProjectileID.Sets.TrailCacheLength[Projectile.type] = 60;
            ProjectileID.Sets.TrailingMode[Projectile.type] = 2;
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
            Texture2D pixelTexture = TextureAssets.MagicPixel.Value;

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

            for (int i = 0; i < Projectile.oldPos.Length - 1; i++) {
                if (Projectile.oldPos[i] == Vector2.Zero || Projectile.oldPos[i + 1] == Vector2.Zero)
                    continue;

                Vector2 start = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
                Vector2 end = Projectile.oldPos[i + 1] + Projectile.Size / 2f - Main.screenPosition;

                float distance = Vector2.Distance(start, end);
                int steps = Math.Max(1, (int)(distance / 2f)); // 2 pixel steps

                for (int j = 0; j <= steps; j++) {
                    float stepT = j / (float)steps;
                    Vector2 pos = Vector2.Lerp(start, end, stepT);

                    float trailT = (i + stepT) / (float)Projectile.oldPos.Length; // Overall trail position

                    Color baseColor = currentState switch {
                        AIState.Seeking => new Color(26, 238, 199),
                        AIState.Tracking => new Color(0, 255, 100),
                        AIState.Correcting => new Color(255, 150, 0),
                        AIState.Recovering => new Color(255, 50, 50),
                        _ => new Color(26, 238, 199)
                    };

                    Color col = Color.Lerp(baseColor, new Color(0, 240, 255), trailT) * (1f - trailT) * 0.9f;

                    Rectangle pixelRect = new Rectangle((int)pos.X - 1, (int)pos.Y - 1, 2, 2);
                    Main.spriteBatch.Draw(pixelTexture, pixelRect, col);
                }
            }

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

            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            Vector2 origin = texture.Size() / 2f;
            Main.spriteBatch.Draw(texture, Projectile.Center - Main.screenPosition, null, Color.White, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);

            return false;
        }

        public override void AI() {
            const float maxDetectRadius = 12000f;
            const float minSpeed = 2f;
            const float maxSpeed = 32f;
            const float baseAcceleration = 0.3f;

            if (DelayTimer < 5) {
                DelayTimer += 1;
                if (Projectile.velocity == Vector2.Zero) {
                    Projectile.velocity = new Vector2(8f, 0).RotatedBy(Projectile.rotation);
                }
                return;
            }

            if (Projectile.owner == Main.myPlayer) {
                if (HomingTarget == null || !IsValidTarget(HomingTarget)) {
                    HomingTarget = FindOptimalTarget(maxDetectRadius);
                    if (HomingTarget != null) {
                        targetTrackingFrames = 0;
                        currentState = AIState.Seeking;
                        ConfidenceLevel = 0.3f;

                        // initialize smoothing
                        smoothedTargetVelocity = HomingTarget.velocity; // px/tick
                        smoothedTargetAcceleration = Vector2.Zero;      // px/tick^2
                    }
                    Projectile.netUpdate = true;
                }
            }

            if (HomingTarget == null) {
                currentState = AIState.Recovering;
                HandleNoTarget();
                Projectile.rotation = Projectile.velocity.ToRotation();
                return;
            }

            // Update target tracking & smoothing
            UpdateTargetTracking();

            // AI decision making based on current situation
            AnalyzeSituation();

            // Pick an intercept point (with advanced simulation)
            float predictionStrength = currentState == AIState.Tracking ? 4f : currentState == AIState.Correcting ? 1.5f : 2f;

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

            // Safety: if a collision with tiles is imminent (very short distance), push upward/sideways to avoid digging into ground
            Vector2 tileAvoid = ComputeImmediateTileAvoidance(Projectile.Center, Projectile.velocity);
            if (tileAvoid != Vector2.Zero) {
                // nudge velocity away from collision
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, Projectile.velocity + tileAvoid * Projectile.velocity.Length() * 0.75f, 0.5f);
            }

            Projectile.rotation = Projectile.velocity.ToRotation();
        }

        private void UpdateTargetTracking() {
            if (HomingTarget == null)
                return;

            Vector2 currentTargetPos = HomingTarget.Center;
            Vector2 currentTargetVel = HomingTarget.velocity; // px/tick

            // Exponential smoothing to remove noisy instantaneous spikes (esp. on NPC jump/land events)
            if (targetTrackingFrames == 0) {
                smoothedTargetVelocity = currentTargetVel;
                smoothedTargetAcceleration = Vector2.Zero;
            } else {
                // Work in px/tick units; acceleration is delta-velocity per tick (px/tick^2)
                Vector2 prevVel = smoothedTargetVelocity;
                Vector2 deltaVel = currentTargetVel - prevVel;

                smoothedTargetAcceleration = Vector2.Lerp(smoothedTargetAcceleration, deltaVel, AccelSmoothing);
                smoothedTargetVelocity = Vector2.Lerp(smoothedTargetVelocity, currentTargetVel, PredictionSmoothing);

                // Clamp sudden vertical changes to avoid mimicking bursty jumps/falls
                float maxVelYDeltaPerTick = 0.8f; // px/tick per frame change allowed in smoothed Y
                float velYDelta = smoothedTargetVelocity.Y - prevVel.Y;
                velYDelta = MathHelper.Clamp(velYDelta, -maxVelYDeltaPerTick, maxVelYDeltaPerTick);
                smoothedTargetVelocity.Y = prevVel.Y + velYDelta;

                // Clamp vertical acceleration to reduce wild arcs from instant landing/launch
                float maxVertAccelPerTick2 = 0.8f; // px/tick^2
                smoothedTargetAcceleration.Y = MathHelper.Clamp(smoothedTargetAcceleration.Y, -maxVertAccelPerTick2, maxVertAccelPerTick2);
            }

            lastTargetPosition = currentTargetPos;
            lastTargetVelocity = currentTargetVel;
            targetTrackingFrames++;
        }

        private void AnalyzeSituation() {
            if (HomingTarget == null)
                return;

            float distanceToTarget = Vector2.Distance(Projectile.Center, HomingTarget.Center);

            if (PreviousDistanceToTarget > 0) {
                if (distanceToTarget > PreviousDistanceToTarget + 2f) {
                    MissDetectionTimer++;
                    if (MissDetectionTimer > 8) {
                        if (currentState == AIState.Tracking) {
                            currentState = AIState.Correcting;
                            ConfidenceLevel = Math.Max(0.1f, ConfidenceLevel - 0.4f);
                        }
                    }
                } else {
                    MissDetectionTimer = Math.Max(0, MissDetectionTimer - 1);
                    if (distanceToTarget < PreviousDistanceToTarget - 5f) {
                        ConfidenceLevel = Math.Min(1f, ConfidenceLevel + 0.05f);
                    }
                }
            }

            PreviousDistanceToTarget = distanceToTarget;

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
            float currentSpeed = Projectile.velocity.Length();
            float targetSpeed = Math.Max(4f, currentSpeed * 0.98f);

            if (currentSpeed > 0) {
                Projectile.velocity = Vector2.Normalize(Projectile.velocity) * targetSpeed;
            }
        }

        private void HandleSeeking(float minSpeed, float maxSpeed, float baseAcceleration) {
            // Conservative prediction
            Vector2 interceptPoint = CalculateInterceptPoint(2f);
            Vector2 toIntercept = interceptPoint - Projectile.Center;

            float distanceToTarget = toIntercept.Length();
            float currentSpeed = Projectile.velocity.Length();

            float targetSpeed = MathHelper.Lerp(8f, 18f, Math.Min(distanceToTarget / 600f, 1f));
            float acceleration = baseAcceleration * 0.8f;
            if (currentSpeed < targetSpeed) {
                currentSpeed = Math.Min(currentSpeed + acceleration, targetSpeed);
            } else if (currentSpeed > targetSpeed) {
                currentSpeed = Math.Max(currentSpeed - acceleration, targetSpeed);
            }

            Vector2 desiredVelocity = (toIntercept.LengthSquared() < 1f) ? Projectile.velocity : Vector2.Normalize(toIntercept) * currentSpeed;

            // Avoid obstacles: steering / potential field
            desiredVelocity = ApplyAvoidance(desiredVelocity, 220f);

            float turnRate = 0.12f;
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;
        }

        private void HandleTracking(float minSpeed, float maxSpeed, float baseAcceleration) {
            Vector2 interceptPoint = CalculateInterceptPoint(4f);
            Vector2 toIntercept = interceptPoint - Projectile.Center;

            float distanceToTarget = toIntercept.Length();
            float currentSpeed = Projectile.velocity.Length();

            float baseTargetSpeed = MathHelper.Lerp(maxSpeed * 0.7f, maxSpeed, ConfidenceLevel);
            float targetSpeed = Math.Min(baseTargetSpeed, distanceToTarget / 10f + 8f);

            float acceleration = baseAcceleration * (1f + ConfidenceLevel);
            if (currentSpeed < targetSpeed) {
                currentSpeed = Math.Min(currentSpeed + acceleration, targetSpeed);
            }

            Vector2 desiredVelocity = (toIntercept.LengthSquared() < 1f) ? Projectile.velocity : Vector2.Normalize(toIntercept) * currentSpeed;

            // Stronger avoidance when confident (so it doesn't crash)
            desiredVelocity = ApplyAvoidance(desiredVelocity, 320f);

            float turnRate = 0.25f * (0.5f + ConfidenceLevel * 0.5f);
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;
        }

        private void HandleCorrecting(float minSpeed, float maxSpeed, float baseAcceleration) {
            Vector2 interceptPoint = CalculateInterceptPoint(1.5f);
            Vector2 toIntercept = interceptPoint - Projectile.Center;

            float currentSpeed = Projectile.velocity.Length();

            float targetSpeed = Math.Min(currentSpeed, minSpeed + 6f);
            currentSpeed = Math.Max(currentSpeed - baseAcceleration * 1.5f, targetSpeed);

            Vector2 desiredVelocity = (toIntercept.LengthSquared() < 1f) ? Projectile.velocity : Vector2.Normalize(toIntercept) * currentSpeed;

            // Strong avoidance + very aggressive turning
            desiredVelocity = ApplyAvoidance(desiredVelocity, 260f, preferUpwardBias: true);

            float turnRate = 0.35f;
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;

            MissDetectionTimer = Math.Max(0, MissDetectionTimer - 2);
        }

        private void HandleRecovering(float minSpeed, float maxSpeed, float baseAcceleration) {
            if (HomingTarget != null) {
                Vector2 toTarget = HomingTarget.Center - Projectile.Center;
                float distanceToTarget = toTarget.Length();

                float currentSpeed = Projectile.velocity.Length();
                float targetSpeed = Math.Min(10f, distanceToTarget / 100f + 4f);

                if (currentSpeed < targetSpeed) {
                    currentSpeed = Math.Min(currentSpeed + baseAcceleration * 0.5f, targetSpeed);
                } else {
                    currentSpeed = Math.Max(currentSpeed - baseAcceleration * 0.5f, targetSpeed);
                }

                Vector2 desiredVelocity = Vector2.Normalize(toTarget) * currentSpeed;

                desiredVelocity = ApplyAvoidance(desiredVelocity, 200f);

                float turnRate = 0.08f;
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, desiredVelocity, turnRate);
                Projectile.velocity = Vector2.Normalize(Projectile.velocity) * currentSpeed;

                ConfidenceLevel = Math.Min(0.5f, ConfidenceLevel + 0.01f);

                if (distanceToTarget < 800f) {
                    currentState = AIState.Seeking;
                }
            }
        }

        /// <summary>
        /// Calculate a robust intercept point using simulation and smoothed values.
        /// Uses tick-based units to match Terraria's velocity semantics and blends lead to avoid over-reacting.
        /// </summary>
        private Vector2 CalculateInterceptPoint(float predictionStrength) {
            if (HomingTarget == null)
                return Projectile.Center;

            // If we don't have enough frames tracked, fall back to quick prediction
            if (targetTrackingFrames < 2)
                return HomingTarget.Center;

            Vector2 shooter = Projectile.Center;
            float projectileSpeed = Projectile.velocity.Length(); // px/tick
            if (projectileSpeed < 1f)
                projectileSpeed = 8f;

            // Initial time estimate in ticks
            Vector2 relative = HomingTarget.Center - shooter;
            float predictedTicks = relative.Length() / projectileSpeed;

            // Clamp to reasonable window
            predictedTicks = MathHelper.Clamp(predictedTicks, 4f, 6f * 60f);

            // Use simulation of the target's motion (small dt steps) using smoothed velocity & accel (px/tick, px/tick^2)
            Vector2 simPos = HomingTarget.Center;
            Vector2 simVel = smoothedTargetVelocity;
            Vector2 simAccel = smoothedTargetAcceleration;

            bool applyGravity = !HomingTarget.noGravity;
            float maxVertSpeedPerTick = MaxVerticalPredictionPerSec / 60f; // px/tick

            // Iteratively refine time by simulating forward and computing distance
            for (int iter = 0; iter < 4; iter++) {
                simPos = HomingTarget.Center;
                simVel = smoothedTargetVelocity;
                simAccel = smoothedTargetAcceleration;

                int steps = Math.Max(1, (int)Math.Ceiling(predictedTicks / SimStepTicks));
                float dtTicks = predictedTicks / steps; // ticks per sub-step

                for (int s = 0; s < steps; s++) {
                    // integrate acceleration (smoothed) in px/tick^2
                    simVel += simAccel * dtTicks;

                    // gravity influence per tick
                    if (applyGravity) {
                        simVel.Y += WorldGravity * dtTicks;
                    }

                    // clamp vertical speed
                    simVel.Y = MathHelper.Clamp(simVel.Y, -maxVertSpeedPerTick, maxVertSpeedPerTick);

                    // step position using px/tick velocity
                    simPos += simVel * dtTicks;

                    // if simPos hits the ground (tile collision), snap to surface and zero vertical vel
                    Rectangle npcRect = new Rectangle((int)(simPos.X - HomingTarget.width / 2), (int)(simPos.Y - HomingTarget.height / 2), HomingTarget.width, HomingTarget.height);
                    if (Collision.SolidCollision(new Vector2(npcRect.X, npcRect.Y), npcRect.Width, npcRect.Height)) {
                        int maxPush = 32;
                        for (int push = 0; push < maxPush; push++) {
                            npcRect.Y -= 1;
                            if (!Collision.SolidCollision(new Vector2(npcRect.X, npcRect.Y), npcRect.Width, npcRect.Height)) {
                                simPos.Y = npcRect.Y + HomingTarget.height / 2;
                                simVel.Y = 0;
                                break;
                            }
                        }
                        if (Collision.SolidCollision(new Vector2(npcRect.X, npcRect.Y), npcRect.Width, npcRect.Height)) {
                            break;
                        }
                    }
                }

                // compute new time estimate (ticks)
                float newTicks = Vector2.Distance(shooter, simPos) / projectileSpeed;
                // damp changes and set for next iteration
                predictedTicks = MathHelper.Lerp(predictedTicks, newTicks, 0.6f);
                predictedTicks = MathHelper.Clamp(predictedTicks, 2f, 6f * 60f);
            }

            // Final refined predicted position using final predictedTicks
            Vector2 finalPredPos = HomingTarget.Center;
            Vector2 finalVel = smoothedTargetVelocity;
            Vector2 finalAccel = smoothedTargetAcceleration;

            int finalSteps = Math.Max(1, (int)Math.Ceiling(predictedTicks / SimStepTicks));
            float finalDtTicks = predictedTicks / finalSteps;
            for (int s = 0; s < finalSteps; s++) {
                finalVel += finalAccel * finalDtTicks;
                if (!HomingTarget.noGravity)
                    finalVel.Y += WorldGravity * finalDtTicks;
                finalVel.Y = MathHelper.Clamp(finalVel.Y, -maxVertSpeedPerTick, maxVertSpeedPerTick);
                finalPredPos += finalVel * finalDtTicks;
                // handle landing like above
                Rectangle npcRect = new Rectangle((int)(finalPredPos.X - HomingTarget.width / 2), (int)(finalPredPos.Y - HomingTarget.height / 2), HomingTarget.width, HomingTarget.height);
                if (Collision.SolidCollision(new Vector2(npcRect.X, npcRect.Y), npcRect.Width, npcRect.Height)) {
                    int maxPush = 32;
                    for (int push = 0; push < maxPush; push++) {
                        npcRect.Y -= 1;
                        if (!Collision.SolidCollision(new Vector2(npcRect.X, npcRect.Y), npcRect.Width, npcRect.Height)) {
                            finalPredPos.Y = npcRect.Y + HomingTarget.height / 2;
                            finalVel.Y = 0;
                            break;
                        }
                    }
                    if (Collision.SolidCollision(new Vector2(npcRect.X, npcRect.Y), npcRect.Width, npcRect.Height)) {
                        break;
                    }
                }
            }

            // Blend the lead to avoid over-anticipation; map predictionStrength to a 0..1 lead factor
            float lead = MathHelper.Clamp(predictionStrength / 4.5f, 0.35f, 0.85f);
            Vector2 blendedPredPos = Vector2.Lerp(HomingTarget.Center, finalPredPos, lead);

            // If target is falling (positive Y velocity) and affected by gravity, avoid aiming too far below its current Y to prevent ground-dives
            if (!HomingTarget.noGravity && smoothedTargetVelocity.Y > 0.5f) {
                float maxDownLead = 120f; // pixels below current target center
                blendedPredPos.Y = Math.Min(blendedPredPos.Y, HomingTarget.Center.Y + maxDownLead);
            }

            // Wall avoidance: if we can't reach directly, try offset paths
            if (!Collision.CanHitLine(Projectile.Center, 0, 0, blendedPredPos, 0, 0)) {
                Vector2 toTarget = blendedPredPos - Projectile.Center;
                if (toTarget == Vector2.Zero)
                    return Projectile.Center;
                Vector2 dir = Vector2.Normalize(toTarget);
                Vector2 perp = new Vector2(-dir.Y, dir.X);

                Vector2 best = blendedPredPos;
                Vector2 leftPath = blendedPredPos + perp * 80f;
                Vector2 rightPath = blendedPredPos - perp * 80f;
                bool leftClear = Collision.CanHitLine(Projectile.Center, 0, 0, leftPath, 0, 0);
                bool rightClear = Collision.CanHitLine(Projectile.Center, 0, 0, rightPath, 0, 0);

                if (leftClear && !rightClear)
                    best = leftPath;
                else if (rightClear && !leftClear)
                    best = rightPath;
                else {
                    // sample more offsets (spiral) to handle narrow cave turns
                    for (float offset = 40f; offset <= 320f; offset += 40f) {
                        Vector2 candA = blendedPredPos + perp * offset;
                        Vector2 candB = blendedPredPos - perp * offset;
                        if (Collision.CanHitLine(Projectile.Center, 0, 0, candA, 0, 0)) { best = candA; break; }
                        if (Collision.CanHitLine(Projectile.Center, 0, 0, candB, 0, 0)) { best = candB; break; }
                    }
                }

                blendedPredPos = best;
            }

            return blendedPredPos;
        }

        /// <summary>
        /// Apply local avoidance to a desired velocity vector.
        /// - cast several rays around the desired direction and choose the best
        /// - add repulsive vector from nearby solid tiles
        /// - optionally prefer an upward bias to avoid ground collisions
        /// </summary>
        private Vector2 ApplyAvoidance(Vector2 desiredVelocity, float lookAheadDistance, bool preferUpwardBias = false) {
            if (desiredVelocity == Vector2.Zero)
                return desiredVelocity;

            Vector2 origin = Projectile.Center;
            float desiredSpeed = desiredVelocity.Length();
            Vector2 desiredDir = Vector2.Normalize(desiredVelocity);

            // 1) Repulsive field from nearby tiles
            Vector2 repulse = Vector2.Zero;
            int tileRadius = 6; // tiles to scan around projectile
            int px = (int)(origin.X / 16f);
            int py = (int)(origin.Y / 16f);
            for (int dx = -tileRadius; dx <= tileRadius; dx++) {
                for (int dy = -tileRadius; dy <= tileRadius; dy++) {
                    int tx = px + dx;
                    int ty = py + dy;
                    if (tx < 0 || tx >= Main.maxTilesX || ty < 0 || ty >= Main.maxTilesY)
                        continue;
                    // quick solid tile test
                    if (Collision.SolidTiles(tx, ty, tx + 1, ty + 1)) {
                        Vector2 tileCenter = new Vector2(tx * 16 + 8, ty * 16 + 8);
                        float dist = Vector2.Distance(tileCenter, origin);
                        if (dist <= 0.001f)
                            continue;
                        float influence = MathHelper.Clamp(1f - (dist / (tileRadius * 16f)), 0f, 1f);
                        Vector2 away = Vector2.Normalize(origin - tileCenter) * influence;
                        repulse += away * (1f + (tileRadius - Math.Abs(dx) - Math.Abs(dy)) * 0.05f);
                    }
                }
            }

            // scale repulsion
            repulse *= 1.35f;

            // 2) Ray-sample candidate directions (prefer those close to desiredDir but with clear line)
            int sampleCount = 11;
            float maxAngle = MathHelper.PiOver2; // +/- 90 degrees
            float bestScore = float.MinValue;
            Vector2 bestDir = desiredDir;

            for (int i = 0; i < sampleCount; i++) {
                float t = i / (float)(sampleCount - 1); // 0..1
                float angle = MathHelper.Lerp(-maxAngle, maxAngle, t);
                // shape samples so center samples are tried first (bias to desired)
                float bias = (float)Math.Cos((t - 0.5f) * Math.PI);
                Vector2 sampleDir = desiredDir.RotatedBy(angle * (1f - Math.Abs(Vector2.Dot(desiredDir, Vector2.UnitY)))); // gentle bias

                Vector2 probe = origin + sampleDir * lookAheadDistance;
                bool clear = Collision.CanHitLine(origin, 0, 0, probe, 0, 0);
                // score: prefer closeness to desiredDir and clear lines, penalize collisions
                float dirScore = Vector2.Dot(desiredDir, sampleDir);
                float clearanceScore = clear ? 1f : 0f;
                float score = dirScore * 0.8f + clearanceScore * 0.4f + 0.2f * bias;
                // if it's clear, reward proximity of line (use distance to nearest tile as tiebreaker)
                if (!clear)
                    score -= 0.5f;
                if (score > bestScore) {
                    bestScore = score;
                    bestDir = sampleDir;
                }
            }

            // 3) Upward bias if requested or if the ray ahead detects ground collision soon
            Vector2 forwardProbe = origin + desiredDir * Math.Min(lookAheadDistance, 160f);
            bool forwardClear = Collision.CanHitLine(origin, 0, 0, forwardProbe, 0, 0);
            Vector2 upwardBias = Vector2.Zero;
            if (!forwardClear || preferUpwardBias) {
                upwardBias = new Vector2(0, -1f) * 0.9f;
            }

            // combine bestDir, repulsion, and upward bias
            Vector2 combined = bestDir + repulse * 0.6f + upwardBias * 0.8f;
            if (combined == Vector2.Zero)
                combined = bestDir;
            combined = Vector2.Normalize(combined) * desiredSpeed;

            return combined;
        }

        /// <summary>
        /// If a tile collision is imminent in the direct velocity path (very short range),
        /// returns a small avoidance vector to nudge the projectile away from the tile.
        /// </summary>
        private Vector2 ComputeImmediateTileAvoidance(Vector2 origin, Vector2 vel) {
            if (vel == Vector2.Zero)
                return Vector2.Zero;

            float look = Math.Min(64f + vel.Length() * 3f, 220f);
            Vector2 probe = origin + Vector2.Normalize(vel) * look;

            // If direct line is blocked, try offsetting upward/sideways
            if (!Collision.CanHitLine(origin, 0, 0, probe, 0, 0)) {
                // find the nearest collision point by stepping
                int steps = 12;
                for (int i = 1; i <= steps; i++) {
                    float t = i / (float)steps;
                    Vector2 p = Vector2.Lerp(origin, probe, t);
                    Rectangle checkRect = new Rectangle((int)(p.X - Projectile.width / 2f), (int)(p.Y - Projectile.height / 2f), Projectile.width, Projectile.height);
                    if (Collision.SolidCollision(new Vector2(checkRect.X, checkRect.Y), checkRect.Width, checkRect.Height)) {
                        // compute avoidance vector away from the tile center
                        int tx = (int)(p.X / 16f);
                        int ty = (int)(p.Y / 16f);
                        Vector2 tileCenter = new Vector2(tx * 16 + 8, ty * 16 + 8);
                        Vector2 away = Vector2.Normalize(origin - tileCenter);
                        // prefer upward if tile is mostly below
                        if (away.Y > -0.2f)
                            away.Y = -0.6f; // bias up
                        return away;
                    }
                }
                // general fallback: upward nudge
                return new Vector2(0, -0.6f);
            }

            return Vector2.Zero;
        }

        private NPC FindOptimalTarget(float maxDetectDistance) {
            NPC chosen = null;
            float bestScore = float.MinValue;
            float sqrMaxDetect = maxDetectDistance * maxDetectDistance;

            foreach (NPC npc in Main.ActiveNPCs) {
                if (IsValidTarget(npc)) {
                    float sqrDistance = Vector2.DistanceSquared(Projectile.Center, npc.Center);
                    if (sqrDistance < sqrMaxDetect) {
                        float distance = (float)Math.Sqrt(sqrDistance);
                        float distanceScore = 1f - (distance / maxDetectDistance); // Closer = better

                        float healthScore = 1f - (npc.life / (float)npc.lifeMax); // Lower health = better

                        float velocityMagnitude = npc.velocity.Length();
                        float mobilityScore = 1f - Math.Min(velocityMagnitude / 20f, 1f); // Slower = better

                        float losScore = Collision.CanHitLine(Projectile.Center, 0, 0, npc.Center, 0, 0) ? 1f : 0.3f;

                        float totalScore = distanceScore * 0.45f + healthScore * 0.25f + mobilityScore * 0.15f + losScore * 0.15f;

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
            return target != null
                && target.CanBeChasedBy()
                && !target.friendly
                && target.life > 0
                && target.active;
        }

        public override void OnKill(int timeLeft) {
            Collision.HitTiles(Projectile.position + Projectile.velocity, Projectile.velocity, Projectile.width, Projectile.height);
            SoundEngine.PlaySound(SoundID.Item10, Projectile.position);
        }
    }
}
