using UnityEngine;
using UnityEngine.EventSystems; 

public class ItemSelector : MonoBehaviour
{
    [Header("Setup References")]
    [Tooltip("The parent Canvas GameObject that holds the item display and selector. This script should be on or under this canvas.")]
    public Canvas parentCanvas; 

    [Tooltip("Reference to your NetworkGameManager or script that handles starting the placement phase.")]
    public NetworkGameManager networkGameManager;
    
    [Tooltip("Reference to your ItemDisplayer or script that handles starting the placement phase.")]
    public ItemDisplayer itemDisplayer;

    [Header("Interaction Settings")]
    [Tooltip("The layer mask containing only the selectable items.")]
    public LayerMask selectableItemLayer;

    [Tooltip("Color to use for highlighting the item under the cursor (uses Emission).")]
    public Color highlightColor = Color.yellow;

    [Tooltip("Intensity of the emission highlight.")]
    [Min(0f)]
    public float highlightEmissionIntensity = 1.0f;
    
    
    private Camera mainCamera;
    private GameObject currentlyHoveredItem = null;
    private Renderer hoveredItemRenderer = null;
    private Color originalEmissionColor;
    private bool emissionWasEnabled = false;

    void Awake()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("ItemSelector: No Main Camera found. Please tag your camera.", this);
            enabled = false; // Disable script if no camera
            return;
        }
        
        // Try to find parentCanvas if not assigned by searching upwards
        if (parentCanvas == null)
        {
            parentCanvas = GetComponentInParent<Canvas>();
        }
         if (parentCanvas == null)
        {
            Debug.LogError("ItemSelector: Parent Canvas is not assigned and couldn't be found in parents.", this);
            enabled = false;
            return;
        }

         if (networkGameManager == null)
        {
             // Attempt to find it if not assigned
             networkGameManager = NetworkGameManager.Instance;
             if (networkGameManager == null)
             {
                Debug.LogError("ItemSelector: NetworkGameManager is not assigned and couldn't be found via Singleton.", this);
                enabled = false;
                return;
             }
        }
    }

    void OnEnable()
    {
        UnhighlightCurrent(); // Make sure nothing is highlighted initially
    }

    void OnDisable()
    {
        // Clean up when the selection UI is disabled (or this component is disabled)
        UnhighlightCurrent();
    }


    void Update()
    {
        // Only allow selection if NetworkGameManager is in ItemSelection phase
        if (networkGameManager == null || networkGameManager.CurrentPhase != NetworkGameManager.GamePhase.ItemSelection)
        {
            if (currentlyHoveredItem != null) UnhighlightCurrent(); // Unhighlight if phase changes
            return;
        }
        if (mainCamera == null) return;


        // Prevent interaction if mouse is over UI elements (like buttons, etc. on the same canvas or higher)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            // This check is primarily for UI elements *other than* the 3D items themselves.
             UnhighlightCurrent(); // Unhighlight if mouse moves over UI
             return; 
        }


        // Raycasting for Items
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        GameObject objectHitThisFrame = null;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, selectableItemLayer))
        {
            objectHitThisFrame = hit.collider.gameObject;
            // Ensure the hit object or its parent has ItemIdentifier (if items are complex prefabs)
            ItemIdentifier hitIdentifier = objectHitThisFrame.GetComponentInParent<ItemIdentifier>();


            if (hitIdentifier != null && hitIdentifier.gameObject != currentlyHoveredItem)
            {
                UnhighlightCurrent();
                HighlightItem(hitIdentifier.gameObject); // Highlight the root object with ItemIdentifier
            }
            else if (hitIdentifier == null && currentlyHoveredItem != null) // Moved off an item or onto something non-selectable
            {
                 UnhighlightCurrent();
            }
        }
        else
        {
            UnhighlightCurrent();
        }

        // Selection Input
        if (Input.GetMouseButtonDown(0) && currentlyHoveredItem != null) // Left mouse click while hovering
        {
            SelectItem(currentlyHoveredItem);
        }
    }

    void HighlightItem(GameObject itemToHighlight)
    {
        // Try to get renderer from children first, then on the item itself.
        hoveredItemRenderer = itemToHighlight.GetComponentInChildren<Renderer>();
        if (hoveredItemRenderer == null) hoveredItemRenderer = itemToHighlight.GetComponent<Renderer>();


        if (hoveredItemRenderer != null && hoveredItemRenderer.material.HasProperty("_EmissionColor"))
        {
            originalEmissionColor = hoveredItemRenderer.material.GetColor("_EmissionColor");
            emissionWasEnabled = hoveredItemRenderer.material.IsKeywordEnabled("_EMISSION");

            hoveredItemRenderer.material.EnableKeyword("_EMISSION");
            hoveredItemRenderer.material.SetColor("_EmissionColor", highlightColor * highlightEmissionIntensity);
            currentlyHoveredItem = itemToHighlight;
        }
        else
        {
             currentlyHoveredItem = itemToHighlight; // Still set as hovered, even if not visually highlighted
             hoveredItemRenderer = null; // Ensure no attempt to unhighlight a non-existent renderer state
        }
    }

    void UnhighlightCurrent()
    {
        if (currentlyHoveredItem != null && hoveredItemRenderer != null && hoveredItemRenderer.material.HasProperty("_EmissionColor"))
        {
            hoveredItemRenderer.material.SetColor("_EmissionColor", originalEmissionColor);
            if (!emissionWasEnabled)
            {
                hoveredItemRenderer.material.DisableKeyword("_EMISSION");
            }
        }
        currentlyHoveredItem = null;
        hoveredItemRenderer = null;
    }

    
    void SelectItem(GameObject selectedItemInstance)
    {
        UnhighlightCurrent(); // Unhighlight before proceeding
        itemDisplayer.ClearPreviousItems();

        ItemIdentifier identifier = selectedItemInstance.GetComponent<ItemIdentifier>();
        if (identifier != null && identifier.itemPrefab != null)
        {
            GameObject selectedPrefab = identifier.itemPrefab;
            
            // Get the clean prefab name (remove any instance suffixes)
            string cleanPrefabName = selectedPrefab.name;
            
            // Remove common suffixes that might be added during instantiation
            if (cleanPrefabName.EndsWith("_SelectionInstance"))
            {
                cleanPrefabName = cleanPrefabName.Replace("_SelectionInstance", "");
            }
            if (cleanPrefabName.EndsWith("(Clone)"))
            {
                cleanPrefabName = cleanPrefabName.Replace("(Clone)", "");
            }
            
            // Trim any whitespace
            cleanPrefabName = cleanPrefabName.Trim();
            
            if (networkGameManager != null)
            {
                // NetworkGameManager will handle network communication for item selection
                networkGameManager.SelectItemServerRpc(cleanPrefabName);
            }
            else {
                 Debug.LogError("ItemSelector: NetworkGameManager reference is missing, cannot start placement phase!");
            }
        }
        else
        {
            Debug.LogError($"ItemSelector: SelectedItem '{selectedItemInstance.name}' is missing ItemIdentifier or its itemPrefab reference!", selectedItemInstance);
        }
    }
}