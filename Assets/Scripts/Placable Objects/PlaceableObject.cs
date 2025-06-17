using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PlaceableObject : MonoBehaviour
{
    private IPlaceableFeature[] features;

    void Awake()
    {
        // Grab the features added in Inspector
        features = GetComponents<IPlaceableFeature>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            foreach (var f in features)
                f.OnPlayerEnter(other.gameObject);
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player"))
            foreach (var f in features)
                f.OnPlayerStay(other.gameObject);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            foreach (var f in features)
                f.OnPlayerExit(other.gameObject);
    }
}

