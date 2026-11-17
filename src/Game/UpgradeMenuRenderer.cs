using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class UpgradeMenuRenderer
{
    // What the card copy needs that only the running game has: the hull's own numbers, the turret a card
    // would change, and whether pierce cards are in play yet. Built once per card screen, not per card.
    public readonly record struct CardContext(UpgradeCardCopy.Hull Hull, IReadOnlyDictionary<string, Turret> Guns, bool PierceUnlocked)
    {
        // A new-weapon card names a gun the ship does not have, and a stat card names one it does: the pair
        // a card prints only exists when there is a live turret to compare against.
        public Turret? GunOf(UpgradableOption card) =>
            card.IsNewWeapon || !Guns.TryGetValue(card.WeaponName, out var turret) ? null : turret;
    }

    const int UpgradeCardMinWidth = 60;
    const int UpgradeCardMaxWidth = 320;
    public const int UpgradeCardHeight = 200;
    const int UpgradeCardSpacing = 16;
    const int UpgradeCardSideMargin = 40;
    // A card carries two entries — the bundle's strong half and its weak half — so both are drawn smaller
    // than a one-card layout would need.
    const int BundleLabelFontSize = 17;
    const int BundleValueFontSize = 26;
    // The small rows under the step: the caps naming what an arriving gun carries, and the numbers before and
    // after (plans/CARD_AND_LOADOUT_READABILITY.md). There is deliberately no line cap on the label any more:
    // three lines with the rest silently dropped is how a four-stat arriving set used to disappear off a card.
    const int UpgradeCardStatFontSize = 15;

    public const int HintFontSize = 16;
    public const int HintGap = 28; // gap between card bottom and hint text top
    const int BottomMargin = 32;
    const float FadeInSeconds = 0.15f;

    // Fade-in state: the menu re-fades each time it appears after being hidden.
    static bool _cardsWereShown;
    static float _cardsShownSince;

    public static void ResetFade() => _cardsWereShown = false;

    public static int GetHintY(int windowHeight, float scale) => (int)(windowHeight - (BottomMargin + HintFontSize) * scale);

    public static void DrawCards(int windowWidth, int windowHeight, PendingUpgradeOptions options, int hoveredIndex, float scale, int rerollCharges, bool hoveredReroll, int level, CardContext context)
    {
        if (options.Bundles.Length == 0) return;

        if (!_cardsWereShown)
        {
            _cardsWereShown = true;
            _cardsShownSince = (float)Raylib.GetTime();
        }
        float fade = Ui.FadeAlpha(_cardsShownSince, FadeInSeconds);

        for (int i = 0; i < options.Bundles.Length; i++)
        {
            var (topLeft, w, h) = GetUpgradeCardRect(i, options.Bundles.Length, windowWidth, windowHeight, scale);
            DrawBundleCard((int)topLeft.X, (int)topLeft.Y, w, h, options.Bundles[i], (i + 1).ToString(), i == hoveredIndex, fade, scale, context);
        }

        var (firstCard, _, _) = GetUpgradeCardRect(0, options.Bundles.Length, windowWidth, windowHeight, scale);
        if (level <= HelpUntilLevel)
            DrawInstructions(windowWidth, (int)firstCard.Y, InstructionLines(options), fade, scale);

        // A level takes one of the five bundles (plans/UPGRADE_BUNDLE_PAIRS.md), so the hint names the
        // thing to click rather than a count to pick. The teaching block above the cards carries the rest.
        string hint = "Click a bundle or press its number";
        int font = (int)(HintFontSize * scale);
        Raylib.DrawText(hint, windowWidth / 2 - Raylib.MeasureText(hint, font) / 2, GetHintY(windowHeight, scale), font,
            Ui.WithAlpha(Palette.TextMuted, fade));

        DrawRerollPanel(windowWidth, windowHeight, scale, rerollCharges, hoveredReroll, fade);
    }

    // The first hands are the ones that need explaining: a bundle is taken whole, and the strong card is
    // the one on top. After that the block goes away rather than sit over a stats panel that has grown
    // with the ship's guns (plans/START_SCREEN_ONBOARDING.md).
    const int HelpUntilLevel = 3;
    const int InstructionGap = 22;

    static string[] InstructionLines(PendingUpgradeOptions options) =>
        options.Bundles.Any(b => b.Powerful.IsNewWeapon || b.Weak.IsNewWeapon)
            ? [.. MenuCopy.UpgradeLines, MenuCopy.UpgradeWeaponLine]
            : MenuCopy.UpgradeLines;

    static void DrawInstructions(int windowWidth, int cardTop, string[] lines, float fade, float scale)
    {
        int font = (int)(HintFontSize * scale);
        int pitch = font + (int)(6 * scale);
        int maxWidth = windowWidth - 2 * (int)(UpgradeCardSideMargin * scale);
        var wrapped = lines.Select(line => Ui.WrapText(line, maxWidth, font)).ToArray();
        int y = cardTop - (int)(InstructionGap * scale) - wrapped.Sum(w => w.Length) * pitch;

        for (int i = 0; i < wrapped.Length; i++)
        {
            // The super line is the one a player has to be told, so it is drawn in the colour the cards use for
            // a spiked half — the hand on screen then explains the colour (plans/SUPER_UPGRADES.md D8).
            Color color = i == 0 ? Palette.TextBright : SuperLine(lines[i]) ? Palette.SuperStep : Palette.TextMuted;
            foreach (string line in wrapped[i])
            {
                Raylib.DrawText(line, windowWidth / 2 - Raylib.MeasureText(line, font) / 2, y, font, Ui.WithAlpha(color, fade));
                y += pitch;
            }
        }
    }

    static bool SuperLine(string line) => line == MenuCopy.UpgradeSuperLine;

    // The salvage panel: how many reroll charges the player holds, and the key that spends one. It rides
    // at the right end of the hint row because the card row owns everything above it — the manage-mounts
    // button's slot would land on the card bottoms (a card's bottom edge is `GetHintY - HintGap`).
    // Rect and label come from one place, so the box clicked is the box drawn.
    public static (int X, int Y, int Width, int Height) RerollPanelRect(int windowWidth, int windowHeight, float scale, int charges)
    {
        int font = (int)(HintFontSize * scale);
        int padding = (int)(RerollPanelPadding * scale);
        int icon = (int)(RenderHelpers.SalvageIconRadius(scale) * 2f);
        int width = icon + padding + Raylib.MeasureText(RerollLabel(charges), font) + 2 * padding;
        int height = font + 2 * padding;
        int x = windowWidth - (int)(UpgradeCardSideMargin * scale) - width;
        int y = GetHintY(windowHeight, scale) - padding;
        return (x, y, width, height);
    }

    public static string RerollLabel(int charges) => charges > 0 ? $"R reroll x{charges}" : "no salvage";

    public static void DrawRerollPanel(int windowWidth, int windowHeight, float scale, int charges, bool hovered, float fade)
    {
        var (x, y, w, h) = RerollPanelRect(windowWidth, windowHeight, scale, charges);
        bool spendable = charges > 0;
        int font = (int)(HintFontSize * scale);
        int padding = (int)(RerollPanelPadding * scale);

        Color fill = hovered && spendable ? Palette.PanelFillHover : Palette.PanelFill;
        Color border = hovered && spendable ? Palette.Accent : Palette.PanelEdge;
        Ui.DrawPanel(x, y, w, h, (int)(6 * scale), Ui.WithAlpha(fill, fade), Ui.WithAlpha(border, fade));

        float iconRadius = RenderHelpers.SalvageIconRadius(scale);
        RenderHelpers.DrawSalvageCapsule(x + padding + iconRadius, y + h / 2f, iconRadius, spin: 0f);

        string label = RerollLabel(charges);
        Color text = spendable ? Palette.TextBright : Palette.TextMuted;
        Raylib.DrawText(label, x + w - padding - Raylib.MeasureText(label, font), y + (h - font) / 2, font,
            Ui.WithAlpha(text, fade));
    }

    const int RerollPanelPadding = 10;

    public static (Vector2 topLeft, int Width, int Height) GetUpgradeCardRect(int index, int optionCount, int windowWidth, int windowHeight, float scale)
    {
        if (optionCount <= 0) return (new Vector2(0, 0), 0, 0);

        int cardH = (int)(UpgradeCardHeight * scale);
        int sideMargin = (int)(UpgradeCardSideMargin * scale);
        int spacing = (int)(UpgradeCardSpacing * scale);
        int minW = (int)(UpgradeCardMinWidth * scale);
        int maxW = (int)(UpgradeCardMaxWidth * scale);

        int cardW = Math.Max(minW, Math.Min(maxW, (windowWidth - 2 * sideMargin - spacing * (optionCount - 1)) / optionCount));
        int totalW = cardW * optionCount + spacing * (optionCount - 1);
        int startX = (windowWidth - totalW) / 2;

        // Bottom row, above the hint line.
        int startY = GetHintY(windowHeight, scale) - (int)(HintGap * scale) - cardH;

        int x = startX + index * (cardW + spacing);
        return (new Vector2(x, startY), cardW, cardH);
    }

    // Soft-depth card: rounded translucent panel with shadow; hover lifts it 4px and brightens the border to
    // the accent. The panel holds the two cards of one bundle — strong half above, weak half below — because
    // they are taken together (plans/UPGRADE_BUNDLE_PAIRS.md §6).
    //
    // The halves are no longer equal. A new-weapon half carries a name, a mount size, the upgrades it arrives
    // with and its numbers, while its partner is usually one stat line: two equal halves pushed the busy one
    // across the divider (plans/CARD_AND_LOADOUT_READABILITY.md §1), so the divider now sits where the two
    // halves' content says it should, with a floor that keeps either side readable.
    private static void DrawBundleCard(int x, int y, int width, int height, UpgradeBundle bundle, string key, bool hovered, float fade, float scale, CardContext context)
    {
        int radius = (int)(6 * scale);
        Color background = hovered ? new Color(50, 50, 68, 230) : new Color(28, 30, 40, 230);
        Color border = hovered ? Palette.Accent : Palette.PanelEdge;
        y -= hovered ? (int)(4 * scale) : 0;

        Ui.DrawPanel(x, y, width, height, radius, Ui.WithAlpha(background, fade), Ui.WithAlpha(border, fade));
        DrawKeyBadge(x + (int)(10 * scale), y + (int)(10 * scale), key, fade, scale);

        var strong = UpgradeCardCopy.Build(bundle.Powerful, context.GunOf(bundle.Powerful), context.Hull, context.PierceUnlocked);
        var weak = UpgradeCardCopy.Build(bundle.Weak, context.GunOf(bundle.Weak), context.Hull, context.PierceUnlocked);

        var metrics = MetricsOf(scale);
        int desiredTop = DesiredHeight(strong, width, metrics);
        int desiredBottom = DesiredHeight(weak, width, metrics);
        int topHeight = UpgradeCardCopy.SplitHeight(desiredTop, desiredBottom, height, S(MinHalfHeight, scale));

        DrawEntry(x, y, width, topHeight, strong, bundle.Powerful, fade, scale, metrics);
        DrawEntry(x, y + topHeight, width, height - topHeight, weak, bundle.Weak, fade, scale, metrics);

        int dividerInset = (int)(14 * scale);
        Raylib.DrawRectangle(x + dividerInset, y + topHeight, width - 2 * dividerInset, (int)(1 * scale) + 1,
            Ui.WithAlpha(Palette.PanelEdge, fade));
    }

    // How tall a half would like to be: every row it has, at full words. Compared between the two halves,
    // never against a fixed 50/50.
    static int DesiredHeight(UpgradeCardCopy.Text text, int width, Metrics m)
    {
        int nameLines = Ui.WrapText(text.Name, width - m.Inset, m.LabelFont).Length;
        int height = nameLines * m.LabelPitch + m.ValueGap + m.ValueHeight;

        if (text.Caps.Length > 0)
            height += PackCaps(text.Caps, m.StatFont, width - m.Inset).Length * m.StatPitch + m.PipGap;
        if (text.Pips > 0) height += m.PipHeight;
        if (text.Numbers.Length > 0) height += text.Numbers.Length * m.StatPitch;
        return height;
    }

    // One half of a bundle: its name, its step, and below them the rows that explain the step — the caps
    // naming what an arriving gun already carries, the pip count of that gun's build, and the numbers before
    // and after. A **super** half is marked in green exactly as before (plans/SUPER_UPGRADES.md D8): a wash
    // over its band, a green edge down its left side, a green value and a small SUPER tag.
    private static void DrawEntry(int x, int y, int width, int height, UpgradeCardCopy.Text text, UpgradableOption option, float fade, float scale, Metrics m)
    {
        int textWidth = width - m.Inset;

        if (option.IsSuper) DrawSuperBand(x, y, width, height, fade, scale);

        string[] nameLines = Ui.WrapText(text.Name, textWidth, m.LabelFont);
        var (capsLines, numbers, showPips) = FitRows(text, m, textWidth, height);

        int contentHeight = nameLines.Length * m.LabelPitch + m.ValueGap + m.ValueHeight
            + capsLines.Length * m.StatPitch
            + (showPips ? m.PipHeight + m.PipGap : 0)
            + numbers.Length * m.StatPitch;

        int cursor = y + (height - contentHeight) / 2;
        cursor = DrawCentredLines(x, cursor, width, nameLines, m.LabelFont, Palette.TextMuted, m.LabelPitch, fade);
        cursor += m.ValueGap;

        Color valueColor = option.IsSuper ? Palette.SuperStep : Palette.Accent;
        DrawCentred(x, cursor, width, text.Value, m.ValueFont, valueColor, fade);
        cursor += m.ValueHeight;

        cursor = DrawCentredLines(x, cursor, width, capsLines, m.StatFont, Palette.TextBright, m.StatPitch, fade);
        if (showPips) cursor = DrawPips(x, cursor + m.PipGap, width, text.PipsFilled, text.Pips, m, fade) + m.PipGap;
        DrawLeftLines(x + m.Inset / 2, cursor, numbers, m.StatFont, Palette.TextMuted, m.StatPitch, fade);

        if (option.IsSuper) DrawSuperTag(x, y, width, m.StatFont, fade, scale);
    }

    // Which rows fit the half it was given. Priority (plans/CARD_AND_LOADOUT_READABILITY.md D6): the caps
    // that name an arriving set stay whole and in full words longest, because they are the only thing that
    // tells two builds of one gun apart; the number rows go first, then the caps shorten, and the pip row —
    // the smallest mark on the card — is the last thing left. A cap is never cut: a card that stops short of
    // what it grants is a lie (R10).
    static (string[] CapsLines, string[] Numbers, bool ShowPips) FitRows(UpgradeCardCopy.Text text, Metrics m, int maxWidth, int height)
    {
        string[] fullWords = PackCaps(text.Caps, m.StatFont, maxWidth);
        string[] shortWords = PackCaps(text.CapsShort, m.StatFont, maxWidth);

        // What the name and the step left behind is the budget for everything under them.
        int available = height - (Ui.WrapText(text.Name, maxWidth, m.LabelFont).Length * m.LabelPitch + m.ValueGap + m.ValueHeight);
        bool Room(string[] capsLines, string[] numberLines, bool showPips) =>
            capsLines.Length * m.StatPitch + numberLines.Length * m.StatPitch
            + (showPips && text.Pips > 0 ? m.PipHeight + m.PipGap : 0) <= available;

        foreach (string[] caps in new[] { fullWords, shortWords })
            for (int keep = text.Numbers.Length; keep >= 0; keep--)
            {
                string[] kept = [.. text.Numbers[..keep]];
                if (Room(caps, kept, text.Pips > 0)) return (caps, kept, text.Pips > 0);
            }

        return (shortWords, text.Numbers, false);
    }

    // A card half's row metrics, measured once per bundle card and used by the split, the fit and the draw, so
    // the three cannot disagree. The heights come from the font, not from its point size: the default font
    // draws a row about a third taller than the size asked for, and a layout that assumes the size is what
    // pushes the last row off the bottom of the panel.
    readonly record struct Metrics(int LabelFont, int ValueFont, int StatFont, int Inset,
        int LabelPitch, int StatPitch, int ValueHeight, int PipHeight, int PipMarkGap, int ValueGap, int PipGap);

    static Metrics MetricsOf(float scale) => new(
        S(BundleLabelFontSize, scale), S(BundleValueFontSize, scale), S(UpgradeCardStatFontSize, scale), S(EntrySideInset, scale),
        RowHeight(S(BundleLabelFontSize, scale)) + S(LineGap, scale),
        RowHeight(S(UpgradeCardStatFontSize, scale)) + S(LineGap, scale),
        RowHeight(S(BundleValueFontSize, scale)),
        S(PipSize, scale), S(PipGap, scale), S(ValueGap, scale), S(PipRowGap, scale));

    // The height the default font actually paints a row at, which is about a third more than the point size
    // asked for.  only returns a width, so the height comes from the font itself.
    static int RowHeight(int font) => (int)MathF.Ceiling(Raylib.MeasureTextEx(Raylib.GetFontDefault(), "H", font, 0f).Y);

    // Lay the caps out a whole cap at a time, so a stat name is never split across two lines the way a
    // wrapped sentence would split it.
    static string[] PackCaps(string[] caps, int font, int maxWidth)
    {
        var lines = new List<string>();
        string line = "";
        foreach (string cap in caps)
        {
            string candidate = line.Length == 0 ? cap : $"{line}{CapSeparator}";
            int candidateWidth = Raylib.MeasureText(line.Length == 0 ? cap : line + CapSeparator + cap, font);
            if (line.Length > 0 && candidateWidth > maxWidth)
            {
                lines.Add(line);
                line = cap;
            }
            else
            {
                line = line.Length == 0 ? cap : $"{line}{CapSeparator}{cap}";
            }
        }

        if (line.Length > 0) lines.Add(line);
        return [.. lines];
    }

    // The build of the gun a new-weapon card hands over: one mark per stat that gun could still be upgraded
    // with, filled for the ones it arrives carrying. A row of filled marks is the difference between two
    // builds of one gun before a word of it is read.
    static int DrawPips(int x, int y, int cardWidth, int filled, int total, Metrics m, float fade)
    {
        int size = m.PipHeight;
        int gap = m.PipMarkGap;
        int rowWidth = total * size + (total - 1) * gap;
        int left = x + (cardWidth - rowWidth) / 2;

        for (int i = 0; i < total; i++)
        {
            Color color = i < filled ? Palette.TextBright : Palette.TrackDark;
            Raylib.DrawRectangle(left + i * (size + gap), y, size, size, Ui.WithAlpha(color, fade));
        }

        return y + size;
    }

    static int DrawCentredLines(int x, int y, int width, string[] lines, int font, Color color, int pitch, float fade)
    {
        foreach (string line in lines)
        {
            DrawCentred(x, y, width, line, font, color, fade);
            y += pitch;
        }

        return y;
    }

    static int DrawLeftLines(int x, int y, string[] lines, int font, Color color, int pitch, float fade)
    {
        foreach (string line in lines)
        {
            Raylib.DrawText(line, x, y, font, Ui.WithAlpha(color, fade));
            y += pitch;
        }

        return y;
    }

    static void DrawCentred(int x, int y, int width, string text, int font, Color color, float fade)
    {
        Raylib.DrawText(text, x + width / 2 - Raylib.MeasureText(text, font) / 2, y, font, Ui.WithAlpha(color, fade));
    }

    static int S(int value, float scale) => (int)(value * scale);

    // Card anatomy. The two halves share a card of `UpgradeCardHeight`, so these govern how much of it each
    // row of a half may take.
    const int EntrySideInset = 24;   // both name wrap and the number rows keep this much clear of the edge
    const int LineGap = 3;
    const int ValueGap = 10;
    const int NumberRowGap = 4;
    const int PipRowGap = 6;
    const int PipSize = 7;
    const int PipGap = 4;
    const int MinHalfHeight = 72;    // a half never gets thinner than name + step
    const string CapSeparator = "  ·  ";

    private static void DrawKeyBadge(int x, int y, string key, float fade, float scale)
    {
        int badgeSize = (int)(24 * scale);
        Ui.DrawRoundedRect(x, y, badgeSize, badgeSize, (int)(5 * scale), Ui.WithAlpha(Palette.KeyCapFill, fade));

        int font = (int)(18 * scale);
        int keyWidth = Raylib.MeasureText(key, font);
        Raylib.DrawText(key, x + (badgeSize - keyWidth) / 2, y + (int)(4 * scale), font, Ui.WithAlpha(Palette.TextBright, fade));
    }

    static void DrawSuperBand(int x, int y, int width, int height, float fade, float scale)
    {
        int inset = (int)(2 * scale);
        Raylib.DrawRectangle(x + inset, y, width - 2 * inset, height, Ui.WithAlpha(Palette.SuperBand, fade));
        Raylib.DrawRectangle(x + inset, y, (int)(3 * scale) + 1, height, Ui.WithAlpha(Palette.SuperEdge, fade));
    }

    static void DrawSuperTag(int x, int y, int width, int font, float fade, float scale)
    {
        const string tag = "SUPER";
        Raylib.DrawText(tag, x + width - (int)(16 * scale) - Raylib.MeasureText(tag, font), y + (int)(6 * scale), font,
            Ui.WithAlpha(Palette.SuperStep, fade));
    }
}
