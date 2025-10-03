using AIO.Content.Items.Accessories;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Common {
    public class PredictorUpgradeRecipe : ModSystem {
        public override void AddRecipes() {
            // Create the upgrade recipe that preserves model data
            Recipe recipe = Recipe.Create(ModContent.ItemType<PredictorAccessoryMk2>());
            recipe.AddIngredient<PredictorAccessory>();
            recipe.AddIngredient(ItemID.Diamond, 1);
            recipe.AddTile(TileID.TinkerersWorkbench);
            recipe.AddOnCraftCallback((Recipe recipe, Item item, List<Item> consumedItems, Item destinationStack) => {
                TransferModelData(recipe, item, consumedItems, destinationStack);
            });
            recipe.Register();
        }

        private static void TransferModelData(Recipe recipe, Item item, List<Item> consumedItems, Item destinationStack) {
            // Find the PredictorAccessory in consumed items
            PredictorAccessory sourceAccessory = null;
            foreach (var consumedItem in consumedItems) {
                if (consumedItem.ModItem is PredictorAccessory pa) {
                    sourceAccessory = pa;
                    break;
                }
            }

            if (sourceAccessory != null && item.ModItem is PredictorAccessoryMk2 targetAccessory) {
                // Transfer all model data from Stage 1 to Stage 2
                targetAccessory.AccessoryID = sourceAccessory.AccessoryID;
                targetAccessory.HasLoadedModel = sourceAccessory.HasLoadedModel;
                targetAccessory.LoadedModelW = sourceAccessory.LoadedModelW;
                targetAccessory.LoadedModelB = sourceAccessory.LoadedModelB;
                targetAccessory.LoadedPositivesCount = sourceAccessory.LoadedPositivesCount;
                targetAccessory.LoadedTotalSamples = sourceAccessory.LoadedTotalSamples;
                targetAccessory.Stage = 2;

                // If there's a model manager instance, update it
                if (Main.netMode != NetmodeID.MultiplayerClient) {
                    var mgr = AccessoryModelManager.Instance;
                    if (mgr != null && !string.IsNullOrWhiteSpace(targetAccessory.AccessoryID)) {
                        mgr.UpgradeToStage2Server(targetAccessory.AccessoryID, item);
                    }
                }
            }
        }
    }
}