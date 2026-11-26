using UnityEngine;
using Unity.Netcode;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class RifleChestHolster : NetworkBehaviour
{
    private Rigidbody body;
    private XRGrabInteractable grab;
    private Transform holster;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();

        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
    }

    public override void OnNetworkSpawn()
    {
        // Remote copies receive movement through NetworkTransform.
        if (!IsOwner)
        {
            body.useGravity = false;
            body.isKinematic = true;
        }
    }

    public void PlaceInHolster(Transform target)
    {
        if (!IsSpawned || !IsOwner || target == null ||
            grab.isSelected)
        {
            return;
        }

        holster = target;

        body.useGravity = false;
        body.isKinematic = true;

        FollowHolster();
    }

    private void LateUpdate()
    {
        if (!IsSpawned)
            return;

        if (!IsOwner)
        {
            if (!grab.isSelected)
            {
                body.useGravity = false;
                body.isKinematic = true;
            }

            return;
        }

        if (holster != null && !grab.isSelected)
            FollowHolster();
    }

    private void FollowHolster()
    {
        transform.SetPositionAndRotation(
            holster.position,
            holster.rotation);
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        // Stop following the chest. XR Grab controls movement now.
        holster = null;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        if (!IsSpawned || !IsOwner || grab.isSelected)
            return;

        GetComponent<RifleWeapon>()?.StopFiring();

        var rig = VRRigReferences.Instance;

        if (rig != null && rig.rifleHolster != null)
        {
            PlaceInHolster(rig.rifleHolster);
            return;
        }

        // Fall normally if the holster is unavailable.
        body.isKinematic = false;
        body.useGravity = true;
    }

    public override void OnLostOwnership()
    {
        holster = null;

        body.useGravity = false;
        body.isKinematic = true;
    }

    private void OnDestroy()
    {
        if (grab == null)
            return;

        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);
    }
}