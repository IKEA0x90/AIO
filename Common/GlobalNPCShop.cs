using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AIO.Common.GlobalNPCs
{
	class GlobalNPCShop : GlobalNPC
	{
		public override void ModifyShop(NPCShop shop) {
			if (shop.NpcType == NPCID.Cyborg) {
				shop.Add<Content.Items.Ammo.AImmo>(Condition.DownedMoonLord);
			}
		}
	}
}
