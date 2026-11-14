using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The app's own keyboard under the panel. In a full immersive (Metal) space visionOS shows no system keyboard,
/// because the app has no window; so a tap on a text field opens this one (look at a key and pinch).
/// Enter sends the question (or closes, for the server address), Close closes without sending.
public class VirtualKeyboard : MonoBehaviour {
    public static VirtualKeyboard Instance { get; private set; }
    static readonly string[] Rows = { "1234567890", "qwertyuiop", "asdfghjkl?", "zxcvbnm,.'" };
    const float Width = 1000, Height = 470;

    TMP_InputField field;
    RectTransform keys;
    Image fieldBg;
    Color fieldColor;
    TMP_Text preview, shiftLabel;
    readonly List<(TMP_Text label, char c)> letters = new();
    bool shift;

    public bool IsOpen => field != null && gameObject.activeSelf;

    /// Builds the keyboard over the lower half of `panel` (a world-space canvas), 12 cm nearer the student and tilted
    /// back like a keyboard on a desk, so it is at a comfortable height; hidden until a text field is tapped.
    public static VirtualKeyboard Create(RectTransform panel) {
        var go = new GameObject("Keyboard", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(Width, Height);
        rt.localScale = panel.localScale;
        rt.rotation = panel.rotation * Quaternion.Euler(20, 0, 0);
        float panelHalf = panel.sizeDelta.y * panel.localScale.y / 2;
        rt.position = panel.position - panel.up * (panelHalf * 0.55f) - panel.forward * 0.12f;   // panel.forward points away from the student
        go.AddComponent<BoxCollider>().size = new Vector3(Width, Height, 1);   // look + pinch hits colliders
        var kb = go.AddComponent<VirtualKeyboard>();
        kb.Build(rt);
        LayoutRebuilder.ForceRebuildLayoutImmediate(kb.keys);   // from the layout group itself: the canvas root has none, so a rebuild from there skips everything
        go.SetActive(false);
        Instance = kb;
        return kb;
    }

    void Build(RectTransform rt) {
        App.Stretch(App.Img(rt, new Color(0.90f, 0.91f, 0.94f, 0.97f)).rectTransform);
        var col = new GameObject("Keys", typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(rt, false);
        var cr = keys = (RectTransform)col.transform;
        App.Stretch(cr); cr.offsetMin = new Vector2(14, 14); cr.offsetMax = new Vector2(-14, -14);
        var v = col.GetComponent<VerticalLayoutGroup>();
        v.spacing = 8; v.childControlWidth = v.childControlHeight = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;

        preview = App.Text(col.transform, "", 30);   // what is typed so far (the field itself may be too short)
        preview.GetComponent<LayoutElement>().preferredHeight = 50;
        preview.textWrappingMode = TextWrappingModes.NoWrap;

        foreach (var keys in Rows) {
            var row = App.Row(col.transform, 70);
            foreach (var ch in keys) {
                var c = ch;
                var b = App.Btn(row, c.ToString(), () => Type(c), 70);
                b.GetComponent<Image>().color = Color.white;
                var label = b.GetComponentInChildren<TMP_Text>();
                label.color = new Color(0.1f, 0.1f, 0.15f); label.fontSize = 32;
                if (char.IsLetter(c)) letters.Add((label, c));
            }
        }
        var last = App.Row(col.transform, 70);
        last.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        void Key(string label, UnityEngine.Events.UnityAction act, float width, bool flexible = false) {
            var b = App.Btn(last, label, act, 70);
            var le = b.GetComponent<LayoutElement>(); le.preferredWidth = width; le.flexibleWidth = flexible ? 1 : 0;
            if (label.Length == 1) { b.GetComponent<Image>().color = Color.white; b.GetComponentInChildren<TMP_Text>().color = new Color(0.1f, 0.1f, 0.15f); }
        }
        Key("Shift", ToggleShift, 140);
        shiftLabel = last.GetChild(last.childCount - 1).GetComponentInChildren<TMP_Text>();
        Key(":", () => Type(':'), 70);
        Key("/", () => Type('/'), 70);
        Key("Space", () => Type(' '), 200, true);
        Key("Delete", Backspace, 130);
        Key("Close", Close, 120);
        Key("Enter", Enter, 140);
    }

    /// Types into `f` from now on (a tap on a text field).
    public void Open(TMP_InputField f) {
        if (field != null && field != f) Close();
        field = f;
        fieldBg = f.GetComponent<Image>();
        if (fieldBg != null) { fieldColor = fieldBg.color; fieldBg.color = new Color(1f, 0.96f, 0.75f); }   // which field gets the text
        gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(keys);
        Refresh();
        Debug.Log("[MedicalVR] keyboard open");
    }

    public void Close() {
        if (fieldBg != null) fieldBg.color = fieldColor;
        field = null; fieldBg = null;
        if (shift) ToggleShift();
        gameObject.SetActive(false);
    }

    void Type(char c) {
        if (field == null) return;
        field.text += shift && char.IsLetter(c) ? char.ToUpper(c) : c;
        if (shift) ToggleShift();   // one capital letter, like a phone keyboard
        Refresh();
    }

    void Backspace() {
        if (field != null && field.text.Length > 0) field.text = field.text.Substring(0, field.text.Length - 1);
        Refresh();
    }

    void ToggleShift() {
        shift = !shift;
        foreach (var (label, c) in letters) label.text = (shift ? char.ToUpper(c) : c).ToString();
        if (shiftLabel != null) shiftLabel.text = shift ? "SHIFT" : "Shift";
    }

    void Enter() {
        var f = field;
        Close();
        if (f != null) f.onSubmit.Invoke(f.text);   // the question field sends; the address field has no listener
    }

    void Refresh() {
        var t = field != null ? field.text : "";
        preview.text = (t.Length > 60 ? "..." + t.Substring(t.Length - 57) : t) + "<color=#2F6FB5>|</color>";
    }
}
