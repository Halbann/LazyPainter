using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace LazyPainter;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
[Settings(category = "InstallHelper")]
public class InstallHelper : MonoBehaviour
{
    public static InstallHelper Instance { get; private set; }

    // Registry.
    private static List<PaintMaskMod> recolours = null;
    private bool modDataFetchInProgress = false;
    private static bool triedGetReg = false;

    // Dialog.
    private static readonly HashSet<string> installedThisSession = [];
    private static readonly HashSet<string> remindLater = [];
    private static readonly HashSet<string> dismissedPermanently = [];
    public static readonly Setting<string> dismissedConcatStr = "";

    // Paths.
    private static string pluginData;
    private const string registryURL = @"https://raw.githubusercontent.com/Halbann/LazyPainter/main/GameData/LazyPainter/PluginData/registry.json";
    private static string registryPath;

    public class PaintMaskMod
    {
        public List<string> modRoots;
        public string name;
        public string CKAN;
        public string URL;
    }

    protected void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this);

        pluginData = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "LazyPainter", "PluginData");
        registryPath = Path.Combine(pluginData, "registry.json");

        LoadDismissed();
    }

    public void WhyPartNotRecolourable(Part part)
    {
        if (!triedGetReg)
        {
            triedGetReg = true;
            StartCoroutine(EnsureModData(() => WhyPartNotRecolourable(part)));
            return;
        }

        if (modDataFetchInProgress || recolours == null)
            return;

        if (!TryGetModelPath(part, out string modelPath))
            return;

        if (!TryGetModIdentifier(modelPath, out PaintMaskMod mod))
        {
            if (modelPath.StartsWith("ReStockPBR/") || modelPath.StartsWith("ReStockPlusPBR/"))
                Warn("This RestockPBR part must be painted with Technicolor.");
            else
                Warn($"{part.partInfo.name} from {modelPath.Split('/')[0]} does not support recolouring.");

            return;
        }

        //Debug.Log($"[LazyPainter]: {part.partInfo.name} belongs to {mod}.");

        if (dismissedPermanently.Contains(mod.name)) return;
        if (remindLater.Contains(mod.name)) return;

        if (installedThisSession.Contains(mod.name))
        {
            ShowRestartReminderDialog(mod);
            return;
        }

        ShowRecolourMissingDialog(mod);
    }

    private void Warn(string message) =>
        ScreenMessages.PostScreenMessage(message, 3f, ScreenMessageStyle.LOWER_CENTER);

    #region Registry

    private bool TryGetModelPath(Part part, out string path)
    {
        path = "";
        AvailablePart info = part.partInfo;

        ConfigNode model = info.partConfig?.GetNode("MODEL");
        if (model != null && model.HasValue("model"))
        {
            path = model.GetValue("model");
            return true;
        }

        // Part may be using legacy "mesh" value, which is fully ignored by the game
        // in favour of searching the part config's parent folder. We do the same.
        if (info.partUrlConfig?.parent?.parent != null)
        {
            path = info.partUrlConfig.parent.parent.url;
            return true;
        }

        Debug.LogError($"[LazyPainter]: Couldn't determine the model owner for {info.name}.");
        return false;
    }

    private bool TryGetModIdentifier(string modelPath, out PaintMaskMod recolourMod)
    {
        foreach (var recolour in recolours)
            foreach (var root in recolour.modRoots)
            {
                if (modelPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    recolourMod = recolour;
                    return true;
                }
            }

        recolourMod = null;
        return false;
    }

    private IEnumerator EnsureModData(Action onReady)
    {
        if (modDataFetchInProgress)
        {
            yield return new WaitUntil(() => recolours != null || !modDataFetchInProgress);
            onReady();
            yield break;
        }

        modDataFetchInProgress = true;
        string json = null;

        // Try remote first
        bool remoteSuccess = false;
        using (UnityWebRequest req = UnityWebRequest.Get(registryURL))
        {
            yield return req.SendWebRequest();
            if (req.isNetworkError || req.isHttpError)
                Debug.LogWarning($"[LazyPainter]: Failed to fetch remote mod data: {req.error}. Falling back to local.");
            else
            {
                json = req.downloadHandler.text;
                remoteSuccess = true;
            }
        }

        // Fall back to local
        if (json == null)
        {
            if (File.Exists(registryPath))
                json = File.ReadAllText(registryPath);
            else
                Debug.LogError("[LazyPainter]: Local mod data file not found.");
        }

        if (json != null)
        {
            recolours = ParseModData(json);
            if (remoteSuccess && recolours != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(registryPath));
                File.WriteAllText(registryPath, json);
            }
        }

        modDataFetchInProgress = false;
        onReady();
    }

    private List<PaintMaskMod> ParseModData(string json)
    {
        var result = new List<PaintMaskMod>();

        if (Json.Deserialize(json) is not List<object> list)
        {
            Debug.LogError("[LazyPainter]: Mod data JSON was not a list.");
            return result;
        }

        foreach (var item in list)
        {
            if (item is not Dictionary<string, object> obj) continue;

            List<string> modRoots = ParseStringOrArray(obj, "modRoot");
            string name    = ParseString(obj, "name");
            string ckanId  = ParseString(obj, "CKAN");
            string url     = ParseString(obj, "URL");

            if (modRoots == null || modRoots.Count < 1 || string.IsNullOrEmpty(name))
            {
                Debug.LogWarning("[LazyPainter]: Skipping mod data entry with missing modRoot or name.");
                continue;
            }

            result.Add(new PaintMaskMod
            {
                modRoots = modRoots,
                name = name,
                CKAN = ckanId ?? "",
                URL = url ?? ""
            });
        }

        return result;
    }

    private string ParseString(Dictionary<string, object> obj, string key)
    {
        return obj.TryGetValue(key, out var s) ? s as string : null;
    }

    private List<string> ParseStringOrArray(Dictionary<string, object> obj, string key)
    {
        if (!obj.TryGetValue(key, out var val))
            return null;

        if (val is string s)
            return [s];

        if (val is List<object> l)
        {
            var result = new List<string>(l.Count);
            foreach (var item in l)
                if (item is string s2) result.Add(s2);

            return result;
        }

        return null;
    }

    #endregion

    #region Dialog

    private void ShowRecolourMissingDialog(PaintMaskMod recolour)
    {
        bool hasCKAN = !string.IsNullOrEmpty(recolour.CKAN);
        bool hasURL = !string.IsNullOrEmpty(recolour.URL);

        string message = $"This part requires <b>{recolour.name}</b> in order to be recoloured, but it doesn't appear to be installed.";

        var horizontal = new List<DialogGUIBase>(3);
        var vertical = new List<DialogGUIBase>(3);

        // First row.

        //if (hasCKAN)
        //{
        //    string ckanLabel = CKANUrl.useInstallGrammar ? "Install via CKAN" : "Find in CKAN";
        //    horizontal.Add(new DialogGUIButton($"<color={XKCDColors.HexFormat.KSPBadassGreen}>{ckanLabel}</color>", () =>
        //    {
        //        CKANUrl.Install(recolour.CKAN);
        //        installedThisSession.Add(recolour.name);
        //    }));
        //}

        if (hasURL)
        {
            horizontal.Add(new DialogGUIButton(hasCKAN ? "Manual" : "Download", () =>
            {
                Application.OpenURL(recolour.URL);
                installedThisSession.Add(recolour.name);
            }));
        }

        var firstRow = new DialogGUIHorizontalLayout(horizontal.ToArray());
        firstRow.stretchWidth = true;
        vertical.Add(firstRow);
        horizontal.Clear();

        // Divider.

        if (hasCKAN || hasURL)
            vertical.Add(new DialogGUIBox("", -1f, 3f));

        // Second row.

        horizontal.Add(new DialogGUIButton($"<color={XKCDColors.HexFormat.KSPNotSoGoodOrange}>Never</color>", () =>
        {
            dismissedPermanently.Add(recolour.name);
            SaveDismissed();
        }));
        horizontal.Add(new DialogGUIButton("Later", () =>
            remindLater.Add(recolour.name)));
        horizontal.Add(new DialogGUIButton("Close", () => { }));

        var secondRow = new DialogGUIHorizontalLayout(horizontal.ToArray());
        secondRow.stretchWidth = true;
        vertical.Add(secondRow);
        horizontal.Clear();

        // Pop up.

        var column = new DialogGUIVerticalLayout(vertical.ToArray());
        column.stretchWidth = true;

        var dialog = PopupDialog.SpawnPopupDialog(
            new MultiOptionDialog(
                "LPRecolourMissing",
                message,
                "Recolour Mod Missing",
                HighLogic.UISkin,
                column
            ),
            false,
            HighLogic.UISkin
        ).Alignment(TextAlignmentOptions.Center);
    }

    private void ShowRestartReminderDialog(PaintMaskMod mod)
    {
        PopupDialog.SpawnPopupDialog(
            new MultiOptionDialog(
                "LPRestartReminder",
                $"If {mod.name} was installed this session, please restart the game to enable recolouring for this part.",
                "Restart Required",
                HighLogic.UISkin,
                new DialogGUIButton("OK", () => { })
            ),
            false,
            HighLogic.UISkin
        ).Alignment(TextAlignmentOptions.Center);
    }

    #endregion

    #region Serialisation

    private void LoadDismissed()
    {
        GlobalSettings.Load();
        dismissedPermanently.Clear();

        if (string.IsNullOrWhiteSpace(dismissedConcatStr))
            return;

        foreach (var item in dismissedConcatStr.Value.Split(','))
        {
            var trimmed = item.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                dismissedPermanently.Add(trimmed);
        }
    }

    private void SaveDismissed()
    {
        dismissedConcatStr.Value = string.Join(",", dismissedPermanently);
        GlobalSettings.Save();
    }

    #endregion
}

public static class PopupDialogExtension
{
    public static PopupDialog Alignment(this PopupDialog dialog, TextAlignmentOptions alignment)
    {
        dialog.transform.Find("UITextPrefab(Clone)")?.GetComponent<TextMeshProUGUI>()?.alignment = alignment;
        return dialog;
    }

    //public static DialogGUIButton Colour(this DialogGUIButton button, Color colour)
    //{
    //    button.transform.Find("UITextPrefab(Clone)")?.GetComponent<TextMeshProUGUI>()?.alignment = colour;
    //    return button;
    //}
}