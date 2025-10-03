using Terraria;
using Terraria.ModLoader;

namespace AIO.Common.Players {
    /// <summary>
    /// Holds transient info display data for PredictorAccessory.
    /// </summary>
    public class InfoDisplayPlayer : ModPlayer {
        // Predictor accessory runtime display fields (updated server-side; clients may see stale)
        public float predictorProb;
        public int predictorStage;
        public int predictorPositives;
        public int predictorTotalSamples;
        public int predictorCooldownTicks;

        public override void ResetInfoAccessories() {
            // Values will be repopulated each cycle when accessory is equipped
            // (Do not zero predictorStage so tooltips can show last known state if desired; keep for clarity)
        }

        public override void RefreshInfoAccessoriesFromTeamPlayers(Player otherPlayer) {
            // Could merge team accessory data if design requires (not used currently).
        }
    }
}