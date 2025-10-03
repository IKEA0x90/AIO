using Microsoft.Build.Evaluation;
using Terraria;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Content.Items.Ammo {
    public class AImmo : ModItem {
        public override void SetDefaults() {
            Item.damage = 12;
            Item.DamageType = DamageClass.Ranged;
            Item.width = 8;
            Item.height = 8;
            Item.maxStack = 9999;
            Item.consumable = true;
            Item.knockBack = 2f;
            Item.value = Item.sellPrice(0, 0, 0, 50);
            Item.rare = ItemRarityID.Green;
            Item.shoot = ModContent.ProjectileType<Projectiles.AImmoProjectile>();
            Item.shootSpeed = 4f;
            Item.ammo = AmmoID.Bullet;
            Item.SetShopValues(ItemRarityColor.Green2, Item.buyPrice(silver: 1));
        }
    }
}
