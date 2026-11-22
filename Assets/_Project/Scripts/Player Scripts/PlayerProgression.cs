using UnityEngine;
using Unity.Netcode;
using Unity.Services.Authentication;

[RequireComponent(typeof(NetworkObject))]
public class PlayerProgression : NetworkBehaviour
{
    [SerializeField]
    private PlayerProgressionDefinition definition;

    private NetworkVariable<int> earnedLevel = new(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public int Level => earnedLevel.Value;
    public long TotalXp { get; private set; }

    public static PlayerProgression Local { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
            return;

        Local = this;

        if (definition == null)
        {
            Debug.LogError(
                "[Progression] Assign the progression definition.", this);
            return;
        }

        var authentication = AuthenticationService.Instance;

        if (!authentication.IsSignedIn)
        {
            Debug.LogError(
                "[Progression] Player must be signed in before spawning.",
                this);
            return;
        }

        string saveKey =
            $"WCG.Progression.XP.v1.{authentication.PlayerId}";

        string savedXp = PlayerPrefs.GetString(saveKey, "0");

        TotalXp = long.TryParse(savedXp, out long parsedXp)
            ? System.Math.Max(0L, parsedXp)
            : 0L;

        earnedLevel.Value = definition.GetEarnedLevel(TotalXp);

        Debug.Log(
            $"[Progression] Player: {authentication.PlayerId} | " +
            $"XP: {TotalXp} | Level: {Level}", this);
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this)
            Local = null;

        base.OnNetworkDespawn();
    }
}