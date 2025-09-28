using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Content.Items.Tools {
    public class Magmer : ModItem {
        public override void SetDefaults() {
            Item.CloneDefaults(ItemID.SpectreHamaxe);
    
            Item.damage = 30;
            Item.DamageType = DamageClass.Melee;
            Item.width = 62;
            Item.height = 56;
            Item.useTime = 13;
            Item.useAnimation = 1;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 10;
            Item.value = Item.sellPrice(0, 4, 50, 0);
            Item.rare = ItemRarityID.Orange;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;

            Item.axe = 0;
            Item.hammer = 150; // How much hammer power the weapon has
            Item.attackSpeedOnlyAffectsWeaponAnimation = false; // Melee speed affects how fast the tool swings for damage purposes, but not how fast it can dig
        }

        public override bool? UseItem(Player player) {
            // Get the tile position the player is targeting
            int tileX = Player.tileTargetX;
            int tileY = Player.tileTargetY;
            
            // Check if we're targeting a wall that exists
            if (Main.tile[tileX, tileY].WallType > 0) {
                // Break unsafe walls in a 5x5 area centered on the target
                BreakUnsafeWallsInArea(tileX, tileY, 5);
            }
            
            return base.UseItem(player);
        }

        private void BreakUnsafeWallsInArea(int centerX, int centerY, int size) {
            int radius = size / 2; // For 5x5, radius is 2
            
            for (int x = centerX - radius; x <= centerX + radius; x++) {
                for (int y = centerY - radius; y <= centerY + radius; y++) {
                    // Check bounds
                    if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) {
                        continue;
                    }
                    
                    Tile tile = Main.tile[x, y];
                    
                    // If there's a wall and it's unsafe (not a house wall)
                    if (tile.WallType > 0 && !Main.wallHouse[tile.WallType]) {
                        // Remove the wall
                        tile.WallType = 0;
                        
                        // Sync the change in multiplayer
                        if (Main.netMode != NetmodeID.SinglePlayer) {
                            NetMessage.SendTileSquare(-1, x, y, 1);
                        }
                        
                        // Create dust effect for visual feedback
                        if (Main.rand.NextBool(3)) {
                            Dust.NewDust(new Vector2(x * 16, y * 16), 16, 16, DustID.Smoke, 0, 0);
                        }
                    }
                }
            }
        }

        public override void MeleeEffects(Player player, Rectangle hitbox) {
            if (Main.rand.NextBool(10)) {
                Dust.NewDust(new Vector2(hitbox.X, hitbox.Y), hitbox.Width, hitbox.Height, DustID.Lava, 0, 0);
            }
        }
        
        public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damageDone) {
            target.AddBuff(BuffID.OnFire3, 3);
        }
    }
}