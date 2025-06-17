using UnityEngine;

public class HazardObjectFeature : MonoBehaviour, IPlaceableFeature
{
    [Header("Rotation Settings")]
    [Tooltip("Axis around which this object will rotate (in local space).")]
    public Vector3 rotationAxis = Vector3.up;
    [Tooltip("Degrees per second to rotate around the axis.")]
    public float rotationSpeed = 90f;
    
    
    void Update()
    {
        transform.Rotate(
            rotationAxis.normalized,
            rotationSpeed * Time.deltaTime,
            Space.Self
        );
    }

    public void OnPlayerEnter(GameObject player)
    {
        var net = player.GetComponent<NetworkPlayerController>();
        // only the local‐owner should run Die()
        if (net == null || !net.IsLocalPlayer()) return;

        var death = player.GetComponent<PlayerDeathHandler>();
        if (death != null) death.Die();
    }

    public void OnPlayerStay(GameObject player)
    {
    }

    public void OnPlayerExit(GameObject player)
    {
    }
}
