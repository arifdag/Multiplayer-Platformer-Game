using UnityEngine;

public class BounceFeature : MonoBehaviour, IPlaceableFeature
{
    [Tooltip("How high to launch the player")] [SerializeField]
    private float bounceStrength = 10f;

    public void OnPlayerEnter(GameObject player)
    {
        LaunchPlayer(player);
    }

    private void LaunchPlayer(GameObject player)
    {
        // Try to get NetworkPlayerController first (for multiplayer)
        NetworkPlayerController networkController = player.GetComponent<NetworkPlayerController>();
        if (networkController != null)
        {
            networkController.Launch(Vector3.up * bounceStrength);
            return;
        }

        // Fallback to regular PlayerController (for single player)
        PlayerController pc = player.GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.Launch(Vector3.up * bounceStrength);
        }
    }

    public void OnPlayerStay(GameObject player)
    {
    }

    public void OnPlayerExit(GameObject player)
    {
    }
}