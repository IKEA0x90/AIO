using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Content.Items.Weapons {
    public class BladeOfTheRuinedKing : ModItem {
        public override void SetDefaults() {
            Item.width = 100; // The item texture's width.
            Item.height = 100; // The item texture's height.

            Item.useStyle = ItemUseStyleID.Swing; // The useStyle of the Item.
            Item.useTime = 16; // The time span of using the weapon. Remember in terraria, 60 frames is a second.
            Item.useAnimation = 16; // The time span of the using animation of the weapon, suggest setting it the same as useTime.
            Item.autoReuse = true; // Whether the weapon can be used more than once automatically by holding the use button.

            Item.DamageType = DamageClass.Melee; // Whether your item is part of the melee class.
            Item.damage = 70; // The damage your item deals.
            Item.knockBack = 3; // The force of knockback of the weapon. Maximum is 20
            Item.crit = -4; // The critical strike chance the weapon has. The player, by default, has a 4% critical strike chance.

            Item.value = Item.buyPrice(gold: 25); // The value of the weapon in copper coins.
            Item.rare = ItemRarityID.Yellow;
            Item.UseSound = SoundID.Item1; // The sound when the weapon is being used.

            //Item.scale = 2f;
        }

        public override void MeleeEffects(Player player, Rectangle hitbox) {
            if (Main.rand.NextBool(10)) {
                // Emit dusts when the sword is swung
                Dust.NewDust(new Vector2(hitbox.X, hitbox.Y), hitbox.Width, hitbox.Height, DustID.Smoke, 0, 0, 100, Color.Turquoise, 1.5f);
            }
        }

        public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damageDone) {
            int heal = (int)(damageDone * 0.1);
            int realHeal = Math.Clamp(heal, 1, heal);
            player.Heal(heal); 
        }
    }
}