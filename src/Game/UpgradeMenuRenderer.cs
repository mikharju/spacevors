using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class UpgradeMenuRenderer
{
    const int UpgradeCardMinWidth = 60;
    const int UpgradeCardMaxWidth = 320;
    public const int UpgradeCardHeight = 200;
    const int UpgradeCardSpacing = 16;
    const int UpgradeCardSideMargin = 40;
    // A card carries two entries — the bundle's strong half and its weak half — so both are drawn smaller
    // than a one-card layout would need.
    const int BundleLabelFontSize = 17;
    const int BundleValueFontSize = 26;
    const int MaxUpgradeLabelLines = 3;
    // A new-weapon card lists the numbers of the gun **as it arrives** — which is the table's own numbers unless
    // the hull had no free mount for it and the deal put upgrades on it
    // (plans/PREUPGRADED_NEW_WEAPONS.md). The block is skipped rather than squeezed when the window is too narrow
    // for it; the arriving upgrades are in the card's label, so they survive that.
    const int UpgradeCardStatFontSize = 15;
    const int MinCardWidthForStats = 150;

    public const int HintFontSize = 16;
    public const int HintGap = 28; // gap between card bottom and hint text top
    const int BottomMargin = 32;
    const float FadeInSeconds = 0.15f;

    // Fade-in state: the menu re-fades each time it appears after being hidden.
    static bool _cardsWereShown;
    static float _cardsShownSince;

    public static void ResetFade() => _cardsWereShown = false;

    public static int GetHintY(int windowHeight, float scale) => (int)(windowHeight - (BottomMargin + HintFontSize) * scale);

    public static void DrawCards(int windowWidth, int windowHeight, PendingUpgradeOptions options, int hoveredIndex, float scale, int rerollCharges, bool hoveredReroll, int level)
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
            DrawBundleCard((int)topLeft.X, (int)topLeft.Y, w, h, options.Bundles[i], (i + 1).ToString(), i == hoveredIndex, fade, scale);
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
    private static void DrawBundleCard(int x, int y, int width, int height, UpgradeBundle bundle, string key, bool hovered, float fade, float scale)
    {
        int radius = (int)(6 * scale);
        Color background = hovered ? new Color(50, 50, 68, 230) : new Color(28, 30, 40, 230);
        Color border = hovered ? Palette.Accent : Palette.PanelEdge;
        y -= hovered ? (int)(4 * scale) : 0;

        Ui.DrawPanel(x, y, width, height, radius, Ui.WithAlpha(background, fade), Ui.WithAlpha(border, fade));
        DrawKeyBadge(x + (int)(10 * scale), y + (int)(10 * scale), key, fade, scale);

        int halfHeight = height / 2;
        DrawEntry(x, y, width, halfHeight, bundle.Powerful, fade, scale);
        DrawEntry(x, y + halfHeight, width, halfHeight, bundle.Weak, fade, scale);

        int dividerInset = (int)(14 * scale);
        Raylib.DrawRectangle(x + dividerInset, y + halfHeight, width - 2 * dividerInset, (int)(1 * scale) + 1,
            Ui.WithAlpha(Palette.PanelEdge, fade));
    }

    // One half of a bundle: muted label, accent value, and for a new gun its mount size and two base numbers.
    // Centred in the band it was given. A **super** half — twice the step of the same card — is marked in green
    // (plans/SUPER_UPGRADES.md D8): a wash over its band, a green edge down its left side, a green value and a
    // small SUPER tag. The label keeps its shape, so a narrow card still fits its text.
    private static void DrawEntry(int x, int y, int width, int height, UpgradableOption option, float fade, float scale)
    {
        int labelFont = (int)(BundleLabelFontSize * scale);
        int valueFont = (int)(BundleValueFontSize * scale);
        int statFont = (int)(UpgradeCardStatFontSize * scale);
        string[] specs = NewWeaponSpecs(option) ?? [];

        if (option.IsSuper) DrawSuperBand(x, y, width, height, fade, scale);

        string[] labelLines = Ui.WrapText(GetUpgradeLabel(option), width - (int)(24 * scale), labelFont);
        int numLines = Math.Min(labelLines.Length, MaxUpgradeLabelLines);
        int linePitch = labelFont + (int)(3 * scale);
        int labelHeight = numLines * linePitch - (int)(3 * scale);
        int valueGap = (int)(10 * scale);
        bool showSpecs = specs.Length > 0 && width >= (int)(MinCardWidthForStats * scale);
        int specPitch = statFont + (int)(3 * scale);
        int specsGap = showSpecs ? (int)(8 * scale) : 0;
        int specsHeight = showSpecs ? specs.Length * specPitch - (int)(3 * scale) : 0;

        int contentY = y + (height - labelHeight - valueGap - valueFont - specsGap - specsHeight) / 2;

        for (int i = 0; i < numLines; i++)
        {
            int lineW = Raylib.MeasureText(labelLines[i], labelFont);
            Raylib.DrawText(labelLines[i], x + width / 2 - lineW / 2, contentY + i * linePitch, labelFont,
                Ui.WithAlpha(Palette.TextMuted, fade));
        }

        int valueY = contentY + labelHeight + valueGap;
        string statValue = GetUpgradeValue(option);
        int valueWidth = Raylib.MeasureText(statValue, valueFont);
        Raylib.DrawText(statValue, x + width / 2 - valueWidth / 2, valueY, valueFont,
            Ui.WithAlpha(option.IsSuper ? Palette.SuperStep : Palette.Accent, fade));

        for (int i = 0; i < specs.Length; i++)
            Raylib.DrawText(specs[i], x + (int)(16 * scale), valueY + valueFont + specsGap + i * specPitch, statFont,
                Ui.WithAlpha(Palette.TextMuted, fade));

        if (option.IsSuper) DrawSuperTag(x, y, width, statFont, fade, scale);
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

    private static void DrawKeyBadge(int x, int y, string key, float fade, float scale)
    {
        int badgeSize = (int)(24 * scale);
        Ui.DrawRoundedRect(x, y, badgeSize, badgeSize, (int)(5 * scale), Ui.WithAlpha(Palette.KeyCapFill, fade));

        int font = (int)(18 * scale);
        int keyWidth = Raylib.MeasureText(key, font);
        Raylib.DrawText(key, x + (badgeSize - keyWidth) / 2, y + (int)(4 * scale), font, Ui.WithAlpha(Palette.TextBright, fade));
    }

    private static string GetUpgradeLabel(UpgradableOption option)
    {
        if (option.IsNewWeapon)
        {
            string gun = MenuCopy.FriendlyWeaponName(option.WeaponName);
            string arriving = ArrivingNames(option.PreApplied);
            // The set has to be on the card, not only in the spec block below: the block is skipped on a narrow
            // card, and two bundles offering the same gun in two builds are otherwise indistinguishable here.
            return arriving.Length == 0 ? $"new weapon {gun}" : $"new weapon {gun} — with {arriving}";
        }

        if (string.IsNullOrEmpty(option.WeaponName))
            return option.Stat switch
            {
                UpgradeOption.Hp => "hit points",
                // plans/ARMOR_AND_PIERCE.md: gunfire subtracts this from every hit, floored at 1.
                UpgradeOption.Armor => "hull armor",
                // plans/SHIELDS.md: the pool drinks a hit whole instead of taxing it, then recharges
                // behind a short shock. The card's value line is the points it banks: +16.
                UpgradeOption.Shield => "hull shield",
                UpgradeOption.PickupRadius => "pickup radius",
                UpgradeOption.ForwardAcceleration => "forward acceleration",
                UpgradeOption.TurnSpeed => "turn speed",
                UpgradeOption.SideThrust => "side thrust",
                UpgradeOption.BackThrust => "back thrust",
                _ => $"{option.Stat}"
            };

        // A gun is spelled on the card the way the start screen and the stats panel spell it, so
        // "MachineGun range" and "Machine Gun ahead" stop being two names for one gun. The bespoke
        // wording the starting guns had ("side shot attack speed") said less than the gun's own name,
        // so it is gone rather than translated.
        string name = MenuCopy.FriendlyWeaponName(option.WeaponName);
        return option.Stat switch
        {
            UpgradeOption.Range => $"{name} range",
            UpgradeOption.Damage => $"{name} damage",
            // A `FireRate` card is two different cards wearing one stat: on a gun whose volley grows it buys
            // another shot — so the card says "volley size", the game's own word for what gets bigger — and on
            // any other gun it really does fire the same shots sooner, so it says "attack speed". The test is the
            // one `WeaponUpgrade.ApplyStat` applies, per card, so a super two-shot card cannot be labelled a
            // faster trigger (R10).
            UpgradeOption.FireRate => GrowsVolley(option) ? $"{name} volley size" : $"{name} attack speed",
            UpgradeOption.ProjectileSpeed => $"{name} projectile speed",
            // plans/ARMOR_AND_PIERCE.md: the gun's line against the one armored hull.
            UpgradeOption.ArmorPierce => $"{name} armor pierce",
            // R19: the rounds are what carry the hit points, so the card says whose rounds get tougher.
            UpgradeOption.MissileHealth => $"{name} missile health",
            _ => $"{name} {option.Stat}"
        };
    }

    // For a new weapon the prominent line carries the mount size, which is the thing the player has
    // to know before picking a hull mount. For every other card it carries the step **this card** grants —
    // doubled on a super one — read off `option.Step`, the same value `ApplyUpgrade` grants from, so a card is
    // never a lie (R10).
    private static string GetUpgradeValue(UpgradableOption option)
    {
        if (WeaponType.FromName(option.WeaponName) is not { } weapon)
            return option.Step.DisplayValue;

        if (option.IsNewWeapon)
            return weapon.Size == TurretSize.Large ? "large" : "small";

        // R33: the damage card is a percent of the gun's base damage sized by its mount class, so the label
        // is read off the gun; the card definition carries no single percent. A `Damage` card is never super
        // (plans/SUPER_UPGRADES.md D3), so there is no doubled percent to print here.
        if (option.Stat == UpgradeOption.Damage)
            return $"+{UpgradeDefinition.DamagePercentFor(weapon.IsSupport)}%";

        // A card that grows a volley buys shots — two of them on a super card — rather than the +30 % a gun with
        // a fixed volley gets. A guided volley whose budget cannot take this card's shots takes the card as a
        // rate instead, which is why the test is per card (R17/R18).
        if (option.Stat == UpgradeOption.FireRate && GrowsVolley(option))
            return $"+{option.SalvoShots} shot{(option.SalvoShots > 1 ? "s" : "")}";

        return option.Step.DisplayValue;
    }

    // Whether this card buys shots on this gun. One predicate, shared by the label, the value and the grant, so
    // a card can never be labelled one thing and granted another.
    static bool GrowsVolley(UpgradableOption option) =>
        WeaponType.FromName(option.WeaponName)?.Stats.CanGrowSalvoBy(option.SalvoShots) ?? false;

    // The numbers of a new gun as it arrives. A gun the hull can still fit arrives plain, so these are the table's
    // own; a gun dealt to a full hull arrives carrying upgrades, and the card must print the upgraded numbers —
    // reading the table there is the lie R10 refuses. `WeaponUpgrade.Apply` is the same call that builds the
    // turret the player mounts, so the card and the gun cannot disagree.
    private static string[]? NewWeaponSpecs(UpgradableOption option)
    {
        if (!option.IsNewWeapon || WeaponType.FromName(option.WeaponName) is not { } weapon) return null;

        var arriving = WeaponUpgrade.Apply(TurretMount.CarriedTurret(weapon), option.PreApplied);

        // A card holds two entries, so the new gun says only what its two numbers are; attack speed and how
        // the round is delivered stay on the stats screen.
        return
        [
            $"range {Format(arriving.Range)}",
            $"damage {arriving.Weapon.Damage}{(arriving.Weapon.PelletCount > 1 ? $" x{arriving.Weapon.PelletCount}" : "")}",
        ];
    }

    // The arriving upgrades in the card's own words, shortened so a whole set fits the label of a card that also
    // carries a second entry: the full wording is what a single-stat card prints, and these are the same six.
    static string ArrivingNames(WeaponPreUpgrade mask) => string.Join(", ", WeaponUpgrade.StatsOf(mask).Select(ShortStat));

    static string ShortStat(UpgradeOption stat) => stat switch
    {
        UpgradeOption.FireRate => "rate",
        UpgradeOption.ProjectileSpeed => "proj speed",
        UpgradeOption.MissileHealth => "round hp",
        UpgradeOption.ArmorPierce => "pierce",
        _ => stat.ToString().ToLowerInvariant()
    };

    private static string Format(float value) => value < 10f ? $"{value:0.0}" : $"{(int)MathF.Round(value)}";
}
