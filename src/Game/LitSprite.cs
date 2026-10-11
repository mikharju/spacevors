using Raylib_cs;

namespace Spacevors.Game;

// Base + normal + depth maps of one lit sprite variant.
// LitGroupRenderer uses this as a dictionary key: record equality compares the three texture ids,
// and an id identifies one loaded texture, so equal keys really are the same variant.
public sealed record LitSprite(Texture2D Base, Texture2D Normals, Texture2D Depth);
