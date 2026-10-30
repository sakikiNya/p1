using UnityEngine;

// Animate only the visible child. The root stays at the AR hit used for reporting.
public class InspectionMarkerVisual : MonoBehaviour
{
    public Transform floatingVisual;

    void Update() { Animate(Time.unscaledTime); }

    public void Animate(float time)
    {
        if (!floatingVisual) return;
        floatingVisual.localPosition = new Vector3(0, .075f + Mathf.Sin(time * Mathf.PI * 2) * .0125f, 0);
        floatingVisual.localRotation = Quaternion.Euler(15, time * 55, 15);
    }
}
