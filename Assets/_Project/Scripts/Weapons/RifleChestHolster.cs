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
    private NetworkVariable<ulong> startingPlayerId = new(
    ulong.MaxValue,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Owner);

    private bool IsMyStartingRifle =>
        IsSpawned &&
        IsOwner &&
        startingPlayerId.Value == NetworkManager.LocalClientId;
    private Collider[] rifleColliders;
    private CharacterController ignoredController;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        rifleColliders = GetComponentsInChildren<Collider>(true);

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

    private void UpdatePlayerCollisionExclusion()
    {
        CharacterController controller = null;

        if (IsOwner && VRRigReferences.Instance != null)
        {
            var rig = VRRigReferences.Instance;

            controller = rig.GetComponentInParent<CharacterController>();

            if (controller == null)
            {
                controller =
                    rig.GetComponentInChildren<CharacterController>(true);
            }
        }

        if (ignoredController != controller)
        {
            RestorePlayerCollisions();
            ignoredController = controller;
        }

        if (ignoredController == null ||
            !ignoredController.enabled ||
            !ignoredController.gameObject.activeInHierarchy)
        {
            return;
        }

        foreach (var rifleCollider in rifleColliders)
        {
            if (rifleCollider == null ||
                !rifleCollider.enabled ||
                !rifleCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!Physics.GetIgnoreCollision(
                rifleCollider, ignoredController))
            {
                Physics.IgnoreCollision(
                    rifleCollider, ignoredController, true);
            }
        }
    }

    private void RestorePlayerCollisions()
    {
        if (ignoredController != null && rifleColliders != null)
        {
            foreach (var rifleCollider in rifleColliders)
            {
                if (rifleCollider != null)
                {
                    Physics.IgnoreCollision(
                        rifleCollider, ignoredController, false);
                }
            }
        }

        ignoredController = null;
    }

    public override void OnNetworkDespawn()
    {
        RestorePlayerCollisions();
        base.OnNetworkDespawn();
    }
    public void PlaceInHolster(Transform target)
    {
        if (!IsMyStartingRifle || target == null || grab.isSelected)
            return;

        holster = target;

        body.useGravity = false;
        body.isKinematic = true;

        FollowHolster();
    }

    private void LateUpdate()
    {
        if (!IsSpawned)
            return;

        UpdatePlayerCollisionExclusion();

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
    public void AssignStartingPlayer(ulong clientId)
    {
        if (!IsSpawned || !IsOwner ||
            startingPlayerId.Value != ulong.MaxValue)
        {
            return;
        }

        startingPlayerId.Value = clientId;
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

        if (IsMyStartingRifle &&
            rig != null &&
            rig.rifleHolster != null)
        {
            PlaceInHolster(rig.rifleHolster);
            return;
        }

        // Found weapons and another player's rifle drop normally.
        holster = null;
        body.isKinematic = false;
        body.useGravity = true;
    }

    public override void OnLostOwnership()
    {
        RestorePlayerCollisions();
        holster = null;

        body.useGravity = false;
        body.isKinematic = true;
    }

    private void OnDestroy()
    {
        RestorePlayerCollisions();
        if (grab == null)
            return;

        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);
    }
}