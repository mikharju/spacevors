using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

// The turret-mount screen: the hull drawn large, every mount with its firing arc, and the cargo of
// inactive weapons beside it. One screen serves two entries — placing a new weapon from a level-up
// card, and rearranging guns from the stats screen. Whatever is in hand (the "held" weapon) is what a
// mount click places; a filled mount swaps, pushing the displaced gun into cargo and into hand.
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

    // Cargo list: a column at the left edge, starting well below the top so it never fights the hull.
    const float CargoMarginX = 40f;
    const float CargoTopFraction = 0.17f;
    const int CargoFontSize = 16;
    const int CargoRowPitch = 26;
    const int CargoRowPaddingX = 10;
    const int CargoRowPaddingY = 5;

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
        // The mount can take the weapon in hand: it gets a green ring, so the player sees at a glance
        // which mounts are candidates rather than hunting with the mouse.
        bool AcceptsHeld,
        // 1-based key/hint number; every slot is numbered, so the key for a mount never changes.
        int Number);

    // One cargo weapon, drawn as a clickable row.
    public readonly record struct CargoItem(
        string WeaponName,
        float ScreenX,
        float ScreenY,
        float Width,
        float Height,
        bool Held);

    public sealed record Prepared(
        ShipType Ship,
        float ShipX,
        float ShipY,
        float ShipDiameter,
        float Scale,
        SlotMarker[] Markers,
        CargoItem[] Cargo,
        float CargoTop,
        string HeldWeapon,
        string Hint);

    public static Prepared Prepare(EntityManager em, ShipType ship, int windowWidth, int windowHeight, string heldWeapon, IReadOnlyList<string> cargoNames, string hint)
    {
        float scale = StatsScreenRenderer.UiScale(windowWidth);
        float diameter = MathF.Min(windowHeight * ShipHeightFraction, MaxShipDiameter) * scale;
        float shipX = windowWidth / 2f;
        float shipY = windowHeight * 0.5f;
        float zoom = diameter / (ship.Radius * 2f);

        // With a weapon in hand every mount that fits is a place to put it; with nothing in hand the
        // clickable things are the mounted guns, which is how a gun gets taken down into cargo.
        WeaponType? held = WeaponType.FromName(heldWeapon);

        // Slots are laid out on the unrotated hull: forward is up, +x is the ship's right.
        var markers = new List<SlotMarker>();
        int number = 0;

        foreach (var (slotEntity, slot) in em.GetEntitiesWithComponents<TurretSlot>())
        {
            bool occupied = em.HasComponent<Turret>(slotEntity);
            bool acceptsHeld = held is { } weapon && TurretMount.Fits(slot.Definition, weapon);
            bool selectable = acceptsHeld || (held is null && occupied);
            number++;

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
                acceptsHeld,
                number));
        }

        var cargo = new List<CargoItem>();
        int cargoX = (int)(CargoMarginX * scale);
        int rowFont = (int)(CargoFontSize * scale);
        int rowPitch = (int)(CargoRowPitch * scale);
        int top = (int)(windowHeight * CargoTopFraction);
        int cargoY = top + rowPitch; // one row down, leaving room for the header

        foreach (string name in cargoNames)
        {
            float width = Raylib.MeasureText(name, rowFont) + 2f * CargoRowPaddingX * scale;
            cargo.Add(new CargoItem(name, cargoX, cargoY, width, rowPitch, name == heldWeapon));
            cargoY += rowPitch;
        }

        return new Prepared(ship, shipX, shipY, diameter, scale, [.. markers], [.. cargo], top, heldWeapon, hint);
    }

    public static int GetHoveredMountIndex(Prepared prepared, int mouseX, int mouseY)
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

    public static int GetHoveredCargoIndex(Prepared prepared, int mouseX, int mouseY)
    {
        for (int i = 0; i < prepared.Cargo.Length; i++)
        {
            var item = prepared.Cargo[i];
            if (mouseX >= item.ScreenX && mouseX <= item.ScreenX + item.Width &&
                mouseY >= item.ScreenY && mouseY <= item.ScreenY + item.Height)
                return i;
        }

        return -1;
    }

    public static void Draw(Prepared prepared, int hoveredMount, int hoveredCargo, int windowWidth, int windowHeight)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, Palette.ScreenDim);

        ShipSpriteRenderer.DrawShipSprite(prepared.Ship, prepared.ShipX, prepared.ShipY, prepared.ShipDiameter, 0f);

        Entity? hoveredSlot = hoveredMount >= 0 ? prepared.Markers[hoveredMount].SlotEntity : null;

        foreach (var marker in prepared.Markers)
        {
            bool hovered = marker.Selectable && marker.SlotEntity == hoveredSlot;
            DrawArc(marker, hovered);
            if (marker.AcceptsHeld) DrawFitRing(marker, hovered, prepared.Scale);
            DrawMarker(marker, hovered);
            DrawNumberBadge(marker, prepared.Scale);
        }

        DrawCargo(prepared, hoveredCargo);

        int font = (int)(UpgradeMenuRenderer.HintFontSize * prepared.Scale);
        Raylib.DrawText(prepared.Hint, windowWidth / 2 - Raylib.MeasureText(prepared.Hint, font) / 2,
            UpgradeMenuRenderer.GetHintY(windowHeight, prepared.Scale), font, Palette.TextMuted);
    }

    // Cargo column: inactive weapons, each a click away from being mounted. The one in hand is framed.
    static void DrawCargo(Prepared prepared, int hoveredCargo)
    {
        int headerFont = (int)(CargoFontSize * prepared.Scale);
        int rowPitch = (int)(CargoRowPitch * prepared.Scale);
        int x = (int)(CargoMarginX * prepared.Scale);

        Raylib.DrawText("CARGO", x, (int)prepared.CargoTop, headerFont, Palette.Accent);

        if (prepared.Cargo.Length == 0)
        {
            Raylib.DrawText("(empty)", x, (int)prepared.CargoTop + rowPitch, headerFont, Palette.TextMuted);
            return;
        }

        for (int i = 0; i < prepared.Cargo.Length; i++)
        {
            var item = prepared.Cargo[i];
            if (item.Held || i == hoveredCargo)
            {
                Ui.DrawRoundedRect((int)item.ScreenX, (int)item.ScreenY, (int)item.Width, rowPitch - 3, 3,
                    item.Held ? Palette.PanelFill : Palette.RowHover);
            }

            Color text = item.Held ? Palette.Accent : i == hoveredCargo ? Palette.TextBright : Palette.TextMuted;
            Raylib.DrawText(item.WeaponName, (int)(item.ScreenX + CargoRowPaddingX * prepared.Scale), (int)item.ScreenY, headerFont, text);
        }
    }

    // Green ring around a mount that can take the weapon in hand. Hovering doubles the ring, so the
    // hovered candidate still stands out from the rest.
    static void DrawFitRing(SlotMarker marker, bool hovered, float scale)
    {
        float reach = marker.MarkerRadius + ClickSlack * 0.6f;
        Raylib.DrawCircleLines((int)marker.ScreenX, (int)marker.ScreenY, reach, Palette.TurretFits);
        if (hovered) Raylib.DrawCircleLines((int)marker.ScreenX, (int)marker.ScreenY, reach + 3f * scale, Palette.TurretFits);
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

    // Empty mounts are circles; a mounted one is the same circle with a point toward where the gun aims.
    static void DrawMarker(SlotMarker marker, bool hovered)
    {
        var center = new System.Numerics.Vector2(marker.ScreenX, marker.ScreenY);

        if (!marker.Occupied)
        {
            Raylib.DrawCircle((int)center.X, (int)center.Y, marker.MarkerRadius, Palette.TurretEmptySlot);
            if (hovered) Raylib.DrawCircleLines((int)center.X, (int)center.Y, marker.MarkerRadius + ClickSlack, Palette.Accent);
            return;
        }

        // A guided mount is a rack: a square in the hull frame (the menu draws the hull unrotated), not
        // a point that tracks where the gun happens to be looking.
        if (WeaponType.FromName(marker.WeaponName)?.Stats.Homing == true)
        {
            RenderHelpers.DrawRackMarker(center.X, center.Y, marker.MarkerRadius, 0f, hovered ? Palette.Accent : Palette.TurretMounted);
            return;
        }

        float aimDeg = Angle.ToDeg(marker.AimAngle);
        RenderHelpers.DrawTurretMarker(center.X, center.Y, marker.MarkerRadius, aimDeg, hovered ? Palette.Accent : Palette.TurretMounted);
    }

    static void DrawNumberBadge(SlotMarker marker, float scale)
    {
        int font = (int)(NumberFontSize * scale);
        string label = marker.Number.ToString();
        Raylib.DrawText(label, (int)(marker.ScreenX + marker.MarkerRadius + 2f), (int)(marker.ScreenY - font / 2f), font, Palette.TextBright);
    }
}
