using KSP.UI;
using KSP.UI.Screens;
using KSPShaderTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LazyPainter
{
    [Settings(category = "UI")]
    public class LazyPainterIMGUI : MonoBehaviour
    {
        public static string windowTitle;

        private bool init = false;
        public LazyPainter lp;
        private ClickBlocker clickBlocker;
        private ApplicationLauncherButton appLauncherButton;

        // Window.
        private static Rect windowRect = new Rect(Screen.width * 0.04f, Screen.height * 0.1f, 0, 0);
        private int windowID;
        public static int windowWidth = 300;

        // Styles.
        private static GUIStyle boxStyle;
        private static GUIStyle questionStyle;
        private static GUIStyle nonWrappingLabelStyle;
        private static GUIStyle squareButtonStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle textBoxStyle;
        private static GUIStyle topButtonStyle;

        // Scroll.
        private Vector2 presetColorScrollPos;
        private static bool scrollLock = false;

        // Show/hide.
        public static bool showPresetColours = false;
        public bool showHelp = false;
        public bool showDebug = false;
        public bool showSettings = false;

        // Colour slot textures.
        private static Texture2D[] colourTextures;

        // HSV or RGB.
        private int colourMode = 0;
        private string[] colourModes = new string[] { "HSV", "RGB" };
        private bool UseRGB => colourMode == 1;

        // Preset browser.
        private static int groupIndex = 0;
        private static string groupName = "FULL";
        private string presetSaveString = "";
        private int deleteIndex;
        private bool deleteForm = false;
        private string deleteTitle;

        // Serialised settings.
        [Setting] public static bool display255 = true;
        [Setting] public static bool buttonInFlight = true;

        public static LazyPainterIMGUI Create(LazyPainter lazyPainter)
        {
            LazyPainterIMGUI gui = lazyPainter.gameObject.AddComponent<LazyPainterIMGUI>();
            gui.lp = lazyPainter;
            gui.Init();
            gui.enabled = false;

            return gui;
        }

        private void Init()
        {
            AddToolbarButton();
            windowID = GUIUtility.GetControlID(FocusType.Passive);

            if (clickBlocker == null)
                clickBlocker = ClickBlocker.Create(UIMasterController.Instance.mainCanvas, nameof(LazyPainter));

            GameEvents.onEditorScreenChange.Add(OnEditorScreenChange);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            GameEvents.onEditorScreenChange.Add(OnEditorScreenChange);

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            windowTitle = $"Lazy Painter v{version.Major}.{version.Minor}.{version.Build}";

            init = true;
        }

        protected void Update()
        {
            if (HighLogic.LoadedSceneIsFlight && GameSettings.PAUSE.GetKeyUp())
                Close();
        }

        protected void OnGUI()
        {
            if (UIMasterController.Instance.IsUIShowing)
                DrawGUI();
        }

        protected void OnEnable()
        {
            if (!init)
                return;

            if (appLauncherButton && appLauncherButton.toggleButton.CurrentState == UIRadioButton.State.False)
                appLauncherButton.SetTrue(false);

            clickBlocker.Blocking = true;
            OnUIChanged(true);

            lp.Setup();
        }

        protected void OnDisable()
        {
            if (!init)
                return;

            if (appLauncherButton && appLauncherButton.toggleButton.CurrentState == UIRadioButton.State.True)
                appLauncherButton.SetFalse(false);

            clickBlocker.Blocking = false;
            InputLockManager.RemoveControlLock("LazyPainterScrollLock");
            OnUIChanged(false);

            lp.Cleanup();
            GlobalSettings.Save();
        }

        protected void OnDestroy()
        {
            GameEvents.onEditorScreenChange.Remove(OnEditorScreenChange);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);
            GameEvents.onEditorScreenChange.Remove(OnEditorScreenChange);

            InputLockManager.RemoveControlLock("LazyPainterScrollLock");

            if (appLauncherButton)
                ApplicationLauncher.Instance.RemoveModApplication(appLauncherButton);

            Destroy(clickBlocker);
        }

        public void Open() =>
            enabled = true;

        public void Close() =>
            enabled = false;

        private void OnEditorScreenChange(EditorScreen data) =>
            appLauncherButton?.gameObject.SetActive(data == EditorScreen.Parts);

        private void OnEditorRestart() =>
            Close();

        private void OnSceneChange(GameScenes data) =>
            Close();

        private void OnUIChanged(bool enabled)
        {
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.editorScreen == EditorScreen.Parts)
            {
                EditorLogic.fetch.UpdateUI();
                string stateType = enabled ? "Out" : "In";

                EditorToolsUI toolsUI = EditorLogic.fetch.toolsUI;
                if (toolsUI != null && toolsUI.gameObject.activeInHierarchy)
                    toolsUI.panelTransition?.Transition(stateType);

                EditorPartList.Instance.GetComponent<UIPanelTransition>()?.Transition(stateType);
                EditorPanels.Instance.searchField.Transition(stateType);
            }
        }

        public void DrawGUI()
        {
            // Keep window inside screen space.
            windowRect.position = new Vector2(
                Mathf.Clamp(windowRect.position.x, 0, Screen.width - windowRect.width),
                Mathf.Clamp(windowRect.position.y, 0, Screen.height - windowRect.height)
            );

            windowRect = GUILayout.Window(windowID, windowRect, FillWindow, windowTitle, GUILayout.Height(1), GUILayout.Width(windowWidth));
            clickBlocker.UpdateRect(windowRect);
        }

        public bool MouseOverUI() =>
            windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));

        private void HelpSection()
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
            GUILayout.EndHorizontal();
            GUILayout.EndHorizontal();
        }

        private void SettingsSection()
        {
            GUILayout.BeginVertical(boxStyle);
            display255 = GUILayout.Toggle(display255, "Display values from 0 to 255.");
            buttonInFlight = GUILayout.Toggle(buttonInFlight, "Show app button in flight (after next scene load).");
            GUILayout.EndVertical();
        }

        private void DebugSection()
        {
            GUILayout.Label("Selection Debug:");

            foreach (RecolourableSection section in lp.selectedSections)
            {
                if (section == null)
                    continue;

                GUILayout.Label($"{section.name} on {section.host.part.partInfo.title}", boxStyle);
            }
        }

        private void FillWindow(int windowID)
        {
            // Scroll lock when hovering over the window.

            bool lockedScroll = false;
            if (windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
            {
                lockedScroll = true;
                scrollLock = true;
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, "LazyPainterScrollLock");
            }

            // Close button.
            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), "x", topButtonStyle))
                Close();

            // Style initialisation.
            if (boxStyle == null)
                InitStyles();

            // Settings button.
            if (GUI.Button(new Rect(windowRect.width - (18 * 2), 2, 16, 16), "s", topButtonStyle))
            {
                showSettings = !showSettings;
                showHelp = false;
            }

            // Help button.
            if (GUI.Button(new Rect(windowRect.width - (18 * 3), 2, 16, 16), "i", questionStyle))
            {
                showHelp = !showHelp;
                showSettings = false;
            }

            // Main section or loading screen.
            if (lp.Ready)
                MainSection();
            else
                LoadingScreen();

            // Help section.
            if (showHelp)
                HelpSection();

            // Settings.
            if (showSettings)
                SettingsSection();

            // Debug section.
            //if (GUILayout.Button(showDebug ? "Show Debug" : "Hide Debug"))
            //    showDebug = !showDebug;

            if (showDebug && lp.selectedSections.Count > 0)
                DebugSection();

            // End window and release scroll lock.
            GUI.DragWindow(new Rect(0, 0, 10000, 500));

            if (!lockedScroll && scrollLock)
            {
                InputLockManager.RemoveControlLock("LazyPainterScrollLock");
            }
        }

        private void MainSection()
        {
            // Selection count.

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            int partsCount = lp.selectedSections.Count;
            switch (partsCount)
            {
                case 0:
                    GUILayout.Label("Nothing selected.", boxStyle);
                    break;
                case 1:
                    GUILayout.Label("1 section selected.", boxStyle);
                    break;
                default:
                    GUILayout.Label(partsCount + " sections selected.", boxStyle);
                    break;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Stock/Paint button section.

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Stock"))
                lp.EnableRecolouring(false);

            if (GUILayout.Button("Paint"))
                lp.EnableRecolouring(true);

            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(boxStyle);

            int oldEditingColour = lp.editingColour;
            string[] disabledStrings = new string[] { "", "", "" };
            lp.editingColour = GUILayout.SelectionGrid(lp.editingColour, disabledStrings, 3, GUILayout.Width(windowWidth), GUILayout.Height(50));

            if (GUI.changed && lp.editingColour != 0)
            {
                if (Input.GetMouseButtonUp(1))
                {
                    lp.selectionState[lp.editingColour] = !lp.selectionState[lp.editingColour];
                    lp.editingColour = lp.editingColour == oldEditingColour ? 0 : oldEditingColour;
                }
                else if (!lp.selectionState[lp.editingColour])
                {
                    lp.selectionState[lp.editingColour] = true;
                }

                UpdateColourBoxes();
                lp.ApplyRecolouring();
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

            ModalColour editingColour = lp.colourData[lp.editingColour];
            float scalar = display255 ? 255 : 1;

            if (!UseRGB)
            {
                HSV hsv = editingColour.HSV;
                SliderSetting("Hue", ref hsv.hue, 0, 1f, ref update, scalar);
                SliderSetting("Saturation", ref hsv.saturation, 0, 1f, ref update, scalar);
                SliderSetting("Value", ref hsv.value, 0, 1f, ref update, scalar);
                if (update) editingColour.HSV = hsv;
            }
            else
            {
                Color colour = editingColour.Colour;
                SliderSetting("Red", ref colour.r, 0, 1f, ref update, scalar);
                SliderSetting("Green", ref colour.g, 0, 1f, ref update, scalar);
                SliderSetting("Blue", ref colour.b, 0, 1f, ref update, scalar);
                if (update) editingColour.Colour = colour;
            }

            // Material slider section.

            GUI.color = Color.grey;
            GUILayout.Label("-------------");
            GUI.color = Color.white;

            SliderSetting("Specular", ref editingColour.specular, 0, 1f, ref update, scalar);
            SliderSetting("Metallic", ref editingColour.metallic, 0, 1f, ref update, scalar);
            SliderSetting("Detail", ref editingColour.detail, 0, 5f, ref update, display255 ? 100 : 1);

            GUI.color = Color.grey;
            GUILayout.Label("-------------");
            GUI.color = Color.white;

            // Colour preset section.

            showPresetColours = GUILayout.Toggle(showPresetColours, "Colour Presets", buttonStyle);
            if (showPresetColours)
                DrawPresetSection(ref update, ref editingColour);

            if (update)
            {
                lp.colourData[lp.editingColour] = editingColour;
                UpdateColourBoxes();
                lp.ApplyRecolouring();
            }

            GUILayout.EndVertical();
        }

        private void LoadingScreen()
        {
            GUILayout.BeginVertical(boxStyle);

            CenteredLabel($"Loading... {Mathf.RoundToInt(lp.setupProgress * 100)}%");
            if (lp.currentSetupPart != null)
                CenteredLabel($"{lp.currentSetupPart.partInfo.title}");

            GUILayout.EndVertical();
        }

        private void CenteredLabel(string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(text);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
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

            topButtonStyle = new GUIStyle(GUI.skin.GetStyle("Button"))
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow
            };

            questionStyle = new GUIStyle(topButtonStyle)
            {
                contentOffset = new Vector2(1, 0)
            };

            nonWrappingLabelStyle = new GUIStyle(GUI.skin.button)
            {
                wordWrap = false,
                clipping = TextClipping.Overflow,
                fontSize = 12
            };

            squareButtonStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 200,
                alignment = TextAnchor.MiddleCenter
            };

            buttonStyle = GUI.skin.button;

            textBoxStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }

        public void Refresh()
        {
            UpdateColourBoxes();
        }

        private void UpdateColourBoxes()
        {
            for (int i = 0; i < colourTextures.Length; i++)
            {
                Color colour = lp.colourData[i].Colour;
                colour.a = lp.selectionState[i] ? 1f : 0.15f;

                colourTextures[i].SetPixel(0, 0, colour);
                colourTextures[i].Apply();
            }
        }

        private void SliderSetting(string name, ref float setting, float min, float max, ref bool update, float scalar = 1)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(3);
            GUILayout.Label(name, GUILayout.Width(70));

            float old = setting;

            // Slider
            setting = (float)Math.Round(GUILayout.HorizontalSlider(setting, min, max), 3);

            // Box
            string text = GUILayout.TextField((setting * scalar).ToString(scalar > 1 ? "N0" : "N2"), textBoxStyle, GUILayout.Width(windowWidth / 8));
            if (float.TryParse(text, out float result))
                setting = (float)Math.Round(result / scalar, 3);
            else if (text == "")
                setting = 0;

            update = update || (old != setting);

            GUILayout.Space(3);
            GUILayout.EndHorizontal();
        }

        private void DrawPresetSection(ref bool update, ref ModalColour editingColour)
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
                Presets.SaveColour(presetSaveString, lp.ExportColourPreset());

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
                        lp.EnableRecolouring(true, false);

                        if (groupName == "Custom")
                            presetSaveString = presetColors[i].title;

                        editingColour = presetColors[i].getRecoloringData();
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
                    Presets.DeletePreset(deleteIndex);
                    deleteForm = false;
                }
                if (GUILayout.Button("No"))
                {
                    deleteForm = false;
                }
                GUILayout.EndHorizontal();
            }
        }

        public static string Truncate(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max - 3).Trim() + "...";
        }

        public void AddToolbarButton()
        {
            if (HighLogic.LoadedSceneIsFlight && !buttonInFlight)
                return;

            if (appLauncherButton != null)
                return;

            ApplicationLauncher.AppScenes scenes = ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.FLIGHT;
            Texture buttonTexture = GameDatabase.Instance.GetTexture("LazyPainter/Textures/icon", false);
            appLauncherButton = ApplicationLauncher.Instance.AddModApplication(Open, Close, null, null, null, null, scenes, buttonTexture);
        }
    }
}
