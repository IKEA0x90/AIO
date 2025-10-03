using Terraria;
using Terraria.ModLoader;

namespace AIO.Content.Buffs {
    /// <summary>
    /// Survival buff applied when model predicts high death probability.
    /// Provides reasonable defensive bonuses instead of complete invulnerability.
    /// TODO: Adjust actual effects (e.g., damage reduction, defense boost, regen).
    /// </summary>
    public class PredictorAccessoryBuff : ModBuff {
        public override void SetStaticDefaults() {
            Main.buffNoTimeDisplay[Type] = false;
            Main.debuff[Type] = false;
        }

        public override void Update(Player player, ref int buffIndex) {
            player.lifeRegen += 20; // Strong health regeneration
            player.endurance += 0.25f;
            player.statDefense += 20; // Extra defense
            player.moveSpeed += 0.15f; // Movement speed boost to help escape
        }
    }
}