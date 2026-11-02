using Raylib_cs;

namespace Spacevors.Game;

// Base colour + one packed surface map (RGB = tangent-space normal, A = depth) of one lit sprite
// variant. A lit fragment reads the normal at one UV and the depth at two, so folding the depth map
// into the normal map's unused alpha is one sampler and one texture fetch fewer per fragment.
// LitGroupRenderer uses this as a dictionary key: record equality compares the two texture ids,
// and an id identifies one loaded texture, so equal keys really are the same variant.
public sealed record LitSprite(Texture2D Base, Texture2D NormalDepth);
