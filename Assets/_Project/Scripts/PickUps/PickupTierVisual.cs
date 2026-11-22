using UnityEngine;

public class PickupTierVisual : MonoBehaviour
{
    [SerializeField] private GameObject[] levelVisuals = new GameObject[6];

    public void ShowLevel(int playerLevel)
    {
        int index = Mathf.Clamp(playerLevel, 1, 6) - 1;

        for (int i = 0; i < levelVisuals.Length; i++)
        {
            if (levelVisuals[i] != null)
                levelVisuals[i].SetActive(i == index);
        }
    }
}