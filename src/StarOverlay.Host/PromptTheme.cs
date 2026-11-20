using System.Drawing.Text;

namespace StarOverlay.Host;

/// <summary>Colors, font and button of the one window the service still opens (the translation prompt input).</summary>
internal static class PromptTheme
{
    public static readonly Color Panel = Color.FromArgb(22, 22, 26);
    public static readonly Color Ink = Color.FromArgb(236, 236, 236);
    public static readonly Color Field = Color.FromArgb(38, 38, 44);
    public static readonly Color Line = Color.FromArgb(82, 82, 92);
    public static readonly Font Font = BundledFont.Create(9F);

    public static Button FlatButton(string text, int width)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, 22),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Ink,
            BackColor = Color.FromArgb(48, 48, 56),
            Font = Font,
            Cursor = Cursors.Default,
            TabStop = false,
        };
        button.FlatAppearance.BorderColor = Line;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(66, 66, 76);
        return button;
    }
}

/// <summary>The bundled Pretendard font, so Korean and Japanese text looks the same on every PC.</summary>
internal static class BundledFont
{
    private static readonly PrivateFontCollection Text = new();

    public static Font Create(float emSize)
    {
        if (Text.Families.Length == 0)
            Text.AddFontFile(Materialize("Pretendard-Bold.ttf", "StarOverlay.Host.Fonts.Pretendard-Bold.ttf"));
        var family = Text.Families[0];
        var style = family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        return new Font(family, emSize, style, GraphicsUnit.Point);
    }

    private static string Materialize(string file, string resource)
    {
        using var stream = typeof(BundledFont).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("묶인 글꼴을 찾지 못했습니다.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var folder = Path.Combine(AppContext.BaseDirectory, "Fonts");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, file);
        if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
            File.WriteAllBytes(path, bytes);
        return path;
    }
}
