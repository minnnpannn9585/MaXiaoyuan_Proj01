using UnityEngine;

public static class GameLanguage
{
    public static bool English { get; private set; }
    public static Font Font { get; private set; }
    public static void Load()
    {
        English = PlayerPrefs.GetInt("Interface.Language", Application.systemLanguage == SystemLanguage.Chinese ||
            Application.systemLanguage == SystemLanguage.ChineseSimplified || Application.systemLanguage == SystemLanguage.ChineseTraditional ? 0 : 1) == 1;
        Font = Resources.Load<Font>("Interface/InterfaceFont");
    }
    public static void SetEnglish(bool english)
    { English=english;PlayerPrefs.SetInt("Interface.Language",english?1:0);PlayerPrefs.Save(); }
    public static string Text(string chinese,string english) { return English?english:chinese; }
}
