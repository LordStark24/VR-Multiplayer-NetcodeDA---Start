using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerHealth : NetworkBehaviour
{
    private const int StartingHealth = 100;

    private NetworkVariable<int> health =
        new NetworkVariable<int>(
            StartingHealth,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    private NetworkVariable<int> deaths = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public int Deaths => deaths.Value;

    private NetworkVariable<int> damageDealt = new(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Owner);

    private NetworkVariable<int> kills = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public int DamageDealt => damageDealt.Value;
    public int Kills => kills.Value;

    public int CurrentHealth => health.Value;
    public bool IsAlive => health.Value > 0;

    public override void OnNetworkSpawn()
    {
        health.OnValueChanged += OnHealthChanged;

        Debug.Log(
            $"[Health] Player spawned | Owner: {OwnerClientId} | " +
            $"Health: {CurrentHealth}");
    }

    public override void OnNetworkDespawn()
    {
        health.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int previous, int current)
    {
        Debug.Log(
            $"[Health] Player {OwnerClientId}: {previous} -> {current}");

        if (previous > 0 && current == 0)
        {
            Debug.Log($"[Health] Player {OwnerClientId} died.");

            if (IsOwner)
            {
                deaths.Value++;

                Debug.Log(
                    $"[Score] Player {OwnerClientId} | Deaths: {Deaths}");

                MoveToPenaltyRoom();
            }
        }
    }
    [Rpc(SendTo.Owner, RequireOwnership = false)]
    public void RequestWeaponDamageRpc(
    NetworkObjectReference weaponReference,
    RpcParams rpcParams = default)
    {
        if (!IsSpawned || !IsOwner || !IsAlive)
            return;

        var match =
            UnityEngine.Object.FindFirstObjectByType<MansionMatchController>();

        // Only allow combat damage during active gameplay.
        if (match == null ||
            match.CurrentPhase != MansionMatchController.MatchPhase.Playing)
            return;

        if (!weaponReference.TryGet(out NetworkObject weaponObject))
            return;

        ulong shooterId = rpcParams.Receive.SenderClientId;

        // The request must come from the rifle's owner.
        if (weaponObject.OwnerClientId != shooterId)
            return;

        // No self-damage for this prototype.
        if (shooterId == OwnerClientId)
            return;

        var weapon = weaponObject.GetComponent<RifleWeapon>();

        if (weapon == null || weapon.Damage <= 0)
            return;

        int actualDamage = Mathf.Min(CurrentHealth, weapon.Damage);

        health.Value -= actualDamage;

        AwardCombatCreditRpc(
            shooterId,
            actualDamage,
            health.Value == 0);

        Debug.Log(
            $"[Health] Player {OwnerClientId} hit by {shooterId} | " +
            $"Damage: {weapon.Damage} | Health: {CurrentHealth}");
    }

    [Rpc(SendTo.Everyone, RequireOwnership = true)]
    private void AwardCombatCreditRpc(
    ulong shooterId,
    int actualDamage,
    bool killed,
    RpcParams rpcParams = default)
    {
        // Only accept credit reported by this victim's owner.
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (NetworkManager.LocalClientId != shooterId ||
            shooterId == OwnerClientId ||
            actualDamage <= 0 ||
            actualDamage > StartingHealth)
            return;

        var localPlayer = NetworkManager.LocalClient?.PlayerObject;

        if (localPlayer == null)
            return;

        var shooterHealth =
            localPlayer.GetComponent<NetworkPlayerHealth>();

        if (shooterHealth == null ||
            !shooterHealth.IsSpawned ||
            !shooterHealth.IsOwner)
            return;

        shooterHealth.damageDealt.Value += actualDamage;

        if (killed)
        {
            shooterHealth.kills.Value++;
        }

        Debug.Log(
            $"[Score] Player {shooterId} | " +
            $"Damage dealt: {shooterHealth.DamageDealt} | " +
            $"Kills: {shooterHealth.Kills} | " +
            $"Deaths: {shooterHealth.Deaths}");
    }

    [ContextMenu("Test / Take 25 Damage")]
    private void TestTakeDamage()
    {
        if (!Application.isPlaying || !IsSpawned || !IsOwner)
        {
            Debug.LogWarning(
                "[Health] Run this test on your locally owned network player.");
            return;
        }

        health.Value = Mathf.Max(0, health.Value - 25);
    }

    private void MoveToPenaltyRoom()
    {
        var penaltySpawn =
            UnityEngine.Object.FindFirstObjectByType<PenaltyRoomSpawn>();

        if (penaltySpawn == null)
        {
            Debug.LogError("[Health] PenaltyRoomSpawn was not found.");
            return;
        }

        if (VRRigReferences.Instance == null)
        {
            Debug.LogError("[Health] Local VR rig was not found.");
            return;
        }

        Transform rigRoot = VRRigReferences.Instance.transform.root;
        var controller = rigRoot.GetComponent<CharacterController>();

        if (controller == null)
        {
            Debug.LogError("[Health] No CharacterController on the local VR rig root.");
            return;
        }

        Quaternion rotation =
            Quaternion.Euler(0f, penaltySpawn.transform.eulerAngles.y, 0f);

        // Put the bottom center of the capsule at the penalty marker.
        float halfHeight = Mathf.Max(controller.height * 0.5f, controller.radius);
        Vector3 capsuleFeet = controller.center - Vector3.up * halfHeight;
        Vector3 rootPosition = penaltySpawn.transform.position - rotation * capsuleFeet;

        rigRoot.SetPositionAndRotation(rootPosition, rotation);

        Debug.Log("[Health] Moved local player to the penalty room.");

        StartCoroutine(RespawnAfterPenalty());
    }

    private IEnumerator RespawnAfterPenalty()
    {
        yield return new WaitForSeconds(15f);

        // The avatar may have despawned while the player was waiting.
        if (!IsSpawned || !IsOwner || health.Value > 0)
            yield break;

        var match =
            UnityEngine.Object.FindFirstObjectByType<MansionMatchController>();

        // Don't respawn if the match has finished.
        if (match == null ||
            match.CurrentPhase != MansionMatchController.MatchPhase.Playing)
        {
            yield break;
        }

        var mansionSpawns =
    UnityEngine.Object.FindFirstObjectByType<MansionSpawnPoints>();

        var rig = VRRigReferences.Instance;

        if (mansionSpawns == null || mansionSpawns.respawnBounds == null ||
            mansionSpawns.respawnGeometryLayers.value == 0 ||
            rig == null || rig.head == null)
        {
            Debug.LogError("[Health] Respawn setup is incomplete. Staying in penalty room.");
            yield break;
        }

        Transform rigRoot = rig.transform.root;
        var controller = rigRoot.GetComponent<CharacterController>();

        if (controller == null)
        {
            Debug.LogError("[Health] No CharacterController on the local rig root.");
            yield break;
        }

        var candidates = new List<Transform>();

        if (mansionSpawns.spawnPoints != null)
        {
            foreach (var marker in mansionSpawns.spawnPoints)
            {
                if (marker != null && marker != mansionSpawns.safeRespawnPoint)
                    candidates.Add(marker);
            }
        }

        while (candidates.Count > 0)
        {
            int index = Random.Range(0, candidates.Count);
            Transform marker = candidates[index];
            candidates.RemoveAt(index);

            if (TryRespawnAt(marker, mansionSpawns, rigRoot, rig.head, controller))
                yield break;
        }

        if (TryRespawnAt(mansionSpawns.safeRespawnPoint, mansionSpawns,
                         rigRoot, rig.head, controller))
        {
            yield break;
        }

        Debug.LogError("[Health] No safe respawn found. Staying in penalty room.");
    }

    private bool TryRespawnAt(
    Transform marker,
    MansionSpawnPoints spawns,
    Transform rigRoot,
    Transform head,
    CharacterController controller)
    {
        if (marker == null || !InsideRespawnBounds(spawns.respawnBounds, marker.position))
            return false;

        // Preserve an upright rig even if the marker is tilted.
        Quaternion rotation = Quaternion.Euler(0f, marker.eulerAngles.y, 0f);

        // Put the headset's horizontal position at the marker.
        Vector3 headOffset = Quaternion.Inverse(rigRoot.rotation) *
                             (head.position - rigRoot.position);
        Vector3 rotatedOffset = rotation * headOffset;
        rotatedOffset.y = 0f;
        Vector3 rootPosition = marker.position - rotatedOffset;

        // Calculate where the controller's capsule will actually land.
        // This assumes the CharacterController is on an upright, unit-scale root.
        Vector3 centerOffset = rotation * controller.center;
        Vector3 center = rootPosition + centerOffset;
        float radius = controller.radius;
        float halfHeight = Mathf.Max(controller.height * 0.5f, radius);

        Vector3 floorProbe = new Vector3(center.x, marker.position.y + 0.5f, center.z);
        if (!Physics.Raycast(floorProbe, Vector3.down, out RaycastHit floor,
                             1f, spawns.respawnGeometryLayers,
                             QueryTriggerInteraction.Ignore) ||
            Vector3.Angle(floor.normal, Vector3.up) > controller.slopeLimit ||
            !InsideRespawnBounds(spawns.respawnBounds, floor.point))
        {
            return false;
        }

        const float floorClearance = 0.05f;
        rootPosition.y = floor.point.y + halfHeight + floorClearance - centerOffset.y;
        center = rootPosition + centerOffset;

        float segment = halfHeight - radius;
        if (Physics.CheckCapsule(center - Vector3.up * segment,
                                 center + Vector3.up * segment,
                                 radius, spawns.respawnGeometryLayers,
                                 QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        rigRoot.SetPositionAndRotation(rootPosition, rotation);
        health.Value = StartingHealth;
        Debug.Log($"[Health] Respawned at {marker.name} with {health.Value} health.");
        return true;
    }

    private static bool InsideRespawnBounds(BoxCollider box, Vector3 point)
    {
        // Use the box dimensions directly because its collider is disabled.
        Vector3 local = box.transform.InverseTransformPoint(point) - box.center;
        Vector3 half = box.size * 0.5f;

        return Mathf.Abs(local.x) <= half.x &&
               Mathf.Abs(local.y) <= half.y &&
               Mathf.Abs(local.z) <= half.z;
    }


}