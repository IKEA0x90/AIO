using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using AIO.Content.Systems;

namespace AIO.Content.Items {
    // An item that toggles a wall tile to be drawn in the foreground when used on it.
    public class ForegroundPaint : ModItem {
        public override void SetStaticDefaults() {
            Item.ResearchUnlockCount = 1;
        }

        public override void SetDefaults() {
            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 6;
            Item.useAnimation = 12;
            Item.autoReuse = true;
            Item.width = 20;
            Item.height = 20;
            Item.rare = ItemRarityID.Blue;
            Item.value = Item.buyPrice(silver: 50);
            Item.useTurn = true;
            Item.noMelee = true; // not a weapon
            Item.UseSound = SoundID.Item19; // paint sound
        }

        public override bool? UseItem(Player player) {
            // Determine the tile under the mouse
            Point tilePos = Main.MouseWorld.ToTileCoordinates();
            int i = tilePos.X;
            int j = tilePos.Y;

            if (!WorldGen.InWorld(i, j))
                return false;

            Tile tile = Framing.GetTileSafely(i, j);
            if (tile.WallType <= 0)
                return false;

            // Left click applies/toggles on, right click removes
            bool remove = Main.mouseRight || player.altFunctionUse == 2;
            bool makeForeground = !remove;

            // If already foreground and left clicking, do nothing
            bool currentlyForeground = ForegroundWallPaintSystem.IsForeground(i, j);
            if (remove) {
                if (currentlyForeground)
                    ForegroundWallPaintSystem.SendPaintChange(i, j, false);
            } else {
                if (!currentlyForeground)
                    ForegroundWallPaintSystem.SendPaintChange(i, j, true);
            }

            return true;
        }

        public override bool AltFunctionUse(Player player) => true; // enable right click

        public override void HoldItem(Player player) {
            // Simple highlight on the tile under cursor when holding the item
            Point tilePos = Main.MouseWorld.ToTileCoordinates();
            if (WorldGen.InWorld(tilePos.X, tilePos.Y) && ForegroundWallPaintSystem.IsForeground(tilePos.X, tilePos.Y)) {
                Lighting.AddLight(tilePos.X, tilePos.Y, 0.2f, 0.8f, 0.8f);
            }
        }

        public override void PostDrawInInventory(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Microsoft.Xna.Framework.Vector2 position, Microsoft.Xna.Framework.Rectangle frame, Microsoft.Xna.Framework.Color drawColor, Microsoft.Xna.Framework.Color itemColor, Microsoft.Xna.Framework.Vector2 origin, float scale) {
            // Optionally draw a tint to hint at special behavior (kept minimal)
        }

        public override void AddRecipes() {
            CreateRecipe()
                .AddIngredient(ItemID.Bottle)
                .AddIngredient(ItemID.Glass, 5)
                .AddTile(TileID.Bottles)
                .Register();
        }
    }
}
