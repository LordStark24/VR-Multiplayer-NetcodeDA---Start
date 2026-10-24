using UnityEngine;

public class BodyFollow : MonoBehaviour
{
    public Transform head;
    public float yOffset = -0.127f;

    void LateUpdate()
    {
        if (head == null)
            return;

        // Keep the body below the head.
        transform.position = head.position + Vector3.up * yOffset;

        // Follow the head's facing direction, keeping the body upright.
        Vector3 horizontalForward = head.forward;
        horizontalForward.y = 0f;

        if (horizontalForward.magnitude > 0.001f)
        {
            transform.rotation =
                Quaternion.LookRotation(horizontalForward.normalized);
        }
    }
}
