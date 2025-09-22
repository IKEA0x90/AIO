using AIO.Common;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AIO.Content {
    // This class contains thoughtful examples of item recipe creation.
    // Recipes are explained in detail on the https://github.com/tModLoader/tModLoader/wiki/Basic-Recipes and https://github.com/tModLoader/tModLoader/wiki/Intermediate-Recipes wiki pages. Please visit the wiki to learn more about recipes if anything is unclear.
    public class ExampleRecipes : ModSystem {

        public override void AddRecipes() {

            Recipe recipe = Recipe.Create(ItemID.Bubble, 25);
            recipe.AddIngredient(ItemID.BottledWater, 10);
            recipe.AddIngredient(ItemID.BubbleWand, 1);
            recipe.AddTile(TileID.BubbleMachine);
            recipe.AddConsumeIngredientCallback(RecipeCallbacks.DontConsumeBaloon);
            recipe.Register();

            var resultItem = ModContent.GetInstance<Items.Placeable.ForegroundGlassWall>();
            resultItem.CreateRecipe(25).
                AddIngredient(ItemID.GlassWall, 25).
                AddTile(TileID.GlassKiln);

            Recipe recipe1 = Recipe.Create(ItemID.GlassWall, 25);
            recipe1.AddIngredient<Items.Placeable.ForegroundGlassWall>(25);
            recipe1.AddTile(TileID.GlassKiln);
            recipe1.Register();
        }
    }
}