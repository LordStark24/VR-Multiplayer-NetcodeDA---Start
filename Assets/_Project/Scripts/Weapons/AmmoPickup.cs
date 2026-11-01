using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(SphereCollider))]
public class AmmoPickup : NetworkBehaviour
{
    [SerializeField] private GameObject pickupVisual;

    [SerializeField, Min(1f)]
    private float respawnSeconds = 60f;

    private NetworkVariable<double> availableAt = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private SphereCollider pickupTrigger;

    private float nextCollectionCheck;
    private uint collectionNumber;
    private ulong collectingClientId;

    public bool IsAvailable =>
        IsSpawned &&
        NetworkManager.ServerTime.Time >= availableAt.Value;

    private void Awake()
    {
        pickupTrigger = GetComponent<SphereCollider>();
    }

    public override void OnNetworkSpawn()
    {
        UpdateAppearance();
    }

    private void Update()
    {
        if (IsSpawned)
            UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        bool available = IsAvailable;

        pickupTrigger.enabled = available;

        if (pickupVisual != null &&
            pickupVisual.activeSelf != available)
        {
            pickupVisual.SetActive(available);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsAvailable || Time.time < nextCollectionCheck)
            return;

        // Only the local VR rig requests collection.
        var rig = other.GetComponentInParent<VRRigReferences>();

        if (rig == null || rig != VRRigReferences.Instance)
            return;

        nextCollectionCheck = Time.time + 0.5f;

        var weapons = Object.FindObjectsByType<RifleWeapon>(
            FindObjectsSortMode.None);

        foreach (var weapon in weapons)
        {
            if (!weapon.CanReceiveAmmo)
                continue;

            RequestCollectionRpc(
                new NetworkObjectReference(weapon.NetworkObject));

            break;
        }
    }

    [Rpc(SendTo.Owner, RequireOwnership = false)]
    private void RequestCollectionRpc(
        NetworkObjectReference weaponReference,
        RpcParams rpcParams = default)
    {
        if (!IsOwner || !IsAvailable)
            return;

        if (!weaponReference.TryGet(out NetworkObject weaponObject))
            return;

        ulong requester = rpcParams.Receive.SenderClientId;

        if (weaponObject.OwnerClientId != requester)
            return;

        var weapon = weaponObject.GetComponent<RifleWeapon>();

        if (weapon == null ||
            weapon.CurrentAmmo >= weapon.MagazineCapacity)
            return;

        // Basic proximity check using the shared rifle position.
        if (Vector3.Distance(
            weapon.transform.position, transform.position) > 3f)
            return;

        collectingClientId = requester;
        collectionNumber++;

        // Reserve immediately so another request cannot also succeed.
        availableAt.Value =
            NetworkManager.ServerTime.Time + respawnSeconds;

        UpdateAppearance();

        GrantCollectionRpc(
            weaponReference, requester, collectionNumber);
    }

    [Rpc(SendTo.Everyone, RequireOwnership = true)]
    private void GrantCollectionRpc(
        NetworkObjectReference weaponReference,
        ulong collectorId,
        uint number)
    {
        if (NetworkManager.LocalClientId != collectorId)
            return;

        bool refilled = false;

        if (weaponReference.TryGet(out NetworkObject weaponObject))
        {
            var weapon = weaponObject.GetComponent<RifleWeapon>();

            if (weapon != null && weapon.CanReceiveAmmo)
            {
                refilled = weapon.RefillMagazine();
            }
        }

        ConfirmCollectionRpc(number, refilled);
    }

    [Rpc(SendTo.Owner, RequireOwnership = false)]
    private void ConfirmCollectionRpc(
        uint number,
        bool refilled,
        RpcParams rpcParams = default)
    {
        if (!IsOwner ||
            number != collectionNumber ||
            rpcParams.Receive.SenderClientId != collectingClientId)
            return;

        if (!refilled)
        {
            // For example, the player dropped the rifle before approval.
            availableAt.Value = 0d;
            UpdateAppearance();
            return;
        }

        Debug.Log(
            $"[Ammo] Collected by client {collectingClientId}. " +
            $"Respawns in {respawnSeconds} seconds.", this);
    }

    [ContextMenu("Test / Consume Pickup")]
    private void TestConsumePickup()
    {
        if (!IsSpawned || !IsOwner || !IsAvailable)
        {
            Debug.LogWarning(
                "[Ammo] Run this test on the pickup's owning player " +
                "while the pickup is available.", this);
            return;
        }

        availableAt.Value =
            NetworkManager.ServerTime.Time + respawnSeconds;

        UpdateAppearance();

        Debug.Log(
            $"[Ammo] Consumed. Returns in {respawnSeconds} seconds.",
            this);
    }
}