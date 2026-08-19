using Raylib_cs;

namespace Spacevors.Game;

// One asteroid variant's graphics: lit (normal + depth maps) when available, otherwise a flat texture.
public sealed record AsteroidSprite(LitSprite? Lit, Texture2D? Flat);
