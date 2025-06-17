using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class PlayerEntryUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image colorSwatch;

    public void Initialize(Player l)
    {
        nameText.text = l.Data["PlayerName"].Value;
        int idx       = int.Parse(l.Data["PlayerColor"].Value);
        colorSwatch.gameObject.SetActive(idx >= 0);
        if (idx >= 0)
            colorSwatch.color = LobbyUIManager.Instance.colorButtons[idx].GetComponent<Image>().color;
    }
}

