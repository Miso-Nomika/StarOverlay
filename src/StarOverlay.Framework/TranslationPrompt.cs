namespace StarOverlay.Framework;

public static class TranslationPrompt
{
    /// <summary>
    /// Rules plus a game-word list for the small local model. Tested on real chat lines: the list fixes class names
    /// (居合=발도), party slang (野良, 周回, 火力, D/T/H) and the single polite speech level keeps lines readable.
    /// </summary>
    public const string Default = """
        아래 규칙으로 일본어 게임 채팅을 한국어로 번역한다.
        - 번역문만 한 줄로 출력한다. 설명, 따옴표, 이모지, 이모티콘은 쓰지 않는다.
        - 원문에 없는 내용을 더하거나 빼지 않는다. 숫자와 이름은 그대로 둔다.
        - 말투는 '~요' 체의 자연스러운 구어로 통일한다.
        - 웃음(w, 草)은 ㅋㅋ로 옮기고 ㅋ은 최대 4개만 쓴다. 원문에 웃음 표현이 없으면 ㅋㅋ를 붙이지 않는다.
        게임 단어 사전:
        威咲=치유, 狂音=음파, 響=협주, 響奏=협주, 森=숲, 若葉=새싹(초보자), 双=쌍도끼, 月影=월광, 狂奏=음파, 猟場=사냥터, 墓=무덤, 工場=공장, ナイトメア=나이트메어, 槍=창, D=딜러, T=탱커, H=힐러, 自認D=자칭 딜러, 配信中=방송 중, 巨塔=거탑, イサキ=심판, ギルハン=길드사냥, @=모집(예시 @たくさん, @いっぱい=많이 모집), 〆=마감, ch=채널, VC=음성챗, 弓=활, 鷹=매, 狼=늑대
        居合=발도, 火力=딜러, 野良=랜덤 파티(모르는 사람들과 하는 파티), 周回=반복 플레이, 枠=자리, 空き=빈자리, 募集=모집, ギルドセンター=길드 센터, 回線落ち=접속 끊김, 固定=고정 파티, 初心者=초보자, 乙=수고했어요
        """;

    private static int _generation;

    public static string Current { get; private set; } = Default;

    public static int Generation => Volatile.Read(ref _generation);

    public static event Action? Changed;

    public static void Apply(string? value)
    {
        var next = string.IsNullOrWhiteSpace(value) ? Default : value.Trim();
        if (next == Current) return;
        Current = next;
        Interlocked.Increment(ref _generation);
        Changed?.Invoke();
    }
}
