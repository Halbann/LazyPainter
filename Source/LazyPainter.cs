using Highlighting;
using KSP.UI;
using KSP.UI.Screens;
using KSPShaderTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LazyPainter
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class LazyPainter : MonoBehaviour
    {
        #region Fields

        // GUI variables.
        private static Rect windowRect = new Rect(Screen.width * 0.04f, Screen.height * 0.1f, 0, 0);
        private int windowID;
        private int windowWidth = 300;
        private GUIStyle boxStyle;
        private GUIStyle questionStyle;
        private Texture2D[] colourTextures;
        private int editingColour = 0;
        private static bool scrollLock = false;
        private bool showHelp = false;

        private static ClickBlocker clickBlocker;
        public static ControlTypes controlLock =
            ControlTypes.EDITOR_PAD_PICK_PLACE
            | ControlTypes.EDITOR_GIZMO_TOOLS
            | ControlTypes.EDITOR_PAD_PICK_COPY
            | ControlTypes.EDITOR_ICON_HOVER
            | ControlTypes.EDITOR_ICON_PICK
            | ControlTypes.EDITOR_TAB_SWITCH
            | ControlTypes.EDITOR_MODE_SWITCH
            | ControlTypes.EDITOR_ROOT_REFLOW
            | ControlTypes.EDITOR_SYM_SNAP_UI
            | ControlTypes.EDITOR_UNDO_REDO;

        private int colourMode = 0;
        private string[] colourModes = new string[] { "HSV", "RGB" };
        private bool UseRGB => colourMode == 1;

        private Vector2 presetColorScrollPos;
        private static GUIStyle nonWrappingLabelStyle;
        private static GUIStyle squareButtonStyle;
        private static GUIStyle colourSlotStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle textBoxStyle;
        private static bool showPresetColours = false;
        private static int groupIndex = 0;
        private static string groupName = "FULL";
        private string presetSaveString = "";
        private int deleteIndex;
        private bool deleteForm = false;
        private string deleteTitle;

        // Highlighting.
        public float rimFallOff = 1.5f;
        public float userHighlighterLimit = 1f;
        private bool mouseOffPart = false;
        private bool MouseOffPart
        {
            set
            {
                if (mouseOffPart == value)
                    return;

                mouseOffPart = value;
                OnMouseOverUI(value);
            }
        }

        // Toolbar.
        private ApplicationLauncherButton appLauncherButton;
        public bool guiEnabled = false;

        // Colour settings.
        private float[] speculars = { 127, 127, 127 };
        private float[] metals = { 0, 0, 0 };
        private float[] details = { 100, 100, 100 };

        private float[][] coloursHSV = new float[][]
        {
            new float[] { 0, 0, 255 },
            new float[] { 0, 0, 190 },
            new float[] { 0, 0, 130 },
        };

        private float[][] coloursRGB = new float[][]
        {
            new float[] { 255, 255, 255 },
            new float[] { 190, 190, 190 },
            new float[] { 130, 130, 130 },
        };

        private bool[] selectionState = new bool[] { true, false, false };

        // Parts.
        private List<RecolourablePartModule> switchers = new List<RecolourablePartModule>();
        internal static Dictionary<string, string> cache = new Dictionary<string, string> { };
        private List<Part> PartsList => HighLogic.LoadedSceneIsEditor ? EditorLogic.SortedShipList : FlightGlobals.ActiveVessel.Parts;
        private List<Part> selectedParts = new List<Part>();
        internal static FieldInfo textureSetsField = typeof(KSPTextureSwitch).GetField("textureSets", BindingFlags.Instance | BindingFlags.NonPublic);

        public string[] textureWords = new string[] { "mwnn", "paint", "recolour" };

        #endregion

        #region Main

        void Start()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                Destroy(this);
                return;
            }

            AddToolbarButton();
            windowID = GUIUtility.GetControlID(FocusType.Passive);

            if (clickBlocker == null)
                clickBlocker = ClickBlocker.Create(UIMasterController.Instance.mainCanvas, nameof(LazyPainter));

            GameEvents.onGameSceneLoadRequested.Add(OnSceneChange);
        }

        private void OnSceneChange(GameScenes data)
        {
            DisableGui();
        }

        private void OnGUI()
        {
            if (guiEnabled && UIMasterController.Instance.IsUIShowing)
                DrawGUI();

            //https://forum.kerbalspaceprogram.com/topic/203394-modders-notes-1120/
            //https://www.kerbalspaceprogram.com/ksp/api/class_k_s_p_1_1_u_i_1_1_app_u_i___data.html
            //https://www.kerbalspaceprogram.com/ksp/api/class_k_s_p_1_1_u_i_1_1_generic_app_frame.html

            // https://github.com/gotmachine/PhysicsHold/blob/master/PhysicsHold/DialogGuiVesselWidget.cs
            // https://github.com/gotmachine/PhysicsHold/blob/master/PhysicsHold/PhysicsHoldManager.cs
            // https://github.com/S-C-A-N/SCANsat/blob/e2d7bde255f5a263a04f5ace60af5c674e50516a/SCANsat/SCAN_Unity/SCAN_UI_Loader.cs#L1018
        }

        void Update()
        {
            if (!guiEnabled)
                return;

            Selection();

            MouseOffPart = Mouse.HoveredPart == null;
        }

        void OnDestroy()
        {
            InputLockManager.RemoveControlLock("LazyPainterGUILock");
            InputLockManager.RemoveControlLock("LazyPainterScrollLock");
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);

            if (appLauncherButton)
                ApplicationLauncher.Instance.RemoveModApplication(appLauncherButton);
        }

        #endregion

        #region Functions

        void Selection()
        {
            // todo: throw this all out.

            if (Input.GetKeyDown(KeyCode.A) && Input.GetKey(KeyCode.LeftControl))
            {
                selectedParts.Clear();
                selectedParts.AddRange(PartsList);
                UpdateHighlighting();
                UpdateSwitchers();
            }

            if (!(Input.GetMouseButtonUp(0) && !windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))))
                return;

            Part hoveredPart = Mouse.HoveredPart;

            if (hoveredPart == null)
            {
                // Clicked empty space without holding shift, clear selection
                if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                {
                    selectedParts.Clear();
                    UpdateHighlighting();
                    UpdateSwitchers();
                }

                return;
            }

            // Clicked an already selected part without holding anything down
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) && selectedParts.Count == 1 && selectedParts.Contains(hoveredPart))
            {
                selectedParts.Remove(hoveredPart);
                UpdateHighlighting();
                UpdateSwitchers();
                return;
            }

            // Control click and shift click combined
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                // Shift control click on an already selected part, remove all parts with the same name
                if (selectedParts.Contains(hoveredPart))
                {
                    selectedParts.RemoveAll(p => p.name == hoveredPart.name);
                }
                // Shift control click on a new part, add all parts with the same name
                else
                {
                    selectedParts.AddRange(PartsList.FindAll(p => p.name == hoveredPart.name));
                }

                UpdateHighlighting();
                UpdateSwitchers();
                return;
            }

            // Control alt click.
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftAlt))
            {
                RecolourablePartModule recolourablePartModule = GetRecolourableModule(hoveredPart);
                if (recolourablePartModule == null)
                    return;

                RecoloringData[] matchColour = recolourablePartModule.GetSectionColors();

                // Control alt click on an already selected part, remove all parts with the same colour.
                if (selectedParts.Contains(hoveredPart))
                {
                    List<RecolourablePartModule> matching = switchers.FindAll(s => s.GetSectionColors()[0].IsEqual(matchColour[0]));
                    selectedParts = selectedParts.Except(matching.Select(m => m.part)).ToList();
                }
                // Control alt click on a new part, add all parts with the same colour.
                else
                {
                    RecolourablePartModule module;

                    foreach (Part p in PartsList)
                    {
                        module = GetRecolourableModule(p);
                        if (module == null)
                            continue;

                        RecoloringData[] partColour = module.GetSectionColors();
                        if (partColour[0].IsEqual(matchColour[0]))
                            selectedParts.Add(p);
                    }
                }

                UpdateHighlighting();
                UpdateSwitchers();
                return;
            }

            // Alt click
            if (Input.GetKey(KeyCode.LeftAlt))
            {
                if (hoveredPart == null)
                    return;

                Eyedropper(Mouse.HoveredPart);
                return;
            }

            // Control click
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                // Control click on an already selected part, remove all parts with the same name
                if (selectedParts.Contains(hoveredPart))
                {
                    selectedParts.RemoveAll(p => p.name == hoveredPart.name);
                }
                // Control click on a new part, add all parts with the same name
                else
                {
                    selectedParts.Clear();
                    selectedParts.AddRange(PartsList.FindAll(p => p.name == hoveredPart.name));
                }

                UpdateHighlighting();
                UpdateSwitchers();
                return;
            }

            // Shift click
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                // Shift click on an already selected part, remove it
                if (selectedParts.Contains(hoveredPart))
                {
                    selectedParts.Remove(hoveredPart);
                }
                // Shift click on a new part, add it to the selection
                else
                {
                    selectedParts.Add(hoveredPart);
                }

                UpdateHighlighting();
                UpdateSwitchers();
                return;
            }

            // Clicked an unselected part without holding anything down, clear selection and select the part
            selectedParts.Clear();
            selectedParts.Add(hoveredPart);
            UpdateHighlighting();
            UpdateSwitchers();
        }

        void UpdateHighlighting()
        {
            IEnumerable<Part> nonselected = PartsList.Except(selectedParts);
            foreach (Part part in nonselected)
            {
                part.SetHighlightColor(Color.clear);
                part.SetHighlightType(Part.HighlightType.Disabled);
            }

            foreach (Part part in selectedParts)
            {
                part.SetHighlightColor(Color.white);
                part.SetHighlightType(Part.HighlightType.AlwaysOn);
            }
        }

        void OnMouseOverUI(bool over)
        {
            foreach (Part part in selectedParts)
            {
                part.mpb.SetColor(PropertyIDs._RimColor, over ? Color.clear : Color.white);
                part.GetPartRenderers().ToList().ForEach(r => r.SetPropertyBlock(part.mpb));
            }
        }

        void UpdateSwitchers()
        {
            switchers.Clear();

            KSPTextureSwitch[] textureSwitchers;
            TUPartVariant[] partVariants;

            foreach (Part part in selectedParts)
            {
                textureSwitchers = part.gameObject.GetComponents<KSPTextureSwitch>();
                if (textureSwitchers.Length > 0)
                {
                    switchers.AddRange(textureSwitchers.Select(s => new RecolourablePartModule(s)));
                }
                else if ((partVariants = part.gameObject.GetComponents<TUPartVariant>()).Length > 0)
                {
                    switchers.AddRange(partVariants.Select(p => new RecolourablePartModule(p)));
                }
            }
        }

        void EnableTextures(bool enable)
        {
            //https://github.com/shadowmage45/TexturesUnlimited/blob/ff1a460262d3aae884fb54fb32e7864b4255531b/Plugin/SSTUTools/KSPShaderTools/GUI/CraftRecolorGUI.cs#L573
            //https://github.com/shadowmage45/TexturesUnlimited/blob/ff1a460262d3aae884fb54fb32e7864b4255531b/Plugin/SSTUTools/KSPShaderTools/Module/KSPTextureSwitch.cs#L124
            //https://github.com/shadowmage45/TexturesUnlimited/blob/ff1a460262d3aae884fb54fb32e7864b4255531b/Plugin/SSTUTools/KSPShaderTools/Util/Utils.cs#L802

            if (!enable)
            {
                foreach (RecolourablePartModule switcher in switchers)
                    switcher.Disable();

                return;
            }

            string textureName;
            string partName;
            string[] foundTextures = new string[switchers.Count];
            string[] textureNames;
            string nameLower;

            for (int i = 0; i < switchers.Count; i++)
            {
                nameLower = switchers[i].CurrentTexture.ToLower();
                if (textureWords.Any(w => nameLower.Contains(w)))
                    continue;

                textureName = "";
                partName = switchers[i].CurrentTexture;
                if (partName == "")
                    partName = switchers[i].part.name;

                if (cache.TryGetValue(partName, out textureName))
                {
                    foundTextures[i] = textureName;

                    continue;
                }

                textureNames = switchers[i].GetTextures();

                foreach (string name in textureNames)
                {
                    nameLower = name.ToLower();
                    if (textureWords.Any(w => nameLower.Contains(w)))
                    {
                        textureName = name;
                        break;
                    }
                }

                if (textureName == "" || textureName == null)
                    continue;

                if (partName != "")
                    cache.Add(partName, textureName);

                foundTextures[i] = textureName;
            }

            Debug.Log("Finished looking for textures.");

            for (int i = 0; i < switchers.Count; i++)
            {
                if (foundTextures[i] == null)
                    continue;

                if (switchers[i].CurrentTexture == foundTextures[i])
                    continue;

                switchers[i].SetTexture(foundTextures[i]);
            }

            Debug.Log("Finished applying textures.");

            ApplyAll();
        }

        void ApplyAll()
        {
            int c;

            foreach (RecolourablePartModule switcher in switchers)
            {
                if (switcher == null)
                    continue;

                RecoloringData[] colours = switcher.GetSectionColors();

                for (int i = 0; i < colours.Length; i++)
                {
                    c = selectionState[i] ? i : ((i == 2 && selectionState[1]) ? 1 : 0);

                    colours[i].color = HSV255toRGB(coloursHSV[c]);
                    colours[i].metallic = metals[c] / 255;
                    colours[i].specular = speculars[c] / 255;
                    colours[i].detail = details[c] / 100;
                }

                switcher.SetSectionColors(colours);
            }
        }

        RecolourablePartModule GetRecolourableModule(Part part)
        {
            if (part.TryGetComponent(out KSPTextureSwitch switcher))
            {
                return new RecolourablePartModule(switcher);
            }
            else if (part.TryGetComponent(out TUPartVariant partVariant))
            {
                return new RecolourablePartModule(partVariant);
            }

            return null;
        }

        void Eyedropper(Part originalPart)
        {

            RecolourablePartModule switcher = GetRecolourableModule(originalPart);
            if (switcher == null)
                return;

            RecoloringData[] colours = switcher.GetSectionColors();

            coloursHSV = colours.Select(c => RGBtoHSV255(c.color)).ToArray();
            coloursRGB = coloursHSV.Select(c => HSV255toRGB255(c)).ToArray();

            metals = colours.Select(c => c.metallic * 255).ToArray();
            speculars = colours.Select(c => c.specular * 255).ToArray();
            details = colours.Select(c => c.detail * 100).ToArray();
            selectionState = new bool[] {
                true,
                !colours[0].IsEqual(colours[1]),
                !colours[1].IsEqual(colours[1]) && !colours[0].IsEqual(colours[2])
            };

            StartCoroutine(HighlightPart(originalPart));

            UpdateColourBoxes();
            ApplyAll();
        }

        IEnumerator HighlightPart(Part part)
        {
            part.highlighter.FlashingOn();

            yield return new WaitForSeconds(0.5f);

            part.highlighter.FlashingOff();
        }

        void SaveColour(string name)
        {
            // Add the custom colour to the preset group.

            RecoloringDataPreset preset = new RecoloringDataPreset();
            preset.color = HSV255toRGB(coloursHSV[editingColour]);
            preset.metallic = metals[editingColour] / 255;
            preset.specular = speculars[editingColour] / 255;
            //preset.detail = details[editingColour] / 100;
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
            colourPreset.SetValue("color", String.Join(", ", RGBtoRGB255(preset.color)), true);
            colourPreset.SetValue("metallic", Mathf.RoundToInt(preset.metallic * 255), true);
            colourPreset.SetValue("specular", Mathf.RoundToInt(preset.specular * 255), true);

            if (!groupsNode.GetValues("color").Contains(preset.name))
                groupsNode.AddValue("color", preset.name);

            file.Save(filePath);
        }

        void DeletePreset(int customIndex)
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

            Debug.Log("Delete Colour: " + name);

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

        void AddCustomGroup()
        {
            RecoloringDataPresetGroup customGroup = new RecoloringDataPresetGroup("Custom");
            customGroup.colors = new List<RecoloringDataPreset>();
            PresetColor.getGroupList().Add(customGroup);

            FieldInfo groupsField = typeof(PresetColor).GetField("presetGroups", BindingFlags.Static | BindingFlags.NonPublic);
            Dictionary<string, RecoloringDataPresetGroup> presetGroups = (Dictionary<string, RecoloringDataPresetGroup>)groupsField.GetValue(null);
            presetGroups.Add("Custom", customGroup);
        }

        #endregion

        #region Helper Functions

        private Color HSV255toRGBA(float[] HSV, float alpha)
        {
            Color rgb = HSV255toRGB(HSV);
            rgb.a = alpha;

            return rgb;
        }

        private Color HSV255toRGB(float[] HSV)
        {
            return Color.HSVToRGB(HSV[0] / 255, HSV[1] / 255, HSV[2] / 255);
        }

        static float[] RGBtoHSV255(Color rgb)
        {
            Color.RGBToHSV(rgb, out float h, out float s, out float v);
            return new float[] { h * 255, s * 255, v * 255 };
        }

        static float[] RGB255toHSV255(float[] RGB)
        {
            Color rgb = new Color(RGB[0] / 255, RGB[1] / 255, RGB[2] / 255);
            Color.RGBToHSV(rgb, out float h, out float s, out float v);

            return new float[] { Mathf.Round(h * 255), Mathf.Round(s * 255), Mathf.Round(v * 255) };
        }

        static float[] HSV255toRGB255(float[] HSV)
        {
            Color rgb = Color.HSVToRGB(HSV[0] / 255, HSV[1] / 255, HSV[2] / 255);

            return new float[] { Mathf.Round(rgb.r * 255), Mathf.Round(rgb.g * 255), Mathf.Round(rgb.b * 255) };
        }

        static int[] RGBtoRGB255(Color color)
        {
            return new int[] {
                Mathf.RoundToInt(color.r * 255),
                Mathf.RoundToInt(color.g * 255),
                Mathf.RoundToInt(color.b * 255)
            };
        }

        #endregion

        #region GUI

        public void DrawGUI()
        {
            // Keep window inside screen space.
            windowRect.position = new Vector2(
                Mathf.Clamp(windowRect.position.x, 0, Screen.width - windowRect.width),
                Mathf.Clamp(windowRect.position.y, 0, Screen.height - windowRect.height)
            );

            windowRect = GUILayout.Window(windowID, windowRect, FillWindow, "Lazy Painter", GUILayout.Height(1), GUILayout.Width(windowWidth));
            clickBlocker.UpdateRect(windowRect);
        }

        private void FillWindow(int windowID)
        {
            bool lockedScroll = false;
            if (windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
            {
                lockedScroll = true;
                scrollLock = true;
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, "LazyPainterScrollLock");
            }

            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), ""))
                DisableGui();

            if (boxStyle == null)
                InitStyles();

            if (GUI.Button(new Rect(windowRect.width - (18 * 2), 2, 16, 16), "?", questionStyle))
                showHelp = !showHelp;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            int partsCount = selectedParts.Count;
            switch (partsCount)
            {
                case 0:
                    GUILayout.Label("Nothing selected.", boxStyle);
                    break;
                case 1:
                    GUILayout.Label("1 part selected.", boxStyle);
                    break;
                default:
                    GUILayout.Label(partsCount + " parts selected.", boxStyle);
                    break;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Stock"))
                EnableTextures(false);

            if (GUILayout.Button("Paint"))
                EnableTextures(true);

            GUILayout.EndHorizontal();

            // Colour slot section.

            GUILayout.BeginVertical(boxStyle);
            int oldEditingColour = editingColour;
            string[] disabledStrings = new string[] { "", "", "" };
            editingColour = GUILayout.SelectionGrid(editingColour, disabledStrings, 3, GUILayout.Width(windowWidth), GUILayout.Height(50));

            if (GUI.changed && editingColour != 0)
            {
                if (Input.GetMouseButtonUp(1))
                {
                    selectionState[editingColour] = !selectionState[editingColour];
                    editingColour = editingColour == oldEditingColour ? 0 : oldEditingColour;
                }
                else if (!selectionState[editingColour])
                {
                    selectionState[editingColour] = true;
                }

                UpdateColourBoxes();
                ApplyAll();
            }

            int colourWidth = 85;
            int verticalOffset = 82; //79 54
            for (int i = 0; i < 3; i++)
                GUI.DrawTexture(new Rect(20 + (i * (16 + colourWidth)), verticalOffset, colourWidth, 39), colourTextures[i], ScaleMode.StretchToFill, true);


            // Colour slider section.

            bool update = false;

            GUILayout.BeginHorizontal();
            GUILayout.Space(100);
            colourMode = GUILayout.SelectionGrid(colourMode, colourModes, 2);
            GUILayout.Space(100);
            GUILayout.EndHorizontal();

            if (!UseRGB)
            {
                SliderSetting("Hue", ref coloursHSV[editingColour][0], 0, 255, 0, ref update);
                SliderSetting("Saturation", ref coloursHSV[editingColour][1], 0, 255, 0, ref update);
                SliderSetting("Value", ref coloursHSV[editingColour][2], 0, 255, 0, ref update);
            }
            else
            {
                SliderSetting("Red", ref coloursRGB[editingColour][0], 0, 255, 0, ref update);
                SliderSetting("Green", ref coloursRGB[editingColour][1], 0, 255, 0, ref update);
                SliderSetting("Blue", ref coloursRGB[editingColour][2], 0, 255, 0, ref update);
            }

            // Material slider section.

            GUI.color = Color.grey;
            GUILayout.Label("-------------");
            GUI.color = Color.white;

            SliderSetting("Specular", ref speculars[editingColour], 0, 255, 0, ref update);
            SliderSetting("Metallic", ref metals[editingColour], 0, 255, 0, ref update);
            SliderSetting("Detail", ref details[editingColour], 0, 500, 0, ref update);

            GUI.color = Color.grey;
            GUILayout.Label("-------------");
            GUI.color = Color.white;

            showPresetColours = GUILayout.Toggle(showPresetColours, "Colour Presets", buttonStyle);
            if (showPresetColours)
                DrawPresetSection(ref update);

            if (update)
            {
                if (UseRGB)
                    coloursHSV[editingColour] = RGB255toHSV255(coloursRGB[editingColour]);
                else
                    coloursRGB[editingColour] = HSV255toRGB255(coloursHSV[editingColour]);

                UpdateColourBoxes();
                ApplyAll();
            }

            GUILayout.EndVertical();

            if (showHelp)
            {
                GUILayout.BeginHorizontal(boxStyle);
                GUILayout.BeginHorizontal(boxStyle);
                GUILayout.Label("Click on parts to select them." +
                    "\n\n<b>Control click</b> a part to select all parts of that type. " +
                    "\n\n<b>Shift click</b> to add more parts to the selection. " +
                    "\n\n<b>Control alt click</b> a part to select all parts that share the same primary colour. " +
                    "\n\nPress <b>control + A</b> to select all parts. " +
                    "\n\nClick anywhere to clear the selection. " +
                    "\n\n<b>Alt click</b> a part to copy its colours to the palette. " +
                    "\n\nClick <b>'Paint'</b> to activate recolouring for the selected parts. " +
                    "\n\nRight click a colour slot to enable/disable it.");
                GUILayout.EndVertical();
                GUILayout.EndVertical();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 500));

            if (!lockedScroll && scrollLock)
            {
                InputLockManager.RemoveControlLock("LazyPainterScrollLock");
            }
        }

        private void InitStyles()
        {
            boxStyle = GUI.skin.GetStyle("Box");

            colourTextures = new Texture2D[]
            {
                new Texture2D(1, 1, TextureFormat.RGBA32, false),
                new Texture2D(1, 1, TextureFormat.RGBA32, false),
                new Texture2D(1, 1, TextureFormat.RGBA32, false),
            };

            UpdateColourBoxes();

            questionStyle = new GUIStyle(GUI.skin.GetStyle("Button"));
            questionStyle.fontSize = 10;
            questionStyle.alignment = TextAnchor.MiddleCenter;

            nonWrappingLabelStyle = new GUIStyle(GUI.skin.button);
            nonWrappingLabelStyle.wordWrap = false;
            nonWrappingLabelStyle.clipping = TextClipping.Overflow;
            nonWrappingLabelStyle.fontSize = 12;

            squareButtonStyle = new GUIStyle(GUI.skin.box);
            squareButtonStyle.fontSize = 200;
            squareButtonStyle.alignment = TextAnchor.MiddleCenter;

            buttonStyle = GUI.skin.button;

            colourSlotStyle = new GUIStyle(GUI.skin.button);
            colourSlotStyle.fontSize = 400;
            colourSlotStyle.alignment = TextAnchor.MiddleCenter;

            textBoxStyle = new GUIStyle(GUI.skin.textField);
            textBoxStyle.alignment = TextAnchor.MiddleCenter;
        }

        private void UpdateColourBoxes()
        {
            for (int i = 0; i < colourTextures.Length; i++)
            {
                colourTextures[i].SetPixel(0, 0, HSV255toRGBA(coloursHSV[i], selectionState[i] ? 1f : 0.15f));
                colourTextures[i].Apply();
            }
        }

        void SliderSetting(string name, ref float setting, float min, float max, int rounding, ref bool update)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(3);
            GUILayout.Label(name, GUILayout.Width(70));

            float old = setting;

            // Slider
            setting = (float)Math.Round(GUILayout.HorizontalSlider(setting, min, max), rounding);

            // Box
            string text = GUILayout.TextField(setting.ToString(), textBoxStyle, GUILayout.Width(windowWidth / 8));
            if (float.TryParse(text, out float result))
                setting = result;
            else if (text == "")
                setting = 0;

            update = update || (old != setting);

            GUILayout.Space(3);
            GUILayout.EndHorizontal();
        }

        void DrawPresetSection(ref bool update)
        {
            // Group selection.

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("<", GUILayout.Width(20)))
            {
                groupIndex--;
                List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                if (groupIndex < 0) { groupIndex = gs.Count - 1; }
                groupName = gs[groupIndex].name;
            }

            GUILayout.Box(groupName, GUILayout.Width(100));

            if (GUILayout.Button(">", GUILayout.Width(20)))
            {
                groupIndex++;
                List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                if (groupIndex >= gs.Count) { groupIndex = 0; }
                groupName = gs[groupIndex].name;
            }

            // Saving.

            if (groupName == "Custom")
            {
                presetSaveString = GUILayout.TextField(presetSaveString);
            }
            else
            {
                GUI.enabled = false;
                GUILayout.TextField("");
            }

            if (GUILayout.Button("Save", GUILayout.Width(50)))
                SaveColour(presetSaveString);

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // Preset colours.

            GUILayout.BeginVertical(boxStyle);

            presetColorScrollPos = GUILayout.BeginScrollView(presetColorScrollPos, false, true, GUILayout.Height(200f));
            Color old = GUI.color;
            Color guiColor = old;
            List<RecoloringDataPreset> presetColors = PresetColor.getColorList(groupName);

            GUILayout.BeginHorizontal();

            int len = presetColors.Count;
            for (int i = 0; i < len; i++)
            {
                if (i > 0 && i % 2 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }

                if (GUILayout.Button(Truncate(presetColors[i].title, 18), nonWrappingLabelStyle, GUILayout.Width(112)))
                {
                    if (Input.GetMouseButtonUp(1) && groupName == "Custom")
                    {
                        deleteForm = true;
                        deleteIndex = i;
                        deleteTitle = presetColors[i].title;
                    }
                    //else if (Input.GetKey(KeyCode.LeftAlt) && groupName == "Custom")
                    //{
                    //}
                    else
                    {
                        EnableTextures(true);

                        if (groupName == "Custom")
                            presetSaveString = presetColors[i].title;

                        RecoloringData editingColor = presetColors[i].getRecoloringData();

                        coloursHSV[editingColour] = RGBtoHSV255(editingColor.color);
                        coloursRGB[editingColour] = HSV255toRGB255(coloursHSV[editingColour]);
                        speculars[editingColour] = editingColor.specular * 255f;
                        metals[editingColour] = editingColor.metallic * 255f;
                        //details[editingColour] = editingColor.detail * 100f;

                        update = true;
                    }
                }

                guiColor = presetColors[i].color;
                guiColor.a = 1f;
                GUI.color = guiColor;
                GUILayout.Box("■", squareButtonStyle, GUILayout.Width(20), GUILayout.Height(20));
                GUI.color = old;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUI.color = old;
            GUILayout.EndVertical();

            if (deleteForm)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Delete the preset '{deleteTitle}'?");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Yes"))
                {
                    DeletePreset(deleteIndex);
                    deleteForm = false;
                }
                if (GUILayout.Button("No"))
                {
                    deleteForm = false;
                }
                GUILayout.EndHorizontal();
            }
        }

        public string Truncate(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max - 3).Trim() + "...";
        }

        public void AddToolbarButton()
        {
            if (appLauncherButton != null)
                return;

            ApplicationLauncher.AppScenes scenes = ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.FLIGHT;
            Texture buttonTexture = GameDatabase.Instance.GetTexture("LazyPainter/Textures/icon", false);
            appLauncherButton = ApplicationLauncher.Instance.AddModApplication(EnableGui, DisableGui, null, null, null, null, scenes, buttonTexture);
        }

        void ToggleGui()
        {
            if (guiEnabled)
                DisableGui();
            else
                EnableGui();
        }

        public void LockUI(bool locked)
        {
            // Lock controls.

            if (locked)
                InputLockManager.SetControlLock(controlLock, "LazyPainterGUILock");
            else
                InputLockManager.RemoveControlLock("LazyPainterGUILock");

            // Cause the parts list to slide out.

            if (HighLogic.LoadedSceneIsEditor)
            {
                EditorLogic.fetch.UpdateUI();
                string stateType = locked ? "Out" : "In";

                EditorToolsUI toolsUI = EditorLogic.fetch.toolsUI;
                if (toolsUI != null && toolsUI.gameObject.activeInHierarchy)
                    toolsUI.panelTransition?.Transition(stateType);

                EditorPartList.Instance.GetComponent<UIPanelTransition>()?.Transition(stateType);
                EditorPanels.Instance.searchField.Transition(stateType);
            }
        }

        void EnableGui()
        {
            LockUI(true);

            userHighlighterLimit = Highlighter.HighlighterLimit;
            Highlighter.HighlighterLimit = 1f;

            List<Part> parts = PartsList;
            foreach (Part part in parts)
            {
                part.SetHighlightType(Part.HighlightType.Disabled);

                part.mpb.SetFloat(PropertyIDs._RimFalloff, rimFallOff);
                part.GetPartRenderers().ToList().ForEach(r => r.SetPropertyBlock(part.mpb));
            }

            // check if the custom group exists
            if (!PresetColor.getGroupList().Exists(x => x.name == "Custom"))
                AddCustomGroup();

            guiEnabled = true;

            if (appLauncherButton.toggleButton.CurrentState == UIRadioButton.State.False)
                appLauncherButton.SetTrue(false);

            clickBlocker.Blocking = true;
        }

        void DisableGui()
        {
            guiEnabled = false;

            LockUI(false);
            InputLockManager.RemoveControlLock("LazyPainterScrollLock");
            Highlighter.HighlighterLimit = userHighlighterLimit;

            List<Part> parts = PartsList;
            foreach (Part part in parts)
            {
                part.mpb.SetFloat(PropertyIDs._RimFalloff, 2f);
                part.SetHighlightDefault();
            }

            if (appLauncherButton.toggleButton.CurrentState == KSP.UI.UIRadioButton.State.True)
                appLauncherButton.SetFalse(false);

            clickBlocker.Blocking = false;
        }

        #endregion
    }
}