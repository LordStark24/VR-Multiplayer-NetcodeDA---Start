using UnityEngine;

[CreateAssetMenu(
    fileName = "WeaponDefinition",
    menuName = "Zombie Mansion/Weapon Definition")]
public class WeaponDefinition : ScriptableObject
{
    [Header("Identity")]
    public string weaponId;
    public string displayName;
    public GameObject weaponPrefab;

    [Header("Progression")]
    [Min(1)] public int requiredPlayerLevel = 1;

    [Header("Base Stats")]
    [Min(1)] public int damage = 20;

    [Tooltip("Number of shots per second.")]
    [Min(0.1f)] public float fireRate = 5f;

    [Tooltip("Maximum firing distance in metres.")]
    [Min(1f)] public float range = 50f;

    [Min(1)] public int magazineCapacity = 30;
}