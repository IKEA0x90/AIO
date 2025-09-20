using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Content.Items.Placeable
{
	public class AIOre : ModItem
	{
		public override void SetStaticDefaults() {
			Item.ResearchUnlockCount = 100;
			ItemID.Sets.SortingPriorityMaterials[Item.type] = 65;
		}

		public override void SetDefaults() {
			Item.DefaultToPlaceableTile(ModContent.TileType<Tiles.AIOre>());
			Item.width = 12;
			Item.height = 12;
            Item.value = Item.buyPrice(0, 0, 50, 0);
		}
	}
}