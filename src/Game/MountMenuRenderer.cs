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
    const int MountLabelFontSize = 12;
    const float LabelBelowFactor = 2f;
    const float ClickSlack = 8f; // a click this far outside a marker still hits it

    // Cargo list: a column at the left edge, starting well below the top so it never fights the hull.
    const float CargoMarginX = 40f;
    const float CargoTopFraction = 0.17f;
    const int CargoFontSize = 16;
    const int CargoRowPitch = 26;
    const int CargoRowPaddingX = 10;
    const int CargoRowPaddingY = 5;

    // The mount legend, directly under the cargo list: same column, same row rhythm, one more header.
    const int LegendRowPitch = 24;
    const int LegendHeaderGap = 8;

    // The rules panel, opposite the cargo column. It holds what never changes about this screen, which
    // leaves the line at the bottom free to say only what to do right now.
    const int HelpPanelWidth = 440; // wide enough that no row wraps: a help list that wraps reads as a wall
    const int HelpPanelMargin = 40;
    const int HelpPanelGap = 24;
    const float HelpPanelTopFraction = 0.2f;

    static readonly ControlPanel.Section[] HelpSections = [new("Mounting", MenuCopy.MountingRows)];

    public readonly record struct SlotMarker(
        Entity SlotEntity,
        TurretSlotDefinition Slot,
        bool Occupied,
        string WeaponName,
        // The mount's own name in the player's words (`left flank · large`), and what sits in it. A ring
        // that carries only a number says nothing about which gun is where, which is the reason a player
        // used to take a gun out to find that out (plans/CARD_AND_LOADOUT_READABILITY.md D7).
        string MountLabel,
        string WeaponLabel,
        // What a click on this ring would do, in words, shown while it is hovered: what stops the gun in
        // hand, or what a swap would move.
        string HoverNote,
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

    // One row of the mount legend: a mount, its number, and what sits in it. The legend is the answer to
    // "which gun is where", and hovering it lights the ring it stands for.
    public readonly record struct LegendRow(
        int MarkerIndex,
        int Number,
        string MountLabel,
        string WeaponLabel,
        float ScreenX,
        float ScreenY,
        float Width,
        float Height);

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
        string Hint,
        LegendRow[] Legend,
        float LegendTop);

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

            string gunName = occupied ? em.GetComponent<Turret>(slotEntity).WeaponName : "";
            markers.Add(new SlotMarker(
                slotEntity,
                slot.Definition,
                occupied,
                gunName,
                MenuCopy.MountLabel(slot.Definition),
                occupied ? MenuCopy.FriendlyWeaponName(gunName) : "empty",
                HoverNote(occupied, gunName, held, slot.Definition),
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
            // The row is measured against the words it draws — a cargo gun's name, plus the words that say
            // this is the one being carried — so the box always holds the text (D9: state what is in hand
            // where the held gun is, not only in the hint line at the bottom).
            string text = CargoText(name, name == heldWeapon);
            cargo.Add(new CargoItem(name, cargoX, cargoY, Raylib.MeasureText(text, rowFont) + 2f * CargoRowPaddingX * scale, rowPitch, name == heldWeapon));
            cargoY += rowPitch;
        }

        float legendTop = top + rowPitch + cargo.Count * rowPitch + (CargoRowPitch + LegendHeaderGap) * scale;
        var legend = BuildLegend(markers, scale, cargoX, legendTop + (int)(CargoFontSize * scale) + 4);
        return new Prepared(ship, shipX, shipY, diameter, scale, [.. markers], [.. cargo], top, heldWeapon, hint, legend, legendTop);
    }

    // Why a ring is or is not a place for the gun in hand. The screen already rings what fits; this says the
    // reason, in the place the player is looking, instead of leaving "why not" to the rules panel.
    static string HoverNote(bool occupied, string gunName, WeaponType? held, TurretSlotDefinition slot)
    {
        if (held is not { } gun) return occupied ? "take it down to cargo" : "";
        if (!TurretMount.Fits(slot, gun)) return $"needs a {MenuCopy.SizeWord(slot.Size)} mount";
        return occupied ? $"swap with {MenuCopy.FriendlyWeaponName(gunName)}" : $"mount {MenuCopy.FriendlyWeaponName(gun.Name)}";
    }

    // The legend sits in the cargo column, under the guns that are not firing: the two lists together are
    // everything the ship owns, and the mount list is the one that says where each gun stands.
    static LegendRow[] BuildLegend(IReadOnlyList<SlotMarker> markers, float scale, float x, float top)
    {
        int font = (int)(CargoFontSize * scale);
        int pitch = (int)(LegendRowPitch * scale);
        var rows = new List<LegendRow>();
        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            rows.Add(new LegendRow(
                i, marker.Number, marker.MountLabel, marker.WeaponLabel, x, top + i * pitch,
                Raylib.MeasureText(LegendText(marker), font) + 2f * CargoRowPaddingX * scale, pitch));
        }

        return [.. rows];
    }

    // The legend row and the ring describe one mount, so the same hover lights both.
    public static int GetHoveredLegendIndex(Prepared prepared, int mouseX, int mouseY)
    {
        for (int i = 0; i < prepared.Legend.Length; i++)
        {
            var row = prepared.Legend[i];
            if (mouseX >= row.ScreenX && mouseX <= row.ScreenX + row.Width &&
                mouseY >= row.ScreenY && mouseY <= row.ScreenY + row.Height)
                return i;
        }

        return -1;
    }

    static string LegendText(SlotMarker marker) =>
        marker.Occupied ? $"{marker.Number}  {marker.MountLabel}  {marker.WeaponLabel}" : $"{marker.Number}  {marker.MountLabel}  empty";

    static float CargoBottom(IReadOnlyList<CargoItem> cargo, float top, int rowPitch, float scale) =>
        top + rowPitch + cargo.Count * rowPitch + (CargoRowPitch + LegendHeaderGap) * scale;

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

    public static void Draw(Prepared prepared, int hoveredMount, int hoveredCargo, int hoveredLegend, int windowWidth, int windowHeight)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, Palette.ScreenDim);

        ShipSpriteRenderer.DrawShipSprite(prepared.Ship, prepared.ShipX, prepared.ShipY, prepared.ShipDiameter, 0f);

        Entity? hoveredSlot = hoveredMount >= 0 ? prepared.Markers[hoveredMount].SlotEntity : null;

        for (int i = 0; i < prepared.Markers.Length; i++)
        {
            var marker = prepared.Markers[i];
            // The legend row and the ring are the same mount: either one being hovered lights both.
            bool hovered = (marker.Selectable && marker.SlotEntity == hoveredSlot) || i == hoveredLegend;
            DrawArc(marker, hovered);
            if (marker.AcceptsHeld) DrawFitRing(marker, hovered, prepared.Scale);
            DrawMarker(marker, hovered);
            DrawNumberBadge(marker, prepared.Scale);
            DrawMountLabel(marker, hovered, prepared.Scale);
        }

        DrawCargo(prepared, hoveredCargo);
        DrawLegend(prepared, hoveredLegend);
        DrawHelpPanel(prepared, windowWidth, windowHeight);

        int font = (int)(UpgradeMenuRenderer.HintFontSize * prepared.Scale);
        Raylib.DrawText(prepared.Hint, windowWidth / 2 - Raylib.MeasureText(prepared.Hint, font) / 2,
            UpgradeMenuRenderer.GetHintY(windowHeight, prepared.Scale), font, Palette.TextMuted);
    }

    // Skipped when the hull is wide enough to reach the panel's slot — the hull and its arcs are the
    // point of this screen, and text never earns the right to sit on top of them.
    static void DrawHelpPanel(Prepared prepared, int windowWidth, int windowHeight)
    {
        int width = (int)(HelpPanelWidth * prepared.Scale);
        int x = windowWidth - (int)(HelpPanelMargin * prepared.Scale) - width;
        int hullRight = (int)(prepared.ShipX + prepared.ShipDiameter / 2f);
        if (x < hullRight + (int)(HelpPanelGap * prepared.Scale)) return;

        ControlPanel.Draw(x, (int)(windowHeight * HelpPanelTopFraction), width, HelpSections, [], MenuCopy.MountingNote, prepared.Scale);
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
            Raylib.DrawText(CargoText(item.WeaponName, item.Held), (int)(item.ScreenX + CargoRowPaddingX * prepared.Scale), (int)item.ScreenY, headerFont, text);
        }
    }

    // A cargo gun's row says in words which one the player is carrying, so the state is not carried by
    // colour alone.
    static string CargoText(string weaponName, bool held) =>
        MenuCopy.FriendlyWeaponName(weaponName) + (held ? "  (in hand)" : "");

    // The mount list: number, where the mount sits and what stands in it. Read top to bottom it answers
    // "which gun is in what position" without taking a single gun down.
    static void DrawLegend(Prepared prepared, int hoveredLegend)
    {
        int font = (int)(CargoFontSize * prepared.Scale);
        float x = prepared.Legend.Length > 0 ? prepared.Legend[0].ScreenX : (int)(CargoMarginX * prepared.Scale);

        Raylib.DrawText("MOUNTS", (int)x, (int)prepared.LegendTop, font, Palette.Accent);

        for (int i = 0; i < prepared.Legend.Length; i++)
        {
            var row = prepared.Legend[i];
            var marker = prepared.Markers[row.MarkerIndex];

            if (i == hoveredLegend)
                Ui.DrawRoundedRect((int)row.ScreenX, (int)row.ScreenY, (int)row.Width, (int)(row.Height - 3), 3, Palette.RowHover);

            Color text = i == hoveredLegend ? Palette.Accent : marker.Occupied ? Palette.TextBright : Palette.TextMuted;
            Raylib.DrawText(LegendText(marker), (int)(row.ScreenX + CargoRowPaddingX * prepared.Scale), (int)row.ScreenY, font, text);
        }
    }

    // One word under the ring for the gun standing in it, and `empty` for a mount that is not. The legend
    // holds the full list; this is the answer at the ring itself.
    static void DrawMountLabel(SlotMarker marker, bool hovered, float scale)
    {
        int font = (int)(MountLabelFontSize * scale);
        string text = marker.WeaponLabel;
        Color color = hovered ? Palette.Accent : marker.Occupied ? Palette.TextBright : Palette.TextMuted;
        // Below the marker's aiming tip, which reaches 1.9 radii out from the ring.
        int y = (int)(marker.ScreenY + marker.MarkerRadius * LabelBelowFactor + 2f);
        Raylib.DrawText(text, (int)(marker.ScreenX - Raylib.MeasureText(text, font) / 2f), y, font, color);

        if (!hovered || marker.HoverNote.Length == 0) return;
        string note = marker.HoverNote;
        Raylib.DrawText(note, (int)(marker.ScreenX - Raylib.MeasureText(note, font) / 2f), y + font + 2, font, Palette.Accent);
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
