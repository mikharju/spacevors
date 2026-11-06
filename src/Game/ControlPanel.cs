using Raylib_cs;

namespace Spacevors.Game;

// A titled instruction panel: section headings, key-cap rows, wrapped notes under them. Pure drawing —
// the words live in MenuCopy and the screens own the input (plans/START_SCREEN_ONBOARDING.md).
// Height and Draw walk the content the same way, so the box is always the size the text needs.
public static class ControlPanel
{
    public readonly record struct Section(string Title, MenuCopy.ControlRow[] Rows);

    const int Padding = 16;
    const int TitleFontSize = 15;
    const int RowFontSize = 16;
    const int RowPitch = 26;
    const int TitleGap = 8;
    const int SectionGap = 14;
    const int NoteGap = 10;
    // The key caps sit left of this line and the words of every row start right of it, so the words
    // line up in a column the eye can read down on its own.
    const int KeyColumn = 100;

    public static int Height(int width, Section[] sections, string[] notes, string footer, float scale = 1f)
        => 2 * S(Padding, scale) + ContentHeight(width - 2 * S(Padding, scale), sections, notes, footer, scale);

    public static void Draw(int x, int y, int width, Section[] sections, string[] notes, string footer, float scale = 1f)
    {
        int pad = S(Padding, scale);
        int contentWidth = width - 2 * pad;
        Ui.DrawPanel(x, y, width, Height(width, sections, notes, footer, scale), S(8, scale), Palette.PanelFill, Palette.PanelEdge);

        int cursor = y + pad;
        foreach (var section in sections)
            cursor = DrawSection(x + pad, cursor, contentWidth, section, scale);

        foreach (string note in notes)
            cursor = DrawWrapped(x + pad, cursor, contentWidth, note, Palette.TextMuted, scale) + S(NoteGap, scale);

        if (footer.Length > 0)
            DrawWrapped(x + pad, cursor, contentWidth, footer, Palette.Accent, scale);
    }

    static int ContentHeight(int contentWidth, Section[] sections, string[] notes, string footer, float scale)
    {
        int pitch = S(RowPitch, scale);
        int height = 0;

        foreach (var section in sections)
            height += S(TitleFontSize, scale) + S(TitleGap, scale)
                      + section.Rows.Sum(row => RowLines(row, contentWidth, scale).Length) * pitch
                      + S(SectionGap, scale);

        int noteFont = S(RowFontSize, scale);
        foreach (string note in notes)
            height += Ui.WrapText(note, contentWidth, noteFont).Length * pitch + S(NoteGap, scale);

        if (footer.Length > 0)
            height += Ui.WrapText(footer, contentWidth, noteFont).Length * pitch;

        return height;
    }

    static int DrawSection(int x, int y, int contentWidth, Section section, float scale)
    {
        int titleFont = S(TitleFontSize, scale);
        Raylib.DrawText(section.Title.ToUpperInvariant(), x, y, titleFont, Palette.Accent);
        int cursor = y + titleFont + S(TitleGap, scale);

        foreach (var row in section.Rows)
            cursor = DrawRow(x, cursor, contentWidth, row, scale);

        return cursor + S(SectionGap, scale);
    }

    static int DrawRow(int x, int y, int contentWidth, MenuCopy.ControlRow row, float scale)
    {
        int font = S(RowFontSize, scale);
        int capX = x;
        foreach (string key in row.Keys)
            capX = Ui.DrawKeyCap(capX, y, key, font, Palette.TextBright, Palette.KeyCapFill) + S(5, scale);

        var lines = RowLines(row, contentWidth, scale);
        for (int i = 0; i < lines.Length; i++)
            Raylib.DrawText(lines[i], x + S(KeyColumn, scale), y + i * S(RowPitch, scale), font, Palette.TextMuted);

        return y + lines.Length * S(RowPitch, scale);
    }

    // A row's words, wrapped to what is left of the key column. Shared by the measure and the draw so a
    // long line grows the box instead of running out of it.
    static string[] RowLines(MenuCopy.ControlRow row, int contentWidth, float scale) =>
        Ui.WrapText(row.What, contentWidth - S(KeyColumn, scale), S(RowFontSize, scale));

    static int DrawWrapped(int x, int y, int contentWidth, string text, Color color, float scale)
    {
        int font = S(RowFontSize, scale);
        var lines = Ui.WrapText(text, contentWidth, font);
        for (int i = 0; i < lines.Length; i++)
            Raylib.DrawText(lines[i], x, y + i * S(RowPitch, scale), font, color);

        return y + lines.Length * S(RowPitch, scale);
    }

    static int S(int value, float scale) => (int)(value * scale);
}
