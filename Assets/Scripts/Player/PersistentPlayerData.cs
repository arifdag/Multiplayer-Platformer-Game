using UnityEngine;

public static class PersistentPlayerData
{
    private const string UsernameKey = "PlayerUsername";
    public static string Username { get; private set; }

    public static void LoadUsername()
    {
        if (PlayerPrefs.HasKey(UsernameKey))
        {
            Username = PlayerPrefs.GetString(UsernameKey);
        }
        else
        {
            Username = GenerateRandomUsername();
            SaveUsername();
        }
    }

    public static void SetUsername(string newUsername)
    {
        Username = newUsername;
        SaveUsername();
    }

    private static void SaveUsername()
    {
        PlayerPrefs.SetString(UsernameKey, Username);
        PlayerPrefs.Save();
    }

    private static string GenerateRandomUsername()
    {
        // Adding a timestamp to reduce collision risk
        return $"Player{Random.Range(1000, 9999)}_{System.DateTime.UtcNow.Ticks % 10000}";
    }
}