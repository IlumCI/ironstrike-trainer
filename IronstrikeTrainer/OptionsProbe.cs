using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace IronstrikeTrainer;

// One-shot structural dump of the options menu when it opens. Injecting a row means cloning an
// existing card, so we need to know what a card is actually made of -- which components carry the
// label and the click -- and guessing from type names is how you waste an afternoon.
[HarmonyPatch]
internal static class OptionsProbe
{
    static bool done;

    // The rows under Scroll View/Viewport/Content are instantiated when the menu is opened, so a
    // cold prefab read shows nothing. This fires on the real open.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(OptionsMenuUI), nameof(OptionsMenuUI.Show))]
    static void Shown(OptionsMenuUI __instance)
    {
        if (!Plugin.C.ProbeOptions.Value) return;
        done = false;
        Report(__instance);
    }

    // Cold read, for when nobody is around to open the menu.
    public static void Run()
    {
        if (done) return;
        done = true;

        try
        {
            var menus = UnityEngine.Resources.FindObjectsOfTypeAll<OptionsMenuUI>();
            Plugin.Log.LogInfo($"OptionsMenuUI instances: {menus.Length}");
            if (menus.Length == 0) { Plugin.Log.LogWarning("none in memory yet"); done = false; return; }
            Report(menus[0]);
        }
        catch (Exception e) { Plugin.Log.LogError($"options probe: {e}"); }
    }

    static void Report(OptionsMenuUI __instance)
    {
        if (done) return;
        done = true;
        try
        {

            Plugin.Log.LogInfo("=== options menu structure ===");
            var sb = new StringBuilder();
            Walk(__instance.transform, 0, sb, 7);
            foreach (var line in sb.ToString().Split('\n')) 
                if (line.Length > 0) Plugin.Log.LogInfo(line);

            var org = __instance.GetComponentInChildren<OptionsOrganizerUI>(true);
            if (org == null) { Plugin.Log.LogWarning("no OptionsOrganizerUI found"); return; }

            Plugin.Log.LogInfo($"organizer: items={org.optionsItems?.Count} contentRect={(org.contentRect != null ? org.contentRect.name : "<null>")}");

            // Dump one card in full: that is the thing we will clone.
            if (org.optionsItems != null && org.optionsItems.Count > 0)
            {
                var card = org.optionsItems[0];
                Plugin.Log.LogInfo($"=== first card: {card.name} height={card.height} ===");
                var sb2 = new StringBuilder();
                Walk(card.transform, 0, sb2, 6);
                foreach (var line in sb2.ToString().Split('\n'))
                    if (line.Length > 0) Plugin.Log.LogInfo(line);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"options probe: {e}"); }
    }

    static void Walk(Transform t, int depth, StringBuilder sb, int maxDepth)
    {
        var pad = new string(' ', depth * 2);
        var comps = t.GetComponents<Component>();
        var names = new StringBuilder();
        for (int i = 0; i < comps.Count; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (names.Length > 0) names.Append(", ");
            names.Append(c.GetIl2CppType().Name);
        }
        sb.Append($"  {pad}{t.name}  [{names}]\n");

        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb, maxDepth);
    }
}
