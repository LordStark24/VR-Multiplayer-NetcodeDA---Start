using UnityEngine;

public class VRRigReferences : MonoBehaviour
{
    public static VRRigReferences Instance;

    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    public Transform rifleHolster;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}