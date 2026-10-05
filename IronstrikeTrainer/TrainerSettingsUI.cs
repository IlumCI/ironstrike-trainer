using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace IronstrikeTrainer;

// Adds the trainer to the game's own Settings menu by cloning real option rows. Cloning rather than
// building a canvas means we inherit the working TrackedDeviceGraphicRaycaster, the TMP font assets,
// the scroll view and the controller interaction, none of which I can test by clicking in VR.
//
// Row anatomy, from a runtime dump of LeftHandedCheckbox:
//   <card>            [RectTransform, OptionsItemUI]
//     <name>BG        [Image]
//     <name>Title     [TextMeshProUGUI, Localize]
//     <name>Desc      [TextMeshProUGUI, Localize]
//     Checkbox        [Button]
//       ... Checkmark [Image]
[HarmonyPatch]
internal static class TrainerSettingsUI
{
    static bool injected;
    static readonly List<object> keepAlive = new();          // Il2Cpp delegates die with their source
    static readonly List<Action> refreshers = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(OptionsMenuUI), nameof(OptionsMenuUI.Show))]
    static void OnShow(OptionsMenuUI __instance)
    {
        try
        {
            if (!injected) Inject(__instance);
            Refresh();
        }
        catch (Exception e) { Plugin.Log.LogError($"settings injection: {e}"); }
    }

    static void Inject(OptionsMenuUI menu)
    {
        var org = menu.GetComponentInChildren<OptionsOrganizerUI>(true);
        if (org == null || org.contentRect == null) { Plugin.Log.LogWarning("no organizer"); return; }

        OptionsItemUI checkboxTemplate = null, titleTemplate = null;
        for (int i = 0; i < org.optionsItems.Count; i++)
        {
            var it = org.optionsItems[i];
            if (it == null) continue;
            if (checkboxTemplate == null && it.GetComponentInChildren<Button>(true) != null
                                         && it.name.Contains("Checkbox")) checkboxTemplate = it;
            if (titleTemplate == null && it.name.EndsWith("Title")) titleTemplate = it;
        }
        if (checkboxTemplate == null) { Plugin.Log.LogWarning("no checkbox row to clone"); return; }

        var parent = org.contentRect;
        string group = null;
        int added = 0;

        foreach (var row in TrainerModel.Rows)
        {
            if (row.Group != group)
            {
                group = row.Group;
                if (titleTemplate != null) AddTitle(org, parent, titleTemplate, $"TRAINER — {group.ToUpper()}");
            }
            AddRow(org, parent, checkboxTemplate, row);
            added++;
        }

        org.Refresh();
        injected = true;
        Plugin.Log.LogMessage($"settings menu: added {added} trainer rows");
    }

    static void AddTitle(OptionsOrganizerUI org, Transform parent, OptionsItemUI template, string text)
    {
        var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
        if (go == null) return;
        go.name = "TrainerTitle";
        foreach (var c in go.GetComponentsInChildren<Component>(true))
            if (c != null && c.GetIl2CppType().Name == "Localize")
                UnityEngine.Object.Destroy(c);
        var item = go.GetComponent<OptionsItemUI>();
        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (tmps.Count > 0) SetText(tmps[0], text);
        for (int i = 1; i < tmps.Count; i++) tmps[i].gameObject.SetActive(false);
        if (item != null) { org.optionsItems.Add(item); item.SetVisible(true); }
    }

    static void AddRow(OptionsOrganizerUI org, Transform parent, OptionsItemUI template,
                       TrainerModel.Row row)
    {
        var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
        go.name = "Trainer_" + row.Label().Replace(' ', '_');

        var item = go.GetComponent<OptionsItemUI>();

        // The game localises these labels, and I2 would overwrite whatever we write. Strip the
        // Localize components by type name so we do not need a reference to the I2 assembly.
        foreach (var c in go.GetComponentsInChildren<Component>(true))
            if (c != null && c.GetIl2CppType().Name == "Localize")
                UnityEngine.Object.Destroy(c);

        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        TextMeshProUGUI title = tmps.Count > 0 ? tmps[0] : null;
        TextMeshProUGUI desc = tmps.Count > 1 ? tmps[1] : null;

        if (desc != null) desc.gameObject.SetActive(false);   // keep rows compact

        var button = go.GetComponentInChildren<Button>(true);
        Image checkmark = FindCheckmark(button);

        var act = DelegateSupport.ConvertDelegate<UnityAction>(new Action(() =>
        {
            try { row.Activate(); Refresh(); }
            catch (Exception e) { Plugin.Log.LogError($"row '{row.Label()}': {e}"); }
        }));
        keepAlive.Add(act);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(act);
        }

        refreshers.Add(() =>
        {
            try
            {
                if (title != null) SetText(title, row.Label());
                if (checkmark != null)
                    checkmark.gameObject.SetActive(row.Kind == TrainerModel.Kind.Toggle
                                                   && row.IsOn != null && row.IsOn());
            }
            catch { }
        });

        if (item != null) { org.optionsItems.Add(item); item.SetVisible(true); }
    }

    // The tick is the last Image under the button; Background/Outline/Glow come first.
    static Image FindCheckmark(Button button)
    {
        if (button == null) return null;
        var imgs = button.GetComponentsInChildren<Image>(true);
        return imgs.Count > 0 ? imgs[imgs.Count - 1] : null;
    }

    static void SetText(TextMeshProUGUI t, string s)
    {
        t.text = s;
        t.gameObject.SetActive(true);
    }

    internal static void Refresh()
    {
        foreach (var r in refreshers) r();
    }
}
