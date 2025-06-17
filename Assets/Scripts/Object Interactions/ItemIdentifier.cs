using UnityEngine;


// It helps the ItemSelector identify which original prefab was selected
// when the player clicks on an instance of it.
public class ItemIdentifier : MonoBehaviour
{
    [Tooltip("Drag the Prefab asset itself here.")]
    public GameObject itemPrefab;

    void Awake()
    {
        // Basic check to ensure it's assigned in the prefab inspector
        if (itemPrefab == null)
        {
            Debug.LogError($"ItemIdentifier on '{this.gameObject.name}' is missing its 'itemPrefab' reference!", this.gameObject);
        }
    }
}