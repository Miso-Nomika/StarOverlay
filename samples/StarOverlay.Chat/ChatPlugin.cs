using StarOverlay.Framework;

namespace StarOverlay.Chat;

public sealed class ChatPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("chat", "채팅", "0.4.9");

    public void Load(IPluginContext context)
    {
        ChatGloss.Attach();
        TranslationPrompt.Apply(context.Settings.Get("translate-prompt"));
        TranslationDevice.Apply(context.Settings.Get("translate-device"));
        TranslationModel.Apply(context.Settings.Get("translate-model"));
        ChatGloss.SetIme(Flag(context, "show-ime"));
        ChatGloss.SetTranslation(Flag(context, "show-translation"));
        context.Controls.UseFrameButton(0, "\u2699\uFE0F", opensSettings: true);
        context.Controls.UsePromptButton(1, "\U0001F4DD");
        context.Controls.AddToggle("show-system", "시스템 메시지", true); // first: loot and notices in the chat
        context.Controls.AddToggle("show-ime", "IME 표시", true, ChatGloss.SetIme);
        context.Controls.AddToggle("show-translation", "번역문 표시", true, ChatGloss.SetTranslation);
        context.Controls.AddText("text-size", "채팅 크기", "9", 9, 32);
        context.Controls.AddText("line-gap", "줄 간격", "0", 0, 24);
        context.Controls.AddText("translate-prompt", "번역 프롬프트", TranslationPrompt.Default);
        context.Controls.AddText("translate-device", "연산", TranslationDevice.Cpu);
        context.Controls.AddText("translate-model", "번역 모델", "");
        context.Controls.AddButton("translate-download", "번역 도구 받기", TranslationModel.StartDownload);
        context.Controls.AddButton("translate-delete", "번역 모델 삭제", TranslationModel.DeleteInUse);
        context.Ui.AddChat("chat.lines", () => Lines(context), "채팅 없음", () => TextSize(context), () => LineGap(context));
    }

    private static IReadOnlyList<ChatLineView> Lines(IPluginContext context) =>
        Flag(context, "show-system") ? LiveChat.Lines : LiveChat.Lines.Where(line => line.Channel != "시스템").ToList();

    private static bool Flag(IPluginContext context, string key)
    {
        var text = context.Settings.Get(key);
        return text == null || text == "true";
    }

    private static int LineGap(IPluginContext context)
    {
        var text = context.Settings.Get("line-gap");
        if (!int.TryParse(text, out var value)) value = 0;
        return Math.Clamp(value, 0, 24);
    }

    private static int TextSize(IPluginContext context)
    {
        var text = context.Settings.Get("text-size");
        if (!int.TryParse(text, out var value)) value = 9;
        return Math.Clamp(value, 9, 32);
    }

    public void Unload()
    {
        ChatGloss.Detach(); // the translation model is released with the last chat plugin
    }
}
