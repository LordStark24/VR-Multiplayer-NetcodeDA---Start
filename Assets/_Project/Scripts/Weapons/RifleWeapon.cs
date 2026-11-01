using UnityEngine;
using Unity.Netcode;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(XRGrabInteractable))]
public class RifleWeapon : NetworkBehaviour
{
    [SerializeField] private WeaponDefinition definition;
    [SerializeField] private Transform firePoint;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Temporary Testing")]
    [SerializeField] private bool startReadyForTesting;

    private MansionMatchController matchController;

    private NetworkVariable<int> currentAmmo = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public int Damage =>
    definition != null ? Mathf.Max(0, definition.damage) : 0;

    private NetworkVariable<bool> weaponActivated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> initialized = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private XRGrabInteractable grab;
    private bool triggerHeld;
    private float nextShotTime;

    public int CurrentAmmo => currentAmmo.Value;
    public bool CanReceiveAmmo =>
    IsSpawned &&
    IsOwner &&
    initialized.Value &&
    grab != null &&
    grab.isSelected &&
    CurrentAmmo < MagazineCapacity;

    public int MagazineCapacity =>
        definition != null ? definition.magazineCapacity : 0;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
    }

    public override void OnNetworkSpawn()
    {
        currentAmmo.OnValueChanged += OnAmmoChanged;

        if (definition == null || firePoint == null)
        {
            Debug.LogError(
                "[Weapon] Assign Definition and FirePoint.", this);
            return;
        }

        // Initialize once, not every time ownership changes.
        if (IsOwner && !initialized.Value)
        {
            weaponActivated.Value = startReadyForTesting;
            currentAmmo.Value = startReadyForTesting
                ? MagazineCapacity
                : 0;

            initialized.Value = true;
        }

        Debug.Log(
            $"[Weapon] Spawned | Ammo: " +
            $"{CurrentAmmo}/{MagazineCapacity}", this);
    }

    public override void OnNetworkDespawn()
    {
        currentAmmo.OnValueChanged -= OnAmmoChanged;
        StopFiring();
    }

    public override void OnLostOwnership()
    {
        StopFiring();
    }

    public void BeginFiring()
    {
        if (!IsSpawned || !IsOwner || !grab.isSelected)
            return;

        triggerHeld = true;
    }

    public void StopFiring()
    {
        triggerHeld = false;
    }

    private void OnDisable()
    {
        StopFiring();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || !initialized.Value ||
            definition == null || firePoint == null)
            return;

        if (!grab.isSelected)
        {
            StopFiring();
            return;
        }

        if (!LocalPlayerIsAlive() || !MatchIsPlaying())
        {
            StopFiring();
            return;
        }

        if (!triggerHeld || !weaponActivated.Value ||
            CurrentAmmo <= 0 || Time.time < nextShotTime)
            return;

        nextShotTime = Time.time +
            1f / Mathf.Max(0.1f, definition.fireRate);

        currentAmmo.Value--;

        Vector3 origin = firePoint.position;
        Vector3 direction = firePoint.forward;

        bool hitSomething = Physics.Raycast(
            origin,
            direction,
            out RaycastHit hit,
            definition.range,
            hitLayers,
            QueryTriggerInteraction.Ignore);

        if (hitSomething)
        {
            var targetHealth =
                hit.collider.GetComponentInParent<NetworkPlayerHealth>();

            if (targetHealth != null &&
                targetHealth.IsSpawned &&
                targetHealth.OwnerClientId != OwnerClientId)
            {
                targetHealth.RequestWeaponDamageRpc(
                    new NetworkObjectReference(NetworkObject));
            }
        }

        float distance = hitSomething
            ? hit.distance
            : definition.range;

        Debug.DrawRay(
            origin, direction * distance, Color.red, 0.2f);

        Debug.Log(
            $"[Weapon] Shot | Hit: " +
            $"{(hitSomething ? hit.collider.name : "nothing")} | " +
            $"Ammo: {CurrentAmmo}", this);
    }

    public bool RefillMagazine()
    {
        if (!IsSpawned || !IsOwner || !initialized.Value ||
            definition == null || CurrentAmmo >= MagazineCapacity)
            return false;

        currentAmmo.Value = MagazineCapacity;
        return true;
    }

    private void OnAmmoChanged(int previous, int current)
    {
        Debug.Log(
            $"[Weapon] Ammo: {previous} -> {current} | " +
            $"Owner: {OwnerClientId}", this);
    }

    [ContextMenu("Test / Refill Magazine")]
    private void TestRefillMagazine()
    {
        if (!RefillMagazine())
        {
            Debug.LogWarning(
                "[Weapon] Refill requires ownership and " +
                "a magazine that is not already full.", this);
        }
    }

    private bool MatchIsPlaying()
    {
        if (matchController == null)
        {
            matchController =
                Object.FindFirstObjectByType<MansionMatchController>();
        }

        return matchController != null &&
            matchController.IsSpawned &&
            matchController.CurrentPhase ==
                MansionMatchController.MatchPhase.Playing;
    }

    private bool LocalPlayerIsAlive()
    {
        var network = NetworkManager;

        if (network == null ||
            network.LocalClient == null ||
            network.LocalClient.PlayerObject == null)
            return false;

        var playerHealth = network.LocalClient.PlayerObject
            .GetComponent<NetworkPlayerHealth>();

        return playerHealth != null && playerHealth.IsAlive;
    }
}