using System;
using UnityEngine;

public static class SaveDebugActions
{
    public static bool TryEraseSave(out string error)
    {
        try
        {
            EraseSaveOrThrow();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            Debug.LogError("Erase Save failed: " + ex);
            return false;
        }
    }

    public static void EraseSaveOrThrow()
    {
        CPlayerPrefs.DeleteAll();
        CPlayerPrefs.Save();
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        if (Caching.ready)
        {
            Caching.ClearCache();
        }

        GameState.currentWorld = 0;
        GameState.currentSubWorld = 0;
        GameState.currentLevel = 0;
        GameState.unlockedWorld = -1;
        GameState.unlockedSubWord = -1;
        GameState.unlockedLevel = -1;
    }
}
