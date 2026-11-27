using UnityEngine;
using Unity.Netcode;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(XRGrabInteractable))]
public class RifleWeapon : NetworkBehaviour
{
    [Header("Shot Effects")]
    [SerializeField] private GameObject muzzleFlashRoot;

    [SerializeField] private WeaponDefinition definition;
    [SerializeField] private Transform firePoint;
    [SerializeField] private TMP_Text ammoDisplay;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Impact Effects")]
    [SerializeField] private GameObject impactPrefab;
    [SerializeField, Min(0.1f)] private float impactLifetime = 3f;
    [SerializeField] private Vector3 impactRotationOffset;



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
    private int burstShotsRemaining;
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
            weaponActivated.Value = true;
            currentAmmo.Value = startReadyForTesting
                ? MagazineCapacity
                : 0;

            initialized.Value = true;
        }

        RefreshAmmoDisplay(CurrentAmmo);

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
        if (!IsSpawned || !IsOwner ||
            !initialized.Value ||
            definition == null ||
            !grab.isSelected ||
            triggerHeld)
        {
            return;
        }

        triggerHeld = true;
        Debug.Log(
    $"[Weapon Input] Rifle: {name} | " +
    $"Definition: {definition.name} | " +
    $"Shots per pull: {definition.shotsPerTriggerPull} | " +
    $"Ammo: {CurrentAmmo}", this);

        // Another press cannot add shots to an unfinished burst.
        if (burstShotsRemaining > 0)
            return;

        if (!weaponActivated.Value ||
            CurrentAmmo <= 0 ||
            !LocalPlayerIsAlive() ||
            !MatchIsPlaying())
        {
            return;
        }

        if (definition.shotsPerTriggerPull > 0)
        {
            burstShotsRemaining = Mathf.Min(
                definition.shotsPerTriggerPull,
                CurrentAmmo);
        }
    }

    // Trigger release stops automatic fire but lets a burst finish.
    public void EndFiringInput()
    {
        Debug.Log($"[Weapon Input] Trigger released: {name}", this);
        triggerHeld = false;
    }

    // Dropping, holstering, death, or ownership loss cancels all fire.
    public void StopFiring()
    {
        if (triggerHeld || burstShotsRemaining > 0)
        {
            Debug.Log($"[Weapon Input] Firing cancelled: {name}", this);
        }
        triggerHeld = false;
        burstShotsRemaining = 0;
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

        bool shotRequested = definition.shotsPerTriggerPull == 0
    ? triggerHeld
    : burstShotsRemaining > 0;

        if (!shotRequested || !weaponActivated.Value ||
            CurrentAmmo <= 0 || Time.time < nextShotTime)
        {
            return;
        }

        nextShotTime = Time.time +
            1f / Mathf.Max(0.1f, definition.fireRate);

        currentAmmo.Value--;
        PlayMuzzleFlashRpc();
        if (burstShotsRemaining > 0)
            burstShotsRemaining--;

        if (CurrentAmmo <= 0)
            burstShotsRemaining = 0;

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
            PlayImpactRpc(hit.point, hit.normal);

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
        RefreshAmmoDisplay(current);

        Debug.Log(
            $"[Weapon] Ammo: {previous} -> {current} | " +
            $"Owner: {OwnerClientId}", this);
    }

    private void RefreshAmmoDisplay(int rounds)
    {
        if (ammoDisplay != null)
            ammoDisplay.text = rounds.ToString();
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

    [Rpc(SendTo.Everyone, RequireOwnership = true)]
    private void PlayMuzzleFlashRpc()
    {
        if (muzzleFlashRoot == null)
            return;

        var systems =
            muzzleFlashRoot.GetComponentsInChildren<ParticleSystem>();

        foreach (var system in systems)
        {
            // Skip effects deliberately disabled in the prefab.
            if (!system.gameObject.activeInHierarchy)
                continue;

            // Restart this system without also restarting its children.
            system.Stop(
                false,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            system.Play(false);
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

    [Rpc(SendTo.Everyone, RequireOwnership = true)]
    private void PlayImpactRpc(Vector3 position, Vector3 normal)
    {
        if (impactPrefab == null || normal.sqrMagnitude < 0.001f)
            return;

        normal.Normalize();

        Quaternion rotation =
            Quaternion.LookRotation(normal) *
            Quaternion.Euler(impactRotationOffset);

        GameObject effect = Instantiate(
            impactPrefab,
            position + normal * 0.01f,
            rotation);

        Destroy(effect, impactLifetime);
    }
}