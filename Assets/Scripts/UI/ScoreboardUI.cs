using System.Collections;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class ScoreboardUI : MonoBehaviour
{
    [SerializeField] private Transform entriesContainer; // ScoreboardPanel’s transform
    [SerializeField] private GameObject scoreEntryPrefab; // ScoreEntryUI prefab

    private ScoreManager scoreManager;
    

    private IEnumerator Start()
    {
        // Wait for the network-spawned ScoreManager
        while (ScoreManager.Instance == null)
            yield return null;

        scoreManager = ScoreManager.Instance;
        // Subscribe before we check the list
        scoreManager.Scores.OnListChanged += OnScoresChanged;

        // Now wait until the server-populated list actually arrives
        yield return new WaitUntil(() => scoreManager.Scores.Count > 0);


        RefreshAll();
    }

    private void OnDestroy()
    {
        if (scoreManager != null)
            scoreManager.Scores.OnListChanged -= OnScoresChanged;
    }


    private void OnScoresChanged(NetworkListEvent<ScoreManager.PlayerStars> _)
        => RefreshAll();

    private void RefreshAll()
    {
        // Clear old entries
        foreach (Transform t in entriesContainer) Destroy(t.gameObject);

        int maxStars = scoreManager.FinishScore.Value;

        foreach (var ps in scoreManager.Scores)
        {
            // Try to grab the synced name on their NetworkObject:
            string name = $"Player {ps.clientId}";
            var client = NetworkManager.Singleton.ConnectedClientsList
                .FirstOrDefault(c => c.ClientId == ps.clientId);
            if (client != null && client.PlayerObject != null)
            {
                var lobbyState = client.PlayerObject.GetComponent<LobbyPlayerState>();
                if (lobbyState != null)
                {
                    // Only use the net-var if it’s non-empty
                    string netName = lobbyState.PlayerNameNet.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(netName))
                        name = netName;
                }
            }

            var go = Instantiate(scoreEntryPrefab, entriesContainer);
            var entry = go.GetComponent<ScoreEntryUI>();
            entry.Setup(name, ps.stars, maxStars);
        }
    }
}