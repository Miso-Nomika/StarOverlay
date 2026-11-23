using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarOverlay.Ingame.Protocol;

public static class Protocol
{
    /// <summary>Raised when the message set changes in a way an older side cannot read.</summary>
    public const int Version = 1;

    /// <summary>Largest line either side accepts. A meter snapshot is small; a long 20-person raid record is a few MB.</summary>
    public const int MaxLineBytes = 8 * 1024 * 1024;

    public static string PipeName(string userSid) => "StarOverlay.Ingame." + userSid;
}

public static class ProtocolJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // keep Korean and Japanese readable in logs and smaller on the wire
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>One line, no line breaks inside (string contents are escaped).</summary>
    public static string Write(Envelope message) => JsonSerializer.Serialize(message, Options);

    public static string WriteWindows(IReadOnlyList<WindowDto> windows) => JsonSerializer.Serialize(windows, Options);

    /// <summary>One window as text: two windows with the same text look the same.</summary>
    public static string WriteWindow(WindowDto window) => JsonSerializer.Serialize(window, Options);

    public static Envelope? Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > Protocol.MaxLineBytes) return null;
        try
        {
            return JsonSerializer.Deserialize<Envelope>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Layout rules shared by the service (defaults, limits) and the agent (pixels), testable without Unity.</summary>
public static class LayoutMath
{
    public const double ReferenceWidth = 1920;
    public const double ReferenceHeight = 1080;
    public const double MinWidth = 0.08;
    public const double MinHeight = 0.04;

    public static LayoutDto Clamp(LayoutDto layout)
    {
        var w = Math.Clamp(layout.W <= 0 ? MinWidth : layout.W, MinWidth, 1.0);
        var h = Math.Clamp(layout.H <= 0 ? MinHeight : layout.H, MinHeight, 1.0);
        return new LayoutDto
        {
            W = w,
            H = h,
            X = Math.Clamp(layout.X, 0, 1 - w),
            Y = Math.Clamp(layout.Y, 0, 1 - h),
            Opacity = Math.Clamp(layout.Opacity, 20, 100),
            Pinned = layout.Pinned,
            Collapsed = layout.Collapsed,
            HeaderHidden = layout.HeaderHidden,
            WindowHidden = layout.WindowHidden,
            Channels = layout.Channels,
        };
    }

    /// <summary>Where the n-th window goes when nothing is stored yet: a cascade down the left side.</summary>
    public static LayoutDto Default(int index, double width, double height)
    {
        var column = index / 4;
        var row = index % 4;
        return Clamp(new LayoutDto { X = 0.02 + column * 0.27, Y = 0.08 + row * 0.2, W = width, H = height });
    }

    /// <summary>Pixel rectangle (x, y, width, height) on a canvas that has the reference size.</summary>
    public static (double X, double Y, double W, double H) ToReferencePixels(LayoutDto layout) =>
        (layout.X * ReferenceWidth, layout.Y * ReferenceHeight, layout.W * ReferenceWidth, layout.H * ReferenceHeight);

    /// <summary>
    /// Pointer movement from Unity (screen pixels, Y grows upwards) as a layout delta in reference pixels (Y grows downwards,
    /// like the layout). Unity reports the pointer with Y up, the window layout counts from the top.
    /// </summary>
    public static (double X, double Y) FromPointerDelta(double pointerX, double pointerY, double scale)
    {
        var s = scale <= 0 ? 1 : scale;
        return (pointerX / s, -pointerY / s);
    }

    /// <summary>Normalized position after dragging by a pixel delta on the reference canvas.</summary>
    public static LayoutDto Moved(LayoutDto layout, double deltaX, double deltaY) =>
        Clamp(new LayoutDto
        {
            X = layout.X + deltaX / ReferenceWidth,
            Y = layout.Y + deltaY / ReferenceHeight,
            W = layout.W,
            H = layout.H,
            Opacity = layout.Opacity,
            Pinned = layout.Pinned,
            Collapsed = layout.Collapsed,
            HeaderHidden = layout.HeaderHidden,
            WindowHidden = layout.WindowHidden,
            Channels = layout.Channels,
        });

    public static LayoutDto Resized(LayoutDto layout, double deltaX, double deltaY) =>
        Clamp(new LayoutDto
        {
            X = layout.X,
            Y = layout.Y,
            W = layout.W + deltaX / ReferenceWidth,
            H = layout.H + deltaY / ReferenceHeight,
            Opacity = layout.Opacity,
            Pinned = layout.Pinned,
            Collapsed = layout.Collapsed,
            HeaderHidden = layout.HeaderHidden,
            WindowHidden = layout.WindowHidden,
            Channels = layout.Channels,
        });
}
