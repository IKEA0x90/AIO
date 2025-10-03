using AIO.Content.Buffs;
using AIO.Content.Items.Accessories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AIO.Common {
    public class AccessoryModelManager : ModSystem {
        public const float SampleIntervalSeconds = 0.5f;
        public const float LabelHorizonSeconds = 3.0f;
        public const int FeatureDim = 12;
        public const int MinibatchSize = 16;
        public const float LR0 = 0.01f;
        public const float LRDecay = 1e-4f;
        public const float Lambda = 1e-3f;
        public const float ActivationThreshold = 0.90f; // Lower threshold from 0.80 to 0.60
        public const int ActivationConsecutiveNeeded = 2; // Reduce from 3 to 2
        public const int BuffDurationSeconds = 2;
        public const int CooldownSeconds = 3;
        public const float SmoothAlpha = 0.5f;
        public const int FeatureVersion = PredictorAccessoryMk2.CurrentFeatureVersion;
        public const float DamageClipShort = 200f;
        public const float DamageClipLong = 600f;
        public const float MaxEnemyDamageClip = 200f;
        public const float DefenseClip = 200f;
        public const float ArmorReductionDebuffValue = 0f;

        private static AccessoryModelManager _instance;
        public static AccessoryModelManager Instance => _instance;
        private readonly Dictionary<string, ModelState> _models = new();
        private Players.PlayerHistoryStore _historyStore;
        private double _nextSampleTime;

        public override void OnWorldLoad() {
            _instance = this; _models.Clear(); _historyStore = new Players.PlayerHistoryStore(); _nextSampleTime = 0;
        }
        public override void OnWorldUnload() { _models.Clear(); _historyStore = null; _instance = null; }
        public override void PostUpdateEverything() { if (Main.netMode == NetmodeID.MultiplayerClient) return; double now = Main.GameUpdateCount / 60.0; PushPlayerHistory(now); if (now >= _nextSampleTime) { _nextSampleTime = now + SampleIntervalSeconds; TickUpdate(now); } }
        private void PushPlayerHistory(double now) { for (int i = 0; i < Main.maxPlayers; i++) { Player p = Main.player[i]; if (p == null || !p.active) continue; _historyStore.PushTick(i, now, p.statLife, p.statLifeMax2, p.potionDelay, p.velocity.Y, IsInHazard(p)); } }
        private bool IsInHazard(Player p) => p.lavaWet || p.honeyWet || p.onFire;
        
        // Fixed to handle both PredictorAccessory and PredictorAccessoryMk2
        public static void MarkEquippedServer(Player player, ModItem accessory) { 
            if (Main.netMode == NetmodeID.MultiplayerClient) return; 
            string accessoryID = null;
            if (accessory is PredictorAccessory pa) {
                accessoryID = pa.AccessoryID;
            } else if (accessory is PredictorAccessoryMk2 pa2) {
                accessoryID = pa2.AccessoryID;
            }
            if (string.IsNullOrWhiteSpace(accessoryID)) return; 
            var mgr = Instance; 
            if (!mgr._models.ContainsKey(accessoryID)) 
                mgr.TryLoadModelFromItem(accessoryID, accessory.Item); 
            if (mgr._models.TryGetValue(accessoryID, out var state)) 
                state.LastSeenPlayer = player.whoAmI; 
        }
        
        public void UpgradeToStage2Server(string accessoryId, Item item) { 
            if (!_models.TryGetValue(accessoryId, out var state)) { 
                TryLoadModelFromItem(accessoryId, item); 
                if (!_models.TryGetValue(accessoryId, out state)) return; 
            } 
            state.Stage = 2; 
            state.PendingSamples.Clear(); 
            
            // Ensure the model exists even if no training data was collected
            // This creates a baseline model that defaults to ~50% probability
            if (state.Model == null) {
                state.Model = new LRModel(FeatureDim);
                // Initialize with small random weights to avoid always predicting exactly 0.5
                var rng = new Random();
                for (int i = 0; i < state.Model.W.Length; i++) {
                    state.Model.W[i] = (float)(rng.NextDouble() * 0.1 - 0.05); // Small random values
                }
                state.Model.B = (float)(rng.NextDouble() * 0.2 - 0.1); // Small bias
            }
            
            SaveModelStateBackToItem(item, state); 
        }
        
        private void TickUpdate(double now) { 
            for (int i = 0; i < Main.maxPlayers; i++) { 
                Player p = Main.player[i]; 
                if (p == null || !p.active) continue; 
                var info = p.GetModPlayer<Players.InfoDisplayPlayer>(); 
                for (int slot = 3; slot < 10 + p.extraAccessorySlots; slot++) { 
                    Item item = p.armor[slot]; 
                    ModItem modItem = item?.ModItem;
                    string accessoryID = null;
                    int stage = 1;
                    
                    if (modItem is PredictorAccessory pa) {
                        accessoryID = pa.AccessoryID;
                        stage = pa.Stage;
                    } else if (modItem is PredictorAccessoryMk2 pa2) {
                        accessoryID = pa2.AccessoryID;
                        stage = pa2.Stage;
                    }
                    
                    if (string.IsNullOrWhiteSpace(accessoryID)) continue;
                    
                    if (!_models.TryGetValue(accessoryID, out var state)) { 
                        TryLoadModelFromItem(accessoryID, item); 
                        if (!_models.TryGetValue(accessoryID, out state)) { 
                            state = new ModelState(accessoryID, FeatureDim); 
                            _models[accessoryID] = state; 
                            SaveModelStateBackToItem(item, state); 
                        } 
                    } 
                    state.LastSeenPlayer = p.whoAmI; 
                    state.Stage = stage; // Ensure stage matches item type
                    
                    if (state.Stage == 1) { 
                        SampleForAccessory(state, p, now); 
                        ResolveLabelsAndMaybeTrain(state, p, now); 
                    } else { 
                        InferenceAndMaybeActivate(state, p, now); 
                    } 
                    info.predictorProb = state.PSmoothed; 
                    info.predictorStage = state.Stage; 
                    info.predictorPositives = state.PositivesCount; 
                    info.predictorTotalSamples = state.TotalSamples; 
                    info.predictorCooldownTicks = state.CooldownTicks; 
                } 
            } 
            if (Main.GameUpdateCount % 300 == 0) PersistAllModels(); 
        }
        
        private void SampleForAccessory(ModelState state, Player p, double now) { float[] feats = BuildFeatureVector(p); state.EnqueueSample(new Sample { Timestamp = now, Features = feats }); }
        private void ResolveLabelsAndMaybeTrain(ModelState state, Player p, double now) { while (state.PendingSamples.Count > 0) { var s = state.PendingSamples.Peek(); if (s.Timestamp + LabelHorizonSeconds > now) break; state.PendingSamples.Dequeue(); bool died = _historyStore.DiedBetween(p.whoAmI, s.Timestamp, s.Timestamp + LabelHorizonSeconds); s.Label = died ? 1 : 0; state.AddLabeledSample(s); } if (state.TrainBuffer.Count >= MinibatchSize) state.PerformSGDStep(LR0, LRDecay, Lambda, MinibatchSize); }
        private void InferenceAndMaybeActivate(ModelState state, Player p, double now) { if (state.Model == null || state.Stage != 2) return; float[] feats = BuildFeatureVector(p); float prob = state.Model.PredictProb(feats); state.PSmoothed = SmoothAlpha * prob + (1 - SmoothAlpha) * state.PSmoothed; if (state.PSmoothed >= ActivationThreshold) state.ConsecutiveHigh++; else state.ConsecutiveHigh = 0; if (state.CooldownTicks > 0) state.CooldownTicks--; if (state.ConsecutiveHigh >= ActivationConsecutiveNeeded && state.CooldownTicks <= 0) ActivateBuff(state, p, prob); }
        private void ActivateBuff(ModelState state, Player p, float prob) { state.ConsecutiveHigh = 0; state.CooldownTicks = CooldownSeconds * 60; int buffId = ModContent.BuffType<PredictorAccessoryBuff>(); p.AddBuff(buffId, BuffDurationSeconds * 60, true); }
        private float[] BuildFeatureVector(Player p) { float[] f = new float[FeatureDim]; double now = Main.GameUpdateCount / 60.0; float dmg05 = _historyStore.DamageTakenBetween(p.whoAmI, now - 0.5, now); float dmg3 = _historyStore.DamageTakenBetween(p.whoAmI, now - 3.0, now); f[0] = Clip01(dmg05 / DamageClipShort); f[1] = Clip01(dmg3 / DamageClipLong); float threatSum = 0f; float maxThreat = 0f; const float maxDist = 1000f; for (int i = 0; i < Main.maxNPCs; i++) { var npc = Main.npc[i]; if (npc == null || !npc.active || !npc.CanBeChasedBy()) continue; float dist = Vector2.Distance(npc.Center, p.Center); if (dist > maxDist) continue; float contrib = (npc.damage / MaxEnemyDamageClip) * (1f / (1f + dist / 20f)); if (contrib > maxThreat) maxThreat = contrib; threatSum += contrib; } for (int i = 0; i < Main.maxProjectiles; i++) { var proj = Main.projectile[i]; if (proj == null || !proj.active || proj.friendly || proj.owner == p.whoAmI || proj.damage <= 0) continue; float dist = Vector2.Distance(proj.Center, p.Center); if (dist > maxDist) continue; float contrib = (proj.damage / MaxEnemyDamageClip) * (1f / (1f + dist / 20f)); if (contrib > maxThreat) maxThreat = contrib; threatSum += contrib; } f[2] = Clip01(threatSum); f[3] = Clip01(maxThreat); f[4] = p.potionDelay > 0 ? 1f : 0f; int healingCount = 0; for (int i = 0; i < 58; i++) { var it = p.inventory[i]; if (it != null && it.stack > 0 && it.healLife > 0) healingCount += it.stack; } f[5] = System.Math.Min(1f, healingCount / 10f); float effDef = p.statDefense - ArmorReductionDebuffValue; f[6] = Clip01(effDef / DefenseClip); f[7] = IsInHazard(p) ? 1f : 0f; double lastHitAgo = _historyStore.SecondsSinceLastDamage(p.whoAmI, now); float inv = (float)(1.0 / (lastHitAgo + 1.0)); f[8] = Clip01(inv); f[9] = 0f; f[10] = 0f; f[11] = 0f; return f; }
        private float Clip01(float x) => x < 0 ? 0 : (x > 1 ? 1 : x);
        private void PersistAllModels() { 
            for (int i = 0; i < Main.maxPlayers; i++) { 
                Player p = Main.player[i]; 
                if (p == null || !p.active) continue; 
                for (int it = 0; it < p.inventory.Length; it++) { 
                    var item = p.inventory[it]; 
                    if (item?.ModItem is PredictorAccessory pa) 
                        if (_models.TryGetValue(pa.AccessoryID, out var state)) 
                            SaveModelStateBackToItem(item, state); 
                    if (item?.ModItem is PredictorAccessoryMk2 pa2) 
                        if (_models.TryGetValue(pa2.AccessoryID, out var state2)) 
                            SaveModelStateBackToItem(item, state2); 
                } 
            } 
        }
        
        private void TryLoadModelFromItem(string accessoryId, Item item) { 
            ModItem modItem = item?.ModItem;
            if (modItem is not PredictorAccessory pa && modItem is not PredictorAccessoryMk2 pa2) return; 
            var state = new ModelState(accessoryId, FeatureDim); 
            if (modItem is PredictorAccessory paStage1) {
                if (paStage1.HasLoadedModel && paStage1.LoadedModelW != null) 
                    state.Model = new LRModel(paStage1.LoadedModelW, paStage1.LoadedModelB); 
                state.Stage = paStage1.Stage; 
                state.PositivesCount = paStage1.LoadedPositivesCount; 
                state.TotalSamples = paStage1.LoadedTotalSamples; 
            } else if (modItem is PredictorAccessoryMk2 paStage2) {
                if (paStage2.HasLoadedModel && paStage2.LoadedModelW != null) 
                    state.Model = new LRModel(paStage2.LoadedModelW, paStage2.LoadedModelB); 
                state.Stage = paStage2.Stage; 
                state.PositivesCount = paStage2.LoadedPositivesCount; 
                state.TotalSamples = paStage2.LoadedTotalSamples; 
            }
            _models[accessoryId] = state; 
        }
        
        public bool TrySerializeModelIntoTag(string accessoryId, TagCompound tag) { if (!_models.TryGetValue(accessoryId, out var state)) return false; if (state.Model != null) { tag["ModelW"] = state.Model.W; tag["ModelB"] = state.Model.B; } tag["Stage"] = state.Stage; tag["PositivesCount"] = state.PositivesCount; tag["TotalSamples"] = state.TotalSamples; var pendingList = new List<TagCompound>(); double now = Main.GameUpdateCount / 60.0; foreach (var s in state.PendingSamples) if (now - s.Timestamp <= LabelHorizonSeconds + 1) pendingList.Add(new TagCompound { ["t"] = (float)(s.Timestamp - now), ["f"] = s.Features }); tag["PendingSamples"] = pendingList; return true; }
        
        private void SaveModelStateBackToItem(Item item, ModelState state) { 
            ModItem modItem = item?.ModItem;
            if (modItem is PredictorAccessory pa) {
                pa.LoadedPositivesCount = state.PositivesCount; 
                pa.LoadedTotalSamples = state.TotalSamples; 
                if (state.Model != null) { 
                    pa.HasLoadedModel = true; 
                    pa.LoadedModelW = state.Model.W; 
                    pa.LoadedModelB = state.Model.B; 
                }
            } else if (modItem is PredictorAccessoryMk2 pa2) {
                pa2.LoadedPositivesCount = state.PositivesCount; 
                pa2.LoadedTotalSamples = state.TotalSamples; 
                if (state.Model != null) { 
                    pa2.HasLoadedModel = true; 
                    pa2.LoadedModelW = state.Model.W; 
                    pa2.LoadedModelB = state.Model.B; 
                }
            }
        }
    }
}