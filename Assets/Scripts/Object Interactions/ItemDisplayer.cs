using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ItemDisplayer : MonoBehaviour
{
    [Header("Item Setup")] [Tooltip("List of all possible item Prefabs to choose from.")]
    public List<GameObject> availableItemPrefabs;

    [Tooltip("How many items should be displayed for selection.")]
    public int numberOfItemsToShow = 3;

    [Header("Placement Settings")] [Tooltip("The fixed Z distance from the camera where items will be placed.")]
    public float placementDepth = 2.5f;

    [Range(0.1f, 1.0f)] [Tooltip("How much of the screen width the items should generally spread across (0.8 = 80%).")]
    public float horizontalSpreadPercentage = 0.8f;

    [Range(0.0f, 1.0f)] [Tooltip("The central vertical position on screen (0 = bottom, 0.5 = middle, 1 = top).")]
    public float verticalCenterPercentage = 0.5f;

    [Header("Randomization Settings")]
    [Range(0.0f, 0.5f)]
    [Tooltip(
        "Maximum random horizontal offset from the base position, as a percentage of screen width (0.1 = +/- 10% width).")]
    public float horizontalJitter = 0.1f;

    [Range(0.0f, 0.5f)]
    [Tooltip(
        "Maximum random vertical offset from the central vertical position, as a percentage of screen height (0.1 = +/- 10% height).")]
    public float verticalJitter = 0.1f;


    private Camera mainCamera;
    private List<GameObject> currentlyShownItems = new List<GameObject>();

    void Awake()
    {
        // Find the main camera in the scene
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("ItemDisplayer: No main camera found in the scene! Please tag your main camera.");
        }
    }
    

    // Show a new set of items
    public void DisplayNewItems()
    {
        if (mainCamera == null)
        {
            Debug.LogError("ItemDisplayer: Cannot display items, main camera is missing.");
            return;
        }

        // Clear any previously shown items
        ClearPreviousItems();

        // Validate inputs
        if (availableItemPrefabs == null || availableItemPrefabs.Count == 0)
        {
            Debug.LogWarning("ItemDisplayer: No available item prefabs assigned.");
            return;
        }

        int count = Mathf.Min(numberOfItemsToShow, availableItemPrefabs.Count);
        if (count <= 0)
        {
            Debug.LogWarning("ItemDisplayer: Number of items to show is zero or less.");
            return;
        }

        // Select random unique items
        List<GameObject> selectedPrefabs = GetRandomItems(count);

        // Calculate screen boundaries for placement
        float screenMinX = Screen.width * (0.5f - horizontalSpreadPercentage / 2f);
        float screenMaxX = Screen.width * (0.5f + horizontalSpreadPercentage / 2f);
        float screenCenterY = Screen.height * verticalCenterPercentage;

        // Calculate max jitter in pixels
        float maxHorizontalOffset = Screen.width * horizontalJitter;
        float maxVerticalOffset = Screen.height * verticalJitter;


        // Place the selected items with random offsets
        for (int i = 0; i < selectedPrefabs.Count; i++)
        {
            GameObject prefabToPlace = selectedPrefabs[i];
            if (prefabToPlace == null)
            {
                Debug.LogWarning($"ItemDisplayer: availableItemPrefabs contains a null entry. Skipping.");
                continue;
            }

            if (prefabToPlace.GetComponent<ItemIdentifier>() == null)
            {
                Debug.LogError(
                    $"ItemDisplayer: Prefab '{prefabToPlace.name}' is missing ItemIdentifier script. It cannot be selected.",
                    prefabToPlace);
            }


            // Calculate BASE screen position (evenly distributed)
            float baseScreenX;
            if (count == 1)
            {
                baseScreenX = Screen.width * 0.5f; // Center if only one item
            }
            else
            {
                float horizontalFraction = (count > 1) ? (float)i / (count - 1) : 0.5f; // Range 0 to 1
                baseScreenX = Mathf.Lerp(screenMinX, screenMaxX, horizontalFraction);
            }

            float baseScreenY = screenCenterY;

            // Add random jitter (offset)
            float randomOffsetX = Random.Range(-maxHorizontalOffset, maxHorizontalOffset);
            float randomOffsetY = Random.Range(-maxVerticalOffset, maxVerticalOffset);

            float finalScreenX = baseScreenX + randomOffsetX;
            float finalScreenY = baseScreenY + randomOffsetY;

            // Create the final screen position vector
            Vector3 screenPosition = new Vector3(finalScreenX, finalScreenY, placementDepth);

            // Convert screen position to world position
            Vector3 worldPosition = mainCamera.ScreenToWorldPoint(screenPosition);

            // Generate a random rotation
            Quaternion randomRotation = Random.rotation;

            // Instantiate the item
            GameObject newItem = Instantiate(prefabToPlace, worldPosition, randomRotation);
            newItem.name = prefabToPlace.name + "_SelectionInstance";

            // Ensure the ItemIdentifier on the instantiated item points to the original prefab
            ItemIdentifier identifier = newItem.GetComponent<ItemIdentifier>();
            if (identifier != null)
            {
                identifier.itemPrefab = prefabToPlace; }
            else
            {
                Debug.LogWarning($"ItemDisplayer: No ItemIdentifier found on instantiated item '{newItem.name}'. Adding one.");
                // Add ItemIdentifier if it doesn't exist
                identifier = newItem.AddComponent<ItemIdentifier>();
                identifier.itemPrefab = prefabToPlace;
            }

            // Keep track of the instantiated item
            currentlyShownItems.Add(newItem);
        }
    }

    // Helper method to select unique random items from the list
    private List<GameObject> GetRandomItems(int count)
    {
        // Filter out any nulls before shuffling
        List<GameObject> validPrefabs = availableItemPrefabs.Where(p => p != null).ToList();
        if (validPrefabs.Count == 0) return new List<GameObject>();

        int actualCount = Mathf.Min(count, validPrefabs.Count);
        return validPrefabs.OrderBy(x => System.Guid.NewGuid()).Take(actualCount).ToList();
    }


    // Clears items currently shown on screen
    public void ClearPreviousItems()
    {
        foreach (GameObject item in currentlyShownItems)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }

        currentlyShownItems.Clear(); // Clear the list
    }
}