using System.Collections;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(XRGrabInteractable))]
public class NetworkGrabOwnership : NetworkBehaviour
{
    public void TakeOwnership()
    {
        if (!IsSpawned)
        {
            Debug.LogWarning(
                "[Weapon] Rifle is not network-spawned.", this);
            return;
        }

        if (!IsOwner)
        {
            NetworkObject.ChangeOwnership(
                NetworkManager.LocalClientId);
        }
    }

    public override void OnGainedOwnership()
    {
        Debug.Log(
            $"[Weapon] Ownership gained | " +
            $"Object: {NetworkObjectId} | " +
            $"Owner: {OwnerClientId}", this);
    }

    public override void OnLostOwnership()
    {
        StartCoroutine(ReleasePreviousGrab());
    }

    private IEnumerator ReleasePreviousGrab()
    {
        // Let the ownership callbacks finish first.
        yield return null;

        if (!IsSpawned || IsOwner)
            yield break;

        var grab = GetComponent<XRGrabInteractable>();
        var manager = grab.interactionManager;

        if (manager == null)
            yield break;

        // Copy the list because SelectExit changes it.
        var selectingInteractors =
            grab.interactorsSelecting.ToArray();

        foreach (var interactor in selectingInteractors)
        {
            manager.SelectExit(interactor, grab);
        }

        GetComponent<RifleWeapon>()?.StopFiring();

        Debug.Log("[Weapon] Previous local grab released.", this);
    }
}