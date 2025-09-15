using Microsoft.Build.Utilities;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace AIO.Content.Tiles {
    public class MoltenOre : ModTile {
        public override void SetStaticDefaults() {
            TileID.Sets.Ore[Type] = true;
            TileID.Sets.FriendlyFairyCanLureTo[Type] = true;
            Main.tileSpelunker[Type] = true; // The tile will be affected by spelunker highlighting
            Main.tileOreFinderPriority[Type] = 660; // Metal Detector value, see https://terraria.wiki.gg/wiki/Metal_Detector
            Main.tileShine2[Type] = true; // Modifies the draw color slightly.
            Main.tileShine[Type] = 300; // How often tiny dust appear off this tile. Larger is less frequently
            Main.tileMergeDirt[Type] = true;
            Main.tileSolid[Type] = true;
            Main.tileBlockLight[Type] = true;

            LocalizedText name = CreateMapEntryName();
            AddMapEntry(new Color(222, 56, 32), name);

            DustType = DustID.Palladium;
            VanillaFallbackOnModDeletion = TileID.Palladium;
            HitSound = SoundID.Tink;

            MinPick = 200;
            Main.rare = ItemRarityID.Yellow;
        }
    }

    // ExampleOreSystem contains code related to spawning ExampleOre. It contains both spawning ore during world generation, seen in ModifyWorldGenTasks, and spawning ore after defeating a boss, seen in BlessWorldWithExampleOre and MinionBossBody.OnKill.
    public class MoltenOreSystem : ModSystem {
        public static LocalizedText MoltenOrePassMessage { get; private set; }

        public override void SetStaticDefaults() {
            MoltenOrePassMessage = Mod.GetLocalization($"WorldGen.{nameof(MoltenOrePassMessage)}");
        }

        // World generation is explained more in https://github.com/tModLoader/tModLoader/wiki/World-Generation
        public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight) {
            // Because world generation is like layering several images on top of each other, we need to do some steps between the original world generation steps.

            // Most vanilla ores are generated in a step called "Shinies", so for maximum compatibility, we will also do this.
            // First, we find out which step "Shinies" is.
            int ShiniesIndex = tasks.FindIndex(genpass => genpass.Name.Equals("Shinies"));

            if (ShiniesIndex != -1) {
                // Next, we insert our pass directly after the original "Shinies" pass.
                // ExampleOrePass is a class seen bellow
                tasks.Insert(ShiniesIndex + 1, new MoltenOrePass("MoltenOres", 237.4298f));
            }
        }
    }

    public class MoltenOrePass : GenPass {
        public MoltenOrePass(string name, float loadWeight) : base(name, loadWeight) {
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration) {
            // progress.Message is the message shown to the user while the following code is running.
            // Try to make your message clear. You can be a little bit clever, but make sure it is descriptive enough for troubleshooting purposes.
            progress.Message = MoltenOreSystem.MoltenOrePassMessage.Value;

            // Ores are quite simple, we simply use a for loop and the WorldGen.TileRunner to place splotches of the specified Tile in the world.
            // "6E-05" is "scientific notation". It simply means 0.00006 but in some ways is easier to read.
            int k = 0;
            while (k < (int)(Main.maxTilesX * Main.maxTilesY * 0.0001 * 0.03)) {
                // The inside of this for loop corresponds to one single splotch of our Ore.
                // First, we randomly choose any coordinate in the world by choosing a random x and y value.
                int x = WorldGen.genRand.Next(0, Main.maxTilesX);

                // WorldGen.worldSurfaceLow is actually the highest surface tile. In practice you might want to use WorldGen.rockLayer or other WorldGen values.
                int y = WorldGen.genRand.Next((int)GenVars.rockLayer, Main.maxTilesY);

                // Then, we call WorldGen.TileRunner with random "strength" and random "steps", as well as the Tile we wish to place.
                // Feel free to experiment with strength and step to see the shape they generate.
                // WorldGen.TileRunner(x, y, WorldGen.genRand.Next(1, 3), WorldGen.genRand.Next(5, 20), ModContent.TileType<MoltenOre>());

                // Alternately, we could check the tile already present in the coordinate we are interested.
                // Wrapping WorldGen.TileRunner in the following condition would make the ore only generate in Snow
                
                Tile tile = Framing.GetTileSafely(x, y);
                if (tile.HasTile && (tile.TileType == TileID.Sand || tile.TileType == TileID.Sandstone)) {
                    WorldGen.TileRunner(x, y, WorldGen.genRand.Next(10, 15), WorldGen.genRand.Next(2, 4), ModContent.TileType<MoltenOre>(), false, 0, 0, false, true);
                    k++;
                }
                /**
                else if (tile.HasTile) {
                    WorldGen.TileRunner(x, y, WorldGen.genRand.Next(5, 11), WorldGen.genRand.Next(1, 4), TileID.SnowBlock, false, 0, 0, false, true);
                    WorldGen.TileRunner(x, y, WorldGen.genRand.Next(1, 3), WorldGen.genRand.Next(5, 20), ModContent.TileType<MoltenOre>(), false, 0, 0, false, true);
                    k++;
                }
                */
            }
        }
    }
}