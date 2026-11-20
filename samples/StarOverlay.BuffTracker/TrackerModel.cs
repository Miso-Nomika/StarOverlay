using System.Text.Json;
using System.Text.Json.Serialization;
using StarOverlay.Framework;

namespace StarOverlay.BuffTracker;

/// <summary>One tracked buff, as stored in the plugin setting <c>items</c> (a JSON array).</summary>
public sealed class TrackedBuff
{
    [JsonPropertyName("uid")] public int Uid { get; set; }

    [JsonPropertyName("time")] public bool Time { get; set; } = true;

    [JsonPropertyName("stacks")] public bool Stacks { get; set; } = true;

    /// <summary>"none" (no picture: the first letter of the name), "lib:Buffs/&lt;name&gt;.png" or "lib:Skills/&lt;name&gt;.png" (the bundled library) or "file:&lt;name&gt;.png" (a picture in assets\custom).</summary>
    [JsonPropertyName("image")] public string Image { get; set; } = "none";

    /// <summary>"none", "ring" (empties with the time) or "ring-reverse" (fills with the time).</summary>
    [JsonPropertyName("gauge")] public string Gauge { get; set; } = "ring";

    [JsonPropertyName("color")] public string Color { get; set; } = "#4FC3F7";

    [JsonPropertyName("hide")] public bool Hide { get; set; }
}

/// <summary>The data side of the buff tracker: the stored list, the edits of the editor widget, and the views for both widgets.</summary>
public sealed class TrackerModel(Func<string?> read, Action<string> write, string customDirectory, Func<long> nowMs)
{
    public const string ItemsKey = "items";
    public const long RecentMs = 300_000;
    const int MaxCandidates = 30;

    /// <summary>How opaque (percent) the picture of a buff that is there is drawn; 100 when not set. The time and the stacks have their own.</summary>
    public Func<int>? PresentOpacity { get; init; }

    /// <summary>How opaque (percent) the picture of a tracked buff that is not there is drawn; 40 when not set. 0 draws nothing.</summary>
    public Func<int>? AbsentOpacity { get; init; }

    /// <summary>How opaque (percent) the time and the stacks are drawn; 100 when not set.</summary>
    public Func<int>? TimeOpacity { get; init; }

    public Func<int>? StacksOpacity { get; init; }

    static int Percent(Func<int>? setting, int fallback) => Math.Clamp(setting?.Invoke() ?? fallback, 0, 100);

    static readonly string[] Gauges = ["ring", "ring-reverse", "none"];
    static readonly (string Hex, string Name)[] Colors =
    [
        ("#4FC3F7", "파랑"), ("#66BB6A", "초록"), ("#FFB74D", "주황"), ("#EF5350", "빨강"), ("#BA68C8", "보라"), ("#FFFFFF", "흰색"),
    ];

    readonly Dictionary<int, (LiveBuff Buff, long Seen)> _seen = [];
    readonly object _lock = new();

    List<TrackedBuff> Load()
    {
        try
        {
            var text = read();
            if (!string.IsNullOrWhiteSpace(text))
                return JsonSerializer.Deserialize<List<TrackedBuff>>(text) ?? [];
        }
        catch (JsonException)
        {
        }

        return [];
    }

    void Save(List<TrackedBuff> items) => write(JsonSerializer.Serialize(items));

    /// <summary>Remembers the own buffs seen now (the candidate list shows those of the last five minutes).</summary>
    public void Observe(IReadOnlyList<LiveBuff> own)
    {
        var now = nowMs();
        lock (_lock)
        {
            foreach (var buff in own) _seen[buff.Id] = (buff, now);
        }
    }

    public void Apply(EditOp op)
    {
        lock (_lock)
        {
            var items = Load();
            if (!int.TryParse(op.Key, out var uid) || uid <= 0) return;
            var index = items.FindIndex(i => i.Uid == uid);
            switch (op.Action)
            {
                case "add" when index < 0:
                    items.Add(new TrackedBuff { Uid = uid });
                    break;
                case "delete" when index >= 0:
                    items.RemoveAt(index);
                    break;
                case "up" when index > 0:
                    (items[index - 1], items[index]) = (items[index], items[index - 1]);
                    break;
                case "down" when index >= 0 && index < items.Count - 1:
                    (items[index + 1], items[index]) = (items[index], items[index + 1]);
                    break;
                case "field" when index >= 0:
                    Field(items[index], op.Value);
                    break;
                case "set" when index >= 0 && SetImage(items[index], op.Value):
                    break;
                default:
                    return;
            }

            Save(items);
        }
    }

    void Field(TrackedBuff item, string field)
    {
        switch (field)
        {
            case "time":
                item.Time = !item.Time;
                break;
            case "stacks":
                item.Stacks = !item.Stacks;
                break;
            case "gauge":
                item.Gauge = Next(Gauges, item.Gauge);
                break;
            case "color":
                item.Color = Next(Colors.Select(c => c.Hex).ToArray(), item.Color);
                break;
        }
    }

    static string Next(string[] choices, string current)
    {
        var at = Array.FindIndex(choices, c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase));
        return choices[(at + 1) % choices.Length];
    }

    static readonly string[] LibraryFolders = ["Buffs", "Skills"];

    /// <summary>The folder with the bundled pictures (assets\library\Buffs and \Skills), next to the folder of the user's own pictures.</summary>
    string LibraryDirectory => Path.Combine(Path.GetDirectoryName(customDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? ".", "library");

    /// <summary>Takes "image=&lt;ref&gt;" when the reference is "none", a bundled picture or a picture file name: nothing else is ever turned into a path.</summary>
    static bool SetImage(TrackedBuff item, string value)
    {
        if (!value.StartsWith("image=", StringComparison.Ordinal)) return false;
        var image = value[6..];
        if (image == "none") item.Image = image;
        else if (image.StartsWith("lib:", StringComparison.Ordinal) && IsLibraryPicture(image[4..])) item.Image = image;
        else if (image.StartsWith("file:", StringComparison.Ordinal) && IsPictureName(image[5..])) item.Image = image;
        else return false;
        return true;
    }

    static bool IsPictureName(string name) =>
        name.Length > 4 && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && name.IndexOfAny(['/', '\\', ':']) < 0 && !name.Contains("..");

    static bool IsLibraryPicture(string reference)
    {
        var parts = reference.Split('/');
        return parts.Length == 2 && LibraryFolders.Contains(parts[0]) && IsPictureName(parts[1]);
    }

    static string NameOf(int uid) => BuffBook.Find(uid)?.Name is { Length: > 0 } name ? name : "알 수 없는 버프";

    string ImagePath(TrackedBuff item)
    {
        if (item.Image.StartsWith("lib:", StringComparison.Ordinal) && IsLibraryPicture(item.Image[4..]))
            return "file:" + Path.Combine(LibraryDirectory, item.Image[4..].Replace('/', Path.DirectorySeparatorChar));
        if (item.Image.StartsWith("file:", StringComparison.Ordinal) && IsPictureName(item.Image[5..]))
            return "file:" + Path.Combine(customDirectory, item.Image[5..]);
        return "";
    }

    string ImageLabel(TrackedBuff item)
    {
        if (item.Image.StartsWith("lib:", StringComparison.Ordinal)) return Path.GetFileNameWithoutExtension(item.Image[4..]);
        if (item.Image.StartsWith("file:", StringComparison.Ordinal)) return item.Image[5..];
        return "없음";
    }

    static string GaugeLabel(string gauge) => gauge switch { "ring-reverse" => "차오름", "none" => "없음", _ => "줄어듦" };

    static int Rgb(string hex) => int.TryParse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out var value) ? value & 0xFFFFFF : 0x4FC3F7;

    public IReadOnlyList<EditRowView> Rows()
    {
        lock (_lock)
        {
            return Load().Select(item => new EditRowView(
                item.Uid.ToString(),
                $"{NameOf(item.Uid)} ({item.Uid})",
                ImagePath(item),
                [
                    new EditFieldView("time", "시간", item.Time ? "on" : "off", true),
                    new EditFieldView("stacks", "중첩", item.Stacks ? "on" : "off", true),
                    new EditFieldView("image", "이미지", ImageLabel(item), false),
                    new EditFieldView("gauge", "게이지", GaugeLabel(item.Gauge), false),
                    new EditFieldView("color", "색", Colors.FirstOrDefault(c => c.Hex == item.Color.ToUpperInvariant()).Name ?? item.Color, false),
                ])).ToArray();
        }
    }

    public IReadOnlyList<EditCandidateView> Candidates()
    {
        lock (_lock)
        {
            var tracked = Load().Select(i => i.Uid).ToHashSet();
            var now = nowMs();
            return _seen
                .Where(pair => !tracked.Contains(pair.Key) && now - pair.Value.Seen <= RecentMs)
                .OrderByDescending(pair => pair.Value.Seen)
                .Take(MaxCandidates)
                .Select(pair =>
                {
                    var buff = pair.Value.Buff;
                    var name = BuffBook.Find(buff.Id)?.Name is { Length: > 0 } known ? known : buff.Name.Length > 0 ? buff.Name : "알 수 없는 버프";
                    var detail = buff.Duration > 0 ? $"중첩 {Math.Max(1, buff.Stacks)} · {buff.Duration:0.#}초" : $"중첩 {Math.Max(1, buff.Stacks)}";
                    return new EditCandidateView(buff.Id.ToString(), name, "", detail);
                })
                .ToArray();
        }
    }

    public IReadOnlyList<IconView> Icons(IReadOnlyList<LiveBuff> own)
    {
        List<TrackedBuff> items;
        lock (_lock) items = Load();
        var views = new List<IconView>();
        var timeAlpha = Percent(TimeOpacity, 100);
        var stacksAlpha = Percent(StacksOpacity, 100);
        foreach (var item in items.Where(i => !i.Hide))
        {
            var present = own.Where(b => b.Id == item.Uid).OrderByDescending(b => b.Stacks).ThenByDescending(b => b.EndsAtMs).FirstOrDefault();
            var entry = BuffBook.Find(item.Uid);
            var image = ImagePath(item);
            var gauge = Gauges.Contains(item.Gauge) ? item.Gauge : "ring";
            if (present == null)
            {
                views.Add(new IconView(item.Uid.ToString(), NameOf(item.Uid), image, "", "", 0, 0, false, gauge, Rgb(item.Color), true, Percent(AbsentOpacity, 40), timeAlpha, stacksAlpha));
                continue;
            }

            var ends = present.EndsAtMs;
            var total = (long)Math.Round((present.Duration > 0 ? present.Duration : entry?.Duration ?? 0) * 1000);
            views.Add(new IconView(
                item.Uid.ToString(),
                NameOf(item.Uid),
                image,
                "",
                item.Stacks && present.Stacks > 0 ? present.Stacks.ToString() : "",
                ends,
                total,
                item.Time && ends > 0,
                gauge,
                Rgb(item.Color),
                false,
                Percent(PresentOpacity, 100),
                timeAlpha,
                stacksAlpha));
        }

        return views;
    }
}
