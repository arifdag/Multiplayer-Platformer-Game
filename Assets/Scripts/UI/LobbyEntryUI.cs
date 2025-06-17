using System;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyEntryUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private Button joinButton;

    public void Initialize(Lobby l, Action onJoin)
    {
        nameText.text    = l.Name;
        playersText.text = $"{l.Players.Count}/{l.MaxPlayers}";
        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(() => onJoin());
    }
}

