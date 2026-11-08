using UnityEngine;

public class MansionSpawnPoints : MonoBehaviour
{

    public BoxCollider respawnBounds;
    public Transform safeRespawnPoint;
    public LayerMask respawnGeometryLayers;
    public Transform[] spawnPoints;
}