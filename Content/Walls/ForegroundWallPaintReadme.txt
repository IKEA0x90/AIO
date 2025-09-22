This mod feature adds a Foreground Paint item that marks any wall tile as foreground and draws it in a front UI layer.
- Uses a GlobalWall to skip vanilla draw for marked tiles.
- Draws in a front layer with lighting and proper framing.
- Ensures textures, vanilla or modded, are loaded via Main.instance.LoadWall(type) before using TextureAssets.Wall[type].
- World save/load, multiplayer sync included.
