using UnityEngine;

public class PickupFloat : MonoBehaviour
{
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private float bobCyclesPerSecond = 0.5f;
    [SerializeField] private float rotationDegreesPerSecond = 15f;

    private Vector3 restingPosition;
    private Quaternion restingRotation;
    private float elapsed;

    private void Awake()
    {
        restingPosition = transform.localPosition;
        restingRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        elapsed = 0f;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        float bob = Mathf.Sin(
            elapsed * bobCyclesPerSecond * Mathf.PI * 2f) * bobHeight;

        transform.localPosition = restingPosition + Vector3.up * bob;

        transform.localRotation = restingRotation *
            Quaternion.Euler(0f, elapsed * rotationDegreesPerSecond, 0f);
    }
}