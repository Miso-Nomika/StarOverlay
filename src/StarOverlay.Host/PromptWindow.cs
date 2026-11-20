using StarOverlay.Framework;

namespace StarOverlay.Host;

internal sealed class PromptWindow : Form
{
    private readonly PluginSetting _prompt;
    private readonly PluginSetting? _device;
    private readonly TextBox _text;
    private readonly RadioButton _gpu;
    private string _savedPrompt;
    private bool _savedGpu;

    public PromptWindow(Form owner, PluginSetting prompt, PluginSetting? device)
    {
        _prompt = prompt;
        _device = device;
        // what is saved on disk, not the in-memory copy: both always show the stored values (the in-memory ones follow later)
        _savedPrompt = string.IsNullOrWhiteSpace(SavedValue(prompt)) ? TranslationPrompt.Default : SavedValue(prompt).Trim();
        _savedGpu = string.Equals(SavedValue(device), TranslationDevice.Gpu, StringComparison.OrdinalIgnoreCase);
        Owner = owner;
        Text = "번역 프롬프트";
        Font = PromptTheme.Font;
        BackColor = PromptTheme.Panel;
        ForeColor = PromptTheme.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        MinimizeBox = false;
        MaximizeBox = false;
        MinimumSize = new Size(360, 280);
        ClientSize = new Size(420, 340);

        var deviceRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = PromptTheme.Panel,
        };
        var caption = new Label
        {
            Text = "연산",
            AutoSize = true,
            ForeColor = PromptTheme.Ink,
            BackColor = PromptTheme.Panel,
            Location = new Point(12, 8),
        };
        var cpu = new RadioButton
        {
            Text = "CPU",
            AutoSize = true,
            Checked = !_savedGpu,
            FlatStyle = FlatStyle.Flat,
            ForeColor = PromptTheme.Ink,
            BackColor = PromptTheme.Panel,
            Location = new Point(12, 28),
        };
        _gpu = new RadioButton
        {
            Text = "GPU",
            AutoSize = true,
            Checked = _savedGpu,
            FlatStyle = FlatStyle.Flat,
            ForeColor = PromptTheme.Ink,
            BackColor = PromptTheme.Panel,
            Location = new Point(88, 28),
        };
        deviceRow.Controls.Add(caption);
        deviceRow.Controls.Add(cpu);
        deviceRow.Controls.Add(_gpu);

        _text = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = PromptTheme.Field,
            ForeColor = PromptTheme.Ink,
            Font = PromptTheme.Font,
            Text = _savedPrompt,
            HideSelection = false,
        };
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = PromptTheme.Panel,
        };
        var apply = PromptTheme.FlatButton("적용", 72);
        apply.Location = new Point(12, 8);
        apply.Click += (_, _) =>
        {
            Apply();
            Close();
        };
        bar.Controls.Add(apply);
        var field = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 0),
            BackColor = PromptTheme.Panel,
        };
        field.Controls.Add(_text);
        Controls.Add(field);
        Controls.Add(deviceRow);
        Controls.Add(bar);
    }

    private static string SavedValue(PluginSetting? setting)
    {
        try
        {
            return setting?.Read() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>Shows the window above everything (the game too) and takes the keyboard, so nobody has to Alt+Tab to find it.</summary>
    public void BringToTop()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        TopMost = true;
        Show();
        // Windows only lets the foreground process take focus; a tap of the Alt key lifts that limit for this call
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(Handle);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        BringToFront();
        Activate();
        _text.Focus();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Apply();
        base.OnFormClosing(e);
    }

    private void Apply()
    {
        var next = _text.Text.Trim();
        if (next.Length == 0)
        {
            next = TranslationPrompt.Default;
            _text.Text = next;
        }

        if (next != _savedPrompt)
        {
            _prompt.Write(next);
            _savedPrompt = next;
            TranslationPrompt.Apply(next);
        }

        var gpu = _gpu.Checked;
        if (gpu == _savedGpu) return;
        var value = gpu ? TranslationDevice.Gpu : TranslationDevice.Cpu;
        _device?.Write(value);
        _savedGpu = gpu;
        TranslationDevice.Apply(value);
    }
}
