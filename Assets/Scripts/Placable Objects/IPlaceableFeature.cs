using UnityEngine;

public interface IPlaceableFeature
{
    // Called when the player enters the trigger
    void OnPlayerEnter(GameObject player);
    // Called each frame the player stays on it
    void OnPlayerStay  (GameObject player);
    // Called when the player leaves
    void OnPlayerExit  (GameObject player);
}

