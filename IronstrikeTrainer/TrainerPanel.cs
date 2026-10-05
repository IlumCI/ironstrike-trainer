using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace IronstrikeTrainer;

// The trainer as its own menu, opened from a TRAINER button next to OPTIONS on the main menu, or
// with F1 anywhere.
//
// It is a copy of the Options menu's Canvas with everything that made it the Options menu removed.
// Copying keeps the parts I cannot test without clicking in VR -- the world-space canvas, the
// TrackedDeviceGraphicRaycaster, TMP fonts, the scroll view -- while owning the layout and the
// show/hide means none of the game's own menu logic runs over our rows. Injecting rows into the real
// Options page broke its layout, so this deliberately shares nothing with it at runtime.
internal static class TrainerPanel
{
    static GameObject panel;
    static RectTransform content;
    static ScrollRect scroll;
    static TextMeshProUGUI panelTitle;
    static GameObject rowSource, titleSource;
    static GameObject menuButton;
    static float nextTry;

    static readonly List<object> alive = new();     // Il2Cpp delegates die with their managed source
    static readonly List<Action> relabel = new();

    // Components that belong to the Options menu's behaviour rather than its look.
    static readonly HashSet<string> Strip = new()
        { "OptionsMenuUI", "OptionsOrganizerUI", "OptionsItemUI", "Transitioner", "Localize" };

    public static void Tick()
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextTry) return;
        nextTry = now + 1f;

        try
        {
            if (panel == null) Build();
            if (menuButton == null && panel != null) AddMenuButton();
        }
        catch (Exception e)
        {
            nextTry = now + 10f;
            Plugin.Log.LogError($"trainer panel: {e}");
        }
    }

    public static void Toggle(Transform besideMenu)
    {
        if (panel == null) Build();
        if (panel == null) { Plugin.Log.LogWarning("trainer panel not ready yet"); return; }

        if (panel.activeSelf) { panel.SetActive(false); return; }

        Place(besideMenu);
        if (panelTitle != null) panelTitle.text = "TRAINER";
        foreach (var r in relabel) r();
        panel.SetActive(true);
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    // ------------------------------------------------------------------ build

    static void Build()
    {
        OptionsMenuUI src = null;
        foreach (var m in Resources.FindObjectsOfTypeAll<OptionsMenuUI>())
            if (m != null && m.gameObject.scene.IsValid()) { src = m; break; }   // skip prefab assets
        if (src == null) return;

        var canvas = src.transform.Find("Canvas");
        if (canvas == null) { Plugin.Log.LogWarning("Options menu has no Canvas child"); return; }

        var go = UnityEngine.Object.Instantiate(canvas.gameObject);
        go.name = "IronstrikeTrainerPanel";
        go.transform.localScale = canvas.lossyScale;
        go.SetActive(false);

        StripBehaviour(go);
        foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true))
        {
            cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
        }

        var box = go.transform.Find("MenuBox");
        panelTitle = box?.Find("Title")?.GetComponent<TextMeshProUGUI>();
        scroll = box?.Find("Scroll View")?.GetComponent<ScrollRect>();
        box?.Find("FirstTimeStuff")?.gameObject.SetActive(false);

        var back = box?.Find("BackButton")?.GetComponent<Button>();
        if (back != null) Bind(back, () => panel.SetActive(false));

        content = box?.Find("Scroll View/Viewport/Content")?.GetComponent<RectTransform>();
        if (content == null) { Plugin.Log.LogWarning("no scroll content in Options canvas"); return; }

        // Keep one checkbox row and one title row as templates, then clear the page.
        foreach (var t in content.GetComponentsInChildren<Transform>(true))
        {
            if (rowSource == null && t.name.EndsWith("Checkbox") && t.GetComponentInChildren<Button>(true) != null)
                rowSource = Detach(t.gameObject, go.transform);
            else if (titleSource == null && t.name.EndsWith("Title") && IsCard(t)
                     && t.GetComponent<TextMeshProUGUI>() != null)
                titleSource = Detach(t.gameObject, go.transform);
        }
        for (int i = content.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(content.GetChild(i).gameObject);

        if (rowSource == null) { Plugin.Log.LogWarning("no checkbox row to template from"); return; }

        panel = go;
        Plugin.Log.LogInfo($"templates: row='{rowSource.name}' header='{(titleSource != null ? titleSource.name : "<synthesised from row>")}'");
        Populate();
        Plugin.Log.LogMessage($"trainer panel built: {TrainerModel.Rows.Count} rows");
    }

    // Section headers like AudioTitle carry OptionsItemUI themselves; labels inside a card do not.
    // (Still present here: StripBehaviour's destroys are deferred to the end of the frame.)
    static bool IsCard(Transform t)
    {
        foreach (var c in t.GetComponents<Component>())
            if (c != null && c.GetIl2CppType().Name == "OptionsItemUI") return true;
        return false;
    }

    static GameObject Detach(GameObject original, Transform holder)
    {
        var copy = UnityEngine.Object.Instantiate(original, holder);
        copy.SetActive(false);
        return copy;
    }

    static void StripBehaviour(GameObject root)
    {
        foreach (var c in root.GetComponentsInChildren<Component>(true))
            if (c != null && Strip.Contains(c.GetIl2CppType().Name))
                UnityEngine.Object.Destroy(c);
    }

    static void Populate()
    {
        relabel.Clear();
        float y = 0f;
        string group = null;

        foreach (var row in TrainerModel.Rows)
        {
            try
            {
                if (row.Group != group)
                {
                    group = row.Group;
                    y += Place(MakeTitle(group), y);
                }
                y += Place(MakeRow(row), y);
            }
            catch (Exception e) { Plugin.Log.LogError($"row '{row.Label()}': {e.Message}"); }
        }

        content.sizeDelta = new Vector2(content.sizeDelta.x, y);
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
    }

    // Stack rows top-down ourselves; the game's organizer is stripped. Anchored to the top centre so
    // the result does not depend on how the template rows were anchored inside their old groups.
    static float Place(GameObject go, float y)
    {
        var rt = go.GetComponent<RectTransform>();
        float h = rt.rect.height > 1f ? rt.rect.height : rowSource.GetComponent<RectTransform>().rect.height;

        // Stretch horizontally to the content width. Reusing the template's own width overflowed
        // the viewport, because that width was measured inside its original group container.
        go.transform.SetParent(content, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, h);
        rt.anchoredPosition = new Vector2(0f, -y);
        go.SetActive(true);
        return h;
    }

    static GameObject MakeTitle(string text)
    {
        // Prefer a real section header. If none was found, a checkbox row with the box hidden and
        // the label emphasised does the same job.
        if (titleSource == null) return MakeHeaderFromRow(text);

        var go = UnityEngine.Object.Instantiate(titleSource);
        go.name = "TrainerTitle";
        StripBehaviour(go);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            relabel.Add(() => tmp.text = text.ToUpper());
        }
        return go;
    }

    static GameObject MakeHeaderFromRow(string text)
    {
        var go = UnityEngine.Object.Instantiate(rowSource);
        go.name = "TrainerHeader";
        StripBehaviour(go);

        var box = go.GetComponentInChildren<Button>(true);
        if (box != null) box.gameObject.SetActive(false);

        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (tmps.Count > 1) tmps[1].gameObject.SetActive(false);
        if (tmps.Count > 0)
        {
            var t = tmps[0];
            var l = t.GetComponent<RectTransform>();
            l.anchorMin = new Vector2(0f, 0f); l.anchorMax = new Vector2(1f, 1f);
            l.offsetMin = new Vector2(Pad, 0f); l.offsetMax = new Vector2(-Pad, 0f);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.color = new Color(1f, 1f, 1f, 0.55f);       // quieter than the rows it introduces
            t.fontSize *= 0.8f;
            relabel.Add(() => t.text = "— " + text.ToUpper() + " —");
        }
        return go;
    }

    static GameObject MakeRow(TrainerModel.Row row)
    {
        var go = UnityEngine.Object.Instantiate(rowSource);
        go.name = "TrainerRow";
        StripBehaviour(go);

        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        var label = tmps.Count > 0 ? tmps[0] : null;
        if (tmps.Count > 1) tmps[1].gameObject.SetActive(false);

        var box = go.GetComponentInChildren<Button>(true);
        var imgs = box != null ? box.GetComponentsInChildren<Image>(true) : null;
        var tick = imgs != null && imgs.Count > 0 ? imgs[imgs.Count - 1] : null;

        // The whole row is clickable, not just the small checkbox.
        var rowButton = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        var bg = go.GetComponent<Image>() ?? go.GetComponentInChildren<Image>(true);
        if (bg != null) rowButton.targetGraphic = bg;

        Action click = () => { row.Activate(); foreach (var r in relabel) r(); };
        if (box != null) Bind(box, click);
        Bind(rowButton, click);

        LayoutLeft(label, box);
        if (row.Kind != TrainerModel.Kind.Toggle && box != null) box.gameObject.SetActive(false);

        relabel.Add(() =>
        {
            if (label != null) label.text = row.Label();
            if (tick != null) tick.gameObject.SetActive(row.Kind == TrainerModel.Kind.Toggle && row.IsOn());
        });
        return go;
    }

    // Checkbox hard against the left edge, name straight after it, scrollbar alone on the right.
    // Rows without a checkbox (multipliers, actions) keep the same text indent so the names line up.
    const float Pad = 40f, Gap = 30f;

    static void LayoutLeft(TextMeshProUGUI label, Button box)
    {
        float boxW = 0f;
        if (box != null)
        {
            var b = box.GetComponent<RectTransform>();
            boxW = b.rect.width;
            b.anchorMin = b.anchorMax = new Vector2(0f, 0.5f);
            b.pivot = new Vector2(0f, 0.5f);
            b.anchoredPosition = new Vector2(Pad, 0f);
        }

        if (label == null) return;
        var l = label.GetComponent<RectTransform>();
        l.anchorMin = new Vector2(0f, 0f);
        l.anchorMax = new Vector2(1f, 1f);
        l.pivot = new Vector2(0f, 0.5f);
        l.offsetMin = new Vector2(Pad + boxW + Gap, 0f);
        l.offsetMax = new Vector2(-Pad, 0f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
    }

    // Replace the event outright. RemoveAllListeners() leaves persistent (editor-wired) listeners in
    // place, so a cloned button would still fire its original handler as well as ours -- which is how
    // the earlier attempt left every cloned row also toggling Left-Handed mode.
    static void Bind(Button b, Action fn)
    {
        var d = DelegateSupport.ConvertDelegate<UnityAction>(new Action(() =>
        {
            try { fn(); } catch (Exception e) { Plugin.Log.LogError($"trainer click: {e}"); }
        }));
        alive.Add(d);
        b.onClick = new Button.ButtonClickedEvent();
        b.onClick.AddListener(d);
    }

    // ------------------------------------------------------------ placement

    static void Place(Transform beside)
    {
        var rt = panel.GetComponent<RectTransform>();
        float width = rt.rect.width * panel.transform.lossyScale.x;

        if (beside != null)
        {
            // Stand it next to the menu the player is already looking at.
            panel.transform.SetPositionAndRotation(beside.position + beside.right * width * 1.05f,
                                                   beside.rotation);
            return;
        }

        var cam = GM.instance?.MainCamera;
        if (cam == null) return;
        var fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        float dist = Mathf.Max(1f, width * 0.9f);
        panel.transform.SetPositionAndRotation(cam.transform.position + fwd * dist,
                                               Quaternion.LookRotation(fwd));
    }

    // -------------------------------------------------------- main menu button

    static void AddMenuButton()
    {
        MainMenuUI mm = null;
        foreach (var m in Resources.FindObjectsOfTypeAll<MainMenuUI>())
            if (m != null && m.gameObject.scene.IsValid()) { mm = m; break; }
        if (mm == null) return;

        Button options = null;
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
        {
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                if (b.onClick.GetPersistentMethodName(i) == "PressOptions") { options = b; break; }
            if (options != null) break;
        }
        if (options == null)
        {
            Plugin.Log.LogWarning("no button wired to MainMenuUI.PressOptions; F1 still opens the trainer");
            menuButton = new GameObject("TrainerButtonUnavailable");   // stop retrying
            return;
        }

        var go = UnityEngine.Object.Instantiate(options.gameObject, options.transform.parent);
        go.name = "TrainerButton";
        go.transform.SetSiblingIndex(options.transform.GetSiblingIndex() + 1);
        StripBehaviour(go);
        foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true)) t.text = "TRAINER";

        // If no layout group places it, put it directly under Options instead of on top of it.
        var parent = options.transform.parent;
        bool laidOut = parent.GetComponent<LayoutGroup>() != null;
        if (!laidOut)
        {
            var src = options.GetComponent<RectTransform>();
            var dst = go.GetComponent<RectTransform>();
            dst.anchoredPosition = src.anchoredPosition - new Vector2(0f, src.rect.height * 1.15f);
        }

        var canvas = mm.GetComponentInChildren<Canvas>(true);
        var anchor = canvas != null ? canvas.transform : mm.transform;
        Bind(go.GetComponent<Button>(), () => Toggle(anchor));

        menuButton = go;
        Plugin.Log.LogMessage($"main menu: TRAINER button added next to '{options.name}' " +
                              $"(parent '{parent.name}', layout group: {laidOut})");
    }
}
