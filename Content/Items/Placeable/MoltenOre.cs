using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Content.Items.Placeable
{
	public class MoltenOre : ModItem
	{
		public override void SetStaticDefaults() {
			Item.ResearchUnlockCount = 100;
			ItemID.Sets.SortingPriorityMaterials[Item.type] = 58;
		}

		public override void SetDefaults() {
			Item.DefaultToPlaceableTile(ModContent.TileType<Tiles.MoltenOre>());
			Item.width = 12;
			Item.height = 12;
            Item.value = Item.buyPrice(0, 0, 30, 0);
        }
	}
}