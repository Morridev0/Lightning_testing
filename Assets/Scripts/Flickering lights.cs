using System.Collections;
using UnityEngine;

public class Flickeringlights : MonoBehaviour
{
    private Light light;


    [Header("Speed")]
    public float flickerSpeed = 0.1f;
    public float minSpeed = 0.1f;
    public float maxSpeed = 0.2f;


    [Header("Intensity")]
    public float minIntensity = .5f;
    public float maxIntensity = 5.0f;


    [Header("Range")]
    public float rangeSpeed = 0.1f;
    public float minRange = 5f;
    public float maxRange = 10f;



    private void Start()
    {
        light = GetComponent<Light>();

        StartFlicker();
    }

    private void StartFlicker()
    {
        flickerSpeed = Random.Range(minSpeed, maxSpeed);

        Invoke("StartFlicker", flickerSpeed);

        Flicker();
    }

    private void Flicker() {

        // Intensidad
        float randomIntensity = Random.Range(minIntensity, maxIntensity);
        light.intensity = randomIntensity;


        // Retocar::
        // Rango
        //float randomRange = Random.Range(minRange, maxRange);
        //light.range = randomRange;
    }
}
