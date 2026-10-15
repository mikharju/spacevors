using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

// The slot-picking step of a level-up: after a new-weapon card is chosen, the hull is drawn large
// and the player picks which mount gets the weapon (or which mounted weapon is replaced).
// Every mount shows its firing arc, so the choice is about coverage, not guesswork.
public static class MountMenuRenderer
{
    // The hull preview is drawn at this fraction of the window height, capped so a tall window
    // does not turn a 46 px scout into a billboard.
    const float ShipHeightFraction = 0.42f;
    const float MaxShipDiameter = 520f;

    // A mount's arc is drawn this many times its marker radius — long enough to read the sector.
    const float ArcRadiusFactor = 5f;
    const int ArcSegments = 24;
    const int NumberFontSize = 16;
    const float ClickSlack = 8f; // a click this far outside a marker still hits it

    public readonly record struct SlotMarker(
        Entity SlotEntity,
        TurretSlotDefinition Slot,
        bool Occupied,
        string WeaponName,
        // Where the gun currently points (absolute), so the triangle matches the in-game marker.
        float AimAngle,
        float ScreenX,
        float ScreenY,
        float MarkerRadius,
        bool Selectable,
        int Number); // 1-based key/hint number, 0 when not selectable

    public sealed record Prepared(ShipType Ship, float ShipX, float ShipY, float ShipDiameter, float Scale, SlotMarker[] Markers, string Hint);

    public static Prepared Prepare(EntityManager em, ShipType ship, int windowWidth, int windowHeight, WeaponType pendingWeapon, bool fillingFreeSlot)
    {
        float scale = StatsScreenRenderer.UiScale(windowWidth);
        float diameter = MathF.Min(windowHeight * ShipHeightFraction, MaxShipDiameter) * scale;
        float shipX = windowWidth / 2f;
        float shipY = windowHeight * 0.5f;
        float zoom = diameter / (ship.Radius * 2f);

        // Slots are laid out on the unrotated hull: forward is up, +x is the ship's right.
        var markers = new List<SlotMarker>();
        int number = 0;

        foreach (var (slotEntity, slot) in em.GetEntitiesWithComponents<TurretSlot>())
        {
            bool occupied = em.HasComponent<Turret>(slotEntity);
            bool selectable = fillingFreeSlot ? !occupied && TurretMount.Fits(slot.Definition, pendingWeapon) : occupied;
            if (selectable) number++;

            markers.Add(new SlotMarker(
                slotEntity,
                slot.Definition,
                occupied,
                occupied ? em.GetComponent<Turret>(slotEntity).WeaponName : "",
                occupied ? em.GetComponent<Rotation>(slotEntity).Angle : slot.Definition.ArcCenter,
                shipX + slot.Definition.Offset.X * zoom,
                shipY + slot.Definition.Offset.Y * zoom,
                RenderHelpers.TurretMarkerRadius(slot.Definition.Size) * zoom,
                selectable,
                selectable ? number : 0));
        }

        string hint = fillingFreeSlot
            ? $"Click a free mount, or press its number, to fit {pendingWeapon.Name} — right-click to go back"
            : $"No free mount fits {pendingWeapon.Name}: click a mounted weapon to replace it — right-click to go back";

        return new Prepared(ship, shipX, shipY, diameter, scale, [.. markers], hint);
    }

    public static int GetHoveredIndex(Prepared prepared, int mouseX, int mouseY)
    {
        var mouse = new Vector2(mouseX, mouseY);

        for (int i = 0; i < prepared.Markers.Length; i++)
        {
            var marker = prepared.Markers[i];
            if (!marker.Selectable) continue;

            var delta = mouse - new Vector2(marker.ScreenX, marker.ScreenY);
            float reach = marker.MarkerRadius + ClickSlack;
            if (Vector2.Dot(delta, delta) <= reach * reach) return i;
        }

        return -1;
    }

    public static void Draw(Prepared prepared, int hoveredIndex, int windowWidth, int windowHeight)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, Palette.ScreenDim);

        ShipSpriteRenderer.DrawShipSprite(prepared.Ship, prepared.ShipX, prepared.ShipY, prepared.ShipDiameter, 0f);

        Entity? hoveredSlot = hoveredIndex >= 0 ? prepared.Markers[hoveredIndex].SlotEntity : null;

        foreach (var marker in prepared.Markers)
        {
            bool hovered = marker.Selectable && marker.SlotEntity == hoveredSlot;
            DrawArc(marker, hovered);
            DrawMarker(marker, hovered);
            if (marker.Number > 0) DrawNumberBadge(marker, prepared.Scale);
        }

        int font = (int)(UpgradeMenuRenderer.HintFontSize * prepared.Scale);
        Raylib.DrawText(prepared.Hint, windowWidth / 2 - Raylib.MeasureText(prepared.Hint, font) / 2,
            UpgradeMenuRenderer.GetHintY(windowHeight, prepared.Scale), font, Palette.TextMuted);
    }

    // Firing arc: a green sector around the mount's centre direction. Screen degrees put 0 to the
    // right, and the hull faces up, so the arc centre sits 90° counter-clockwise from that.
    static void DrawArc(SlotMarker marker, bool hovered)
    {
        float centerDeg = Angle.ToDeg(marker.Slot.ArcCenter) - 90f;
        float halfSpanDeg = Angle.ToDeg(marker.Slot.ArcAngle) / 2f;
        Color fill = hovered ? Palette.TurretArcHover : Palette.TurretArc;

        Raylib.DrawCircleSector(
            new System.Numerics.Vector2(marker.ScreenX, marker.ScreenY),
            marker.MarkerRadius * ArcRadiusFactor,
            centerDeg - halfSpanDeg,
            centerDeg + halfSpanDeg,
            ArcSegments,
            fill);
    }

    // Empty mounts are circles, mounted ones a triangle pointing where the gun currently aims.
    static void DrawMarker(SlotMarker marker, bool hovered)
    {
        var center = new System.Numerics.Vector2(marker.ScreenX, marker.ScreenY);

        if (!marker.Occupied)
        {
            Raylib.DrawCircle((int)center.X, (int)center.Y, marker.MarkerRadius, Palette.TurretEmptySlot);
            if (hovered) Raylib.DrawCircleLines((int)center.X, (int)center.Y, marker.MarkerRadius + ClickSlack, Palette.Accent);
            return;
        }

        float aimDeg = Angle.ToDeg(marker.AimAngle);
        RenderHelpers.DrawTurretMarker(center.X, center.Y, marker.MarkerRadius * 1.6f, aimDeg, hovered ? Palette.Accent : Palette.TurretMounted);
    }

    static void DrawNumberBadge(SlotMarker marker, float scale)
    {
        int font = (int)(NumberFontSize * scale);
        string label = marker.Number.ToString();
        Raylib.DrawText(label, (int)(marker.ScreenX + marker.MarkerRadius + 2f), (int)(marker.ScreenY - font / 2f), font, Palette.TextBright);
    }
}
