using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_VISIONOS || UNITY_EDITOR
using UnityEngine.XR.VisionOS;
using UnityEngine.XR.VisionOS.InputDevices;
#endif

/// Look at a button and pinch (full VR, Metal rendering): visionOS reports the gaze ray once, when the pinch begins.
/// The ray hits the panel's BoxCollider; the topmost button or text field under that point gets the tap.
/// Read like Unity's visionOS Metal sample (AnchorPlacer): the pointer state every frame, a tap on each new Began.
public class SpatialTap : MonoBehaviour {
#if UNITY_VISIONOS || UNITY_EDITOR
    InputAction primary, secondary;
    VisionOSSpatialPointerPhase lastPrimary, lastSecondary;
    // visionOS sometimes reports one pinch as two (the plugin infers an extra Began): the headset log showed pairs of
    // taps in the same second, so ">" skipped a patient and Talk stopped right after it started. One tap per 0.3 s.
    const float Debounce = 0.3f;
    static float lastTap = -1f;   // static: the new app after Restart must not take the pinch that pressed Restart

    void OnEnable() {
        primary = new InputAction("primary", InputActionType.PassThrough, "<VisionOSSpatialPointerDevice>/spatialPointer0");
        secondary = new InputAction("secondary", InputActionType.PassThrough, "<VisionOSSpatialPointerDevice>/spatialPointer1");
        primary.Enable(); secondary.Enable();
    }
    void OnDisable() { primary?.Disable(); secondary?.Disable(); }

    void Update() {
        Handle(primary.ReadValue<VisionOSSpatialPointerState>(), ref lastPrimary);
        Handle(secondary.ReadValue<VisionOSSpatialPointerState>(), ref lastSecondary);
    }

    /// A tap when the phase turns to Began. (Not by interaction id: that is a slot number every new pinch reuses,
    /// so the first build took only the first pinch of the session.) Returns what was tapped, null if no new pinch.
    public string Handle(VisionOSSpatialPointerState s, ref VisionOSSpatialPointerPhase last) {
        bool began = s.phase == VisionOSSpatialPointerPhase.Began && last != VisionOSSpatialPointerPhase.Began;
        last = s.phase;
        if (!began) return null;
        if (Time.realtimeSinceStartup - lastTap < Debounce) { Debug.Log("[MedicalVR] pinch ignored (same pinch twice)"); return null; }
        lastTap = Time.realtimeSinceStartup;
        // the ray is in the headset's tracking space, the space the camera's parent (XR Origin's Camera Offset) maps to the world
        var space = Camera.main != null ? Camera.main.transform.parent : null;
        var origin = space != null ? space.TransformPoint(s.startRayOrigin) : s.startRayOrigin;
        var dir = space != null ? space.TransformDirection(s.startRayDirection) : s.startRayDirection;
        var what = Tap(new Ray(origin, dir));
        Debug.Log($"[MedicalVR] pinch {s.Kind}: ray {origin:F2} -> {dir:F2}, {what}");
        return what;
    }
#endif

    /// Taps the UI element the ray points at. Says what it hit (for the log).
    public string Tap(Ray ray) {
        if (!Physics.Raycast(ray, out var hit, 10f)) return "hit nothing";
        var canvas = hit.collider.GetComponent<Canvas>();
        if (canvas == null) return "hit " + hit.collider.name;
        Selectable best = null;
        float nearest = float.MaxValue;
        foreach (var sel in canvas.GetComponentsInChildren<Selectable>()) {   // active ones, in drawing order
            if (!sel.interactable) continue;
            var rt = (RectTransform)sel.transform;
            var p = (Vector2)rt.InverseTransformPoint(hit.point);
            if (rt.rect.Contains(p)) { best = sel; nearest = 0; continue; }
            // gaze is not exact: a pinch just beside a button (gaps are 14 px) counts for the nearest one within 3 cm
            var gap = Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, rt.rect.xMin, rt.rect.xMax), Mathf.Clamp(p.y, rt.rect.yMin, rt.rect.yMax)));
            var metres = gap * rt.lossyScale.x;
            if (nearest > 0 && metres < 0.03f && metres < nearest) { best = sel; nearest = metres; }
        }
        if (best == null) return "hit the panel, no button there";
        StartCoroutine(Flash(best));
        if (best is Button b) b.onClick.Invoke();
        // visionOS shows no system keyboard in a full immersive space: the app's own keyboard types into the field
        else if (best is TMP_InputField f) {
            if (VirtualKeyboard.Instance != null) VirtualKeyboard.Instance.Open(f);
            else { f.Select(); f.ActivateInputField(); }
        }
        var label = best.GetComponentInChildren<TMP_Text>();
        return $"pressed {best.name} \"{(label != null ? label.text : "")}\"";
    }

    // No hover on visionOS (gaze stays private), so the tapped button blinks instead.
    static IEnumerator Flash(Selectable s) {
        var img = s.targetGraphic;
        if (img == null) yield break;
        var c = img.color;
        img.color = Color.Lerp(c, Color.white, 0.5f);
        yield return new WaitForSeconds(0.15f);
        if (img != null) img.color = c;
    }
}
