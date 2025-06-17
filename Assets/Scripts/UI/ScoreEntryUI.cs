using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ScoreEntryUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private Transform starsContainer;
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private Sprite filledStarSprite;
    [SerializeField] private Sprite emptyStarSprite;

    public void Setup(string playerName, int currentStars, int maxStars)
    {
        playerNameText.text = playerName;

        // Clear old stars
        foreach (Transform c in starsContainer) Destroy(c.gameObject);

        // Spawn exactly maxStars star images
        for (int i = 0; i < maxStars; i++)
        {
            var go = Instantiate(starPrefab, starsContainer);
            var img = go.GetComponent<Image>();
            img.sprite = (i < currentStars) ? filledStarSprite : emptyStarSprite;
        }
    }
}