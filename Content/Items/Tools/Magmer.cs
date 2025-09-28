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