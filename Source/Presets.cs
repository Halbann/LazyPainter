using KSPShaderTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LazyPainter
{
    public static class Presets
    {
        public static void AddCustomGroup()
        {
            RecoloringDataPresetGroup customGroup = new RecoloringDataPresetGroup("Custom");
            customGroup.colors = new List<RecoloringDataPreset>();
            PresetColor.getGroupList().Add(customGroup);

            FieldInfo groupsField = typeof(PresetColor).GetField("presetGroups", BindingFlags.Static | BindingFlags.NonPublic);
            Dictionary<string, RecoloringDataPresetGroup> presetGroups = (Dictionary<string, RecoloringDataPresetGroup>)groupsField.GetValue(null);
            presetGroups.Add("Custom", customGroup);
        }

        public static void DeletePreset(int customIndex)
        {
            // Remove from custom group.

            RecoloringDataPresetGroup customGroup = PresetColor.getGroupList().Find(g => g.name == "Custom");
            if (customGroup == null)
                return;

            RecoloringDataPreset preset = customGroup.colors[customIndex];
            customGroup.colors.RemoveAt(customIndex);

            // Remove from file.

            string kspRoot = KSPUtil.ApplicationRootPath;
            string modPath = Path.Combine(kspRoot, "GameData", "LazyPainter");
            string filePath = Path.Combine(modPath, "customColours" + ".cfg");

            ConfigNode file;

            Debug.Log("Delete Colour: " + preset.name);

            if (File.Exists(filePath))
                file = ConfigNode.Load(filePath);
            else
                return;

            ConfigNode[] groups = file.GetNodes("PRESET_COLOR_GROUP");
            ConfigNode groupsNode = groups.FirstOrDefault(g => g.GetValue("name") == "Custom");
            if (groupsNode == null)
                return;

            ConfigNode colourPreset = file.GetNodes("KSP_COLOR_PRESET").FirstOrDefault(n => n.GetValue("name") == preset.name);
            if (colourPreset == null)
                return;

            file.RemoveNode(colourPreset);

            int index = groupsNode.GetValues().ToList().FindIndex(n => n == preset.name);
            if (index != -1)
                groupsNode.values.Remove(groupsNode.values[index]);

            file.Save(filePath);
        }

        public static void SaveColour(string name, RecoloringDataPreset preset)
        {
            // Add the custom colour to the preset group.

            preset.title = name;
            preset.name = preset.title.Replace(" ", "").ToLower();

            RecoloringDataPresetGroup customGroup = PresetColor.getGroupList().Find(g => g.name == "Custom");

            if (customGroup == null)
                AddCustomGroup();

            List<RecoloringDataPreset> group = customGroup.colors;
            int index;
            if ((index = group.FindIndex(p => p.name == preset.name)) != -1)
            {
                group[index] = preset;
            }
            else
            {
                group.Add(preset);
            }

            // Serialise the custom preset to a file for the next time the game is loaded.

            string kspRoot = KSPUtil.ApplicationRootPath;
            string modPath = Path.Combine(kspRoot, "GameData", "LazyPainter");
            string filePath = Path.Combine(modPath, "customColours" + ".cfg");

            ConfigNode file;

            Debug.Log("SaveColour: " + name);

            if (File.Exists(filePath))
                file = ConfigNode.Load(filePath);
            else
                file = new ConfigNode();

            ConfigNode[] groups = file.GetNodes("PRESET_COLOR_GROUP");
            ConfigNode groupsNode = groups.FirstOrDefault(g => g.GetValue("name") == "Custom");
            if (groupsNode == null)
            {
                groupsNode = new ConfigNode("PRESET_COLOR_GROUP");
                file.AddNode(groupsNode);
                groupsNode.SetValue("name", "Custom", true);
            }

            ConfigNode colourPreset = file.GetNodes("KSP_COLOR_PRESET").FirstOrDefault(n => n.GetValue("name") == preset.name);
            if (colourPreset == null)
            {
                colourPreset = new ConfigNode("KSP_COLOR_PRESET");
                file.AddNode(colourPreset);
            }
            colourPreset.SetValue("name", preset.name, true);
            colourPreset.SetValue("title", preset.title, true);
            colourPreset.SetValue("color", String.Join(", ", Colour.ColortoRGB255(preset.color)), true);
            colourPreset.SetValue("metallic", Mathf.RoundToInt(preset.metallic * 255), true);
            colourPreset.SetValue("specular", Mathf.RoundToInt(preset.specular * 255), true);

            if (!groupsNode.GetValues("color").Contains(preset.name))
                groupsNode.AddValue("color", preset.name);

            file.Save(filePath);
        }
    }
}