using System.Collections.Generic;

[System.Serializable]
public class CrosswordConfig
{
    public string Answer;
    public int XPos;
    public int YPos;
    public int Direction; // 1 - вертикальное, 0 - горизонтальное
}

[System.Serializable]
public class CrosswordData
{
    public List<CrosswordConfig> CrosswordConfigs;
    public List<string> HiddenWords;
}
