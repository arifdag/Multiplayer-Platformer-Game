using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DoorLightManager : MonoBehaviour
{
    [Header("Light Settings")]
    [SerializeField] private Light spotlight;

    [Tooltip("Minimum intensity of the spotlight.")]
    [SerializeField] private float minIntensity = 0.5f;

    [Tooltip("Maximum intensity of the spotlight.")]
    [SerializeField] private float maxIntensity = 5.0f;

    [Tooltip("Time in seconds for one full cycle (min -> max -> min).")]
    [SerializeField] private float duration = 2.0f;

    
    void Start()
    {
        if (spotlight == null)
        {
            Debug.LogError("Spotlight is not assigned in the Inspector!", this);
            enabled = false;
        }
    }
    
    void Update()
    {
        if (duration <= 0f)
        {
            if (spotlight != null)
            {
                spotlight.intensity = minIntensity;
            }
            return;
        }
        
        float lerpFactor = Mathf.PingPong(Time.time * (2.0f / duration), 1.0f);
        float currentIntensity = Mathf.Lerp(minIntensity, maxIntensity, lerpFactor);
        spotlight.intensity = currentIntensity;
    }
}