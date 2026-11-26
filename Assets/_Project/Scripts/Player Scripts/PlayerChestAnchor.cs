using UnityEngine;

public class PlayerChestAnchor : MonoBehaviour
{
    [SerializeField] private Transform head;

    [Tooltip("Position relative to the headset's horizontal facing.")]
    [SerializeField]
    private Vector3 chestOffset =
        new Vector3(0f, -0.25f, 0.1f);

    private void LateUpdate()
    {
        if (head == null)
            return;

        Quaternion facing = Quaternion.Euler(
            0f, head.eulerAngles.y, 0f);

        transform.SetPositionAndRotation(
            head.position + facing * chestOffset,
            facing);
    }
}