using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AIO.Content.Items.Accessories {
    public class PredictorAccessory : ModItem {
        public const int CurrentFeatureVersion = 1;
        public string AccessoryID;
        public int Stage = 1;
        public bool HasLoadedModel;
        public float[] LoadedModelW;
        public float LoadedModelB;
        public int LoadedPositivesCount;
        public int LoadedTotalSamples;

        public override void SetDefaults() {
            Item.width = 28;
            Item.height = 28;
            Item.accessory = true;
            Item.rare = ItemRarityID.LightRed;
            Item.value = Item.sellPrice(gold: 5);
        }

        public override bool CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player) {
            // Prevent wearing both PredictorAccessory and PredictorAccessoryMk2 together
            if (equippedItem.ModItem is PredictorAccessoryMk2 || incomingItem.ModItem is PredictorAccessoryMk2) {
                return false;
            }
            return true;
        }

        public override void SaveData(TagCompound tag) {
            if (string.IsNullOrWhiteSpace(AccessoryID)) {
                AccessoryID = Guid.NewGuid().ToString("N");
            }
            tag["AccessoryID"] = AccessoryID;
            tag["FeatureVersion"] = CurrentFeatureVersion;
            tag["Stage"] = Stage;
            tag["PositivesCount"] = LoadedPositivesCount;
            tag["TotalSamples"] = LoadedTotalSamples;
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                var mgr = ModContent.GetInstance<Common.AccessoryModelManager>();
                mgr.TrySerializeModelIntoTag(AccessoryID, tag);
            } else if (HasLoadedModel) {
                tag["ModelW"] = LoadedModelW;
                tag["ModelB"] = LoadedModelB;
            }
        }

        public override void LoadData(TagCompound tag) {
            AccessoryID = tag.GetString("AccessoryID");
            Stage = tag.ContainsKey("Stage") ? tag.GetInt("Stage") : 1;
            LoadedPositivesCount = tag.ContainsKey("PositivesCount") ? tag.GetInt("PositivesCount") : 0;
            LoadedTotalSamples = tag.ContainsKey("TotalSamples") ? tag.GetInt("TotalSamples") : 0;
            if (tag.ContainsKey("ModelW")) {
                LoadedModelW = tag.Get<float[]>("ModelW");
                LoadedModelB = tag.GetFloat("ModelB");
                HasLoadedModel = true;
            } else {
                HasLoadedModel = false;
            }
        }

        public override void UpdateAccessory(Player player, bool hideVisual) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                if (string.IsNullOrWhiteSpace(AccessoryID)) {
                    AccessoryID = Guid.NewGuid().ToString("N");
                }
                Common.AccessoryModelManager.MarkEquippedServer(player, this);
            }
            var info = player.GetModPlayer<Common.Players.InfoDisplayPlayer>();
            info.predictorStage = Stage;
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips) {
            if (Stage == 1) {
                tooltips.Add(new TooltipLine(Mod, "Hint", "Learns to predict imminent death -> Craft with Diamond at Tinkerer's Workshop to activate"));
            } else {
                tooltips.Add(new TooltipLine(Mod, "Hint", "Provides protective buffs when danger is detected"));
            }
            
            var player = Main.LocalPlayer;
            if (player != null) {
                var info = player.GetModPlayer<Common.Players.InfoDisplayPlayer>();
                string stageStr = Stage == 1 ? "Training" : "Active";
                tooltips.Add(new TooltipLine(Mod, "Stage", $"Stage: {stageStr}"));
                if (Stage == 1) {
                    tooltips.Add(new TooltipLine(Mod, "TrainingStats", $"Samples: {info.predictorTotalSamples}, Positives: {info.predictorPositives}"));
                } else {
                    if (info.predictorProb > 0f)
                        tooltips.Add(new TooltipLine(Mod, "CurrentProb", $"p(danger)= {info.predictorProb:F2}"));
                    tooltips.Add(new TooltipLine(Mod, "Cooldown", $"Cooldown: {info.predictorCooldownTicks / 60f:F1}s"));
                }
            }
        }

        /*
        public override void AddRecipes() {
            // Original recipe for Stage 1
            CreateRecipe()
                .AddIngredient(ItemID.SoulofLight, 5)
                .AddIngredient(ItemID.SoulofNight, 5)
                .AddIngredient(ItemID.LifeCrystal, 1)
                .AddIngredient(ItemID.MeteoriteBar, 10)
                .AddTile(TileID.TinkerersWorkbench)
                .Register();
        }
        */
    }
}