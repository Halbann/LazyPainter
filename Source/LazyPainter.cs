using Highlighting;
using KSPShaderTools;
using System;
using System.Collections;
using System.Collections.Generic;
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

        // Colour settings.

        public ModalColour[] colourData = new ModalColour[]
        {
            new ModalColour(XKCDColors.PaleGrey, 0.5f, 0, 1f),
            new ModalColour(XKCDColors.GreyBlue, 0.5f, 0, 1f),
            new ModalColour(XKCDColors.DarkGrey, 0.5f, 0, 1f)
        };

        public bool[] selectionState = new bool[] { true, false, false };
        public int editingColour = 0;

        // Parts.
        public bool Ready { get; private set; }
        public float setupProgress = 0;
        public static bool yieldOnLoad = true;
        public Part currentSetupPart;
        private Coroutine setupRoutine;

        private float previousTimescale = 1f;
        private Vector3 previousRbVelocity;
        private float userHighlighterLimit;
        private bool userInflightHighlight;

        public LazyPainterIMGUI imgui;
        private bool mouseOverVessel = false;
        private float lastClickTime = 0;

        public static bool noRecolourableTextureSetsDetected = (TexturesUnlimitedLoader.loadedTextureSets?.Count ?? 0) < 1
            || !TexturesUnlimitedLoader.loadedTextureSets.Any(s => s.Value.supportsRecoloring);

        public static bool texturesUnlimitedLoaded = false;
        public static bool texturesUnlimitedCorrectVersion = false;

        private List<Part> PartsList => HighLogic.LoadedSceneIsEditor ? EditorLogic.fetch.ship.parts : FlightGlobals.ActiveVessel.Parts;

        public Dictionary<Part, RecolourablePart> allRecolourables = new Dictionary<Part, RecolourablePart>();
        public HashSet<RecolourableSection> allSections = new HashSet<RecolourableSection>();
        public readonly HashSet<RecolourableSection> selectedSections = new HashSet<RecolourableSection>();
        public readonly HashSet<RecolourableSection> deselectionQueue = new HashSet<RecolourableSection>();

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

        #endregion

        #region Main

        protected void Start()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                Destroy(this);
                return;
            }

            imgui = LazyPainterIMGUI.Create(this);

            if (!PresetColor.getGroupList().Exists(x => x.name == "Custom"))
                Presets.AddCustomGroup();

            CheckForTexturesUnlimited();
        }

        private void CheckForTexturesUnlimited()
        {
            var dependency = new KSPAssemblyDependency("TexturesUnlimited", 1, 6, 3);
            AssemblyLoader.LoadedAssembly tuAssembly = null;

            foreach (var loadedAssembly in AssemblyLoader.loadedAssemblies)
            {
                if (loadedAssembly.name == "TexturesUnlimited")
                {
                    tuAssembly = loadedAssembly;
                    texturesUnlimitedLoaded = true;

                    if ((loadedAssembly.versionMajor > dependency.versionMajor) || (loadedAssembly.versionMajor == dependency.versionMajor && (loadedAssembly.versionMinor > dependency.versionMinor || (loadedAssembly.versionMinor == dependency.versionMinor && loadedAssembly.versionRevision >= dependency.versionRevision))))
                    {
                        texturesUnlimitedCorrectVersion = true;
                        return;
                    }
                }
            }

            string tuURL = @"https://github.com/KSPModStewards/TexturesUnlimited/releases";

            if (texturesUnlimitedLoaded)
            {
                string requiredVersion = $"{dependency.versionMajor}.{dependency.versionMinor}.{dependency.versionRevision}";
                string currentVersion = $"{tuAssembly.versionMajor}.{tuAssembly.versionMinor}.{tuAssembly.versionRevision}";

                Debug.LogError($"[LazyPainter]: TexturesUnlimited version {requiredVersion} or higher is required, but version {currentVersion} is loaded. Please update TexturesUnlimited to the latest version from {tuURL}.");
            }
            else
                Debug.LogError($"[LazyPainter]: TexturesUnlimited not found. Please install TexturesUnlimited from {tuURL}.");
        }

        protected void Update()
        {
            if (!Ready)
                return;

            Selection();
            MouseOverVessel(Mouse.HoveredPart != null);
        }

        private void MouseOverVessel(bool over)
        {
            if (mouseOverVessel == over)
                return;

            mouseOverVessel = over;

            foreach (RecolourableSection section in selectedSections)
                section.Glow(over);
        }

        protected void OnDestroy()
        {
            InputLockManager.RemoveControlLock("LazyPainterLock");
            InputLockManager.RemoveControlLock("LazyPainterFlightLock");
        }

        public void Setup()
        {
            if (Ready)
                return;

            userHighlighterLimit = Highlighter.HighlighterLimit;
            Highlighter.HighlighterLimit = 1f;
            userInflightHighlight = GameSettings.INFLIGHT_HIGHLIGHT;
            GameSettings.INFLIGHT_HIGHLIGHT = true;
            foreach (Part part in PartsList)
                part.SetHighlightType(Part.HighlightType.Disabled);

            if (HighLogic.LoadedSceneIsFlight)
            {
                previousTimescale = Time.timeScale;
                Time.timeScale = 0f;
                GameEvents.onGamePause.Fire();
                KinematicRigidbodies(true);
            }

            LockUI(true);

            if (setupRoutine == default)
                setupRoutine = StartCoroutine(SetupRoutine());
        }

        private IEnumerator SetupRoutine()
        {
            IEnumerator compilePartsEnumerator = Loading.FrameUnlockedCoroutine(PrepareParts());
            while (compilePartsEnumerator.MoveNext())
                yield return null;

            setupRoutine = default;
            Ready = true;
        }

        private IEnumerator PrepareParts()
        {
            int count = 0;
            int total = PartsList.Count;
            int capacity = PartsList.Count;

            // get dictionary count via reflection
            FieldInfo entriesField = typeof(Dictionary<Part, RecolourablePart>).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
            int entries = ((ICollection)entriesField.GetValue(allRecolourables))?.Count ?? 0;

            // Pre-allocate dictionary if it's too small.
            if (entries < capacity)
                allRecolourables = new Dictionary<Part, RecolourablePart>(capacity);

            foreach (Part part in PartsList)
            {
                currentSetupPart = part;
                RecolourablePart recolour = RecolourablePart.Create(part);

                count++;
                setupProgress = (float)count / total;
                if (yieldOnLoad)
                    yield return null;

                if (recolour == null)
                    continue;

                allRecolourables.Add(part, recolour);
            }

            allSections = new HashSet<RecolourableSection>(allRecolourables.Values.Sum(p => p.sections.Length));
            foreach (RecolourablePart recolour in allRecolourables.Values)
                foreach (RecolourableSection section in recolour.sections)
                    allSections.Add(section);
        }

        public void Cleanup()
        {
            if (!Ready || PartsList == null)
                return;

            Highlighter.HighlighterLimit = userHighlighterLimit;
            GameSettings.INFLIGHT_HIGHLIGHT = userInflightHighlight;

            foreach (Part part in PartsList)
                part.SetHighlightDefault();

            if (setupRoutine != default)
                StopCoroutine(setupRoutine);

            setupRoutine = default;
            Ready = false;
            setupProgress = 0;

            //foreach (RecolourablePart recolour in allRecolourableParts)
            foreach (RecolourablePart recolour in allRecolourables.Values)
                Destroy(recolour);

            allRecolourables.Clear();
            allSections.Clear();
            selectedSections.Clear();

            LockUI(false);

            if (HighLogic.LoadedSceneIsFlight)
            {
                KinematicRigidbodies(false);
                Time.timeScale = previousTimescale;
                GameEvents.onGameUnpause.Fire();
                GameEvents.onVesselWasModified.Fire(FlightGlobals.ActiveVessel);
            }
        }

        private void LockUI(bool locked)
        {
            // Lock controls.

            if (locked)
                InputLockManager.SetControlLock(controlLock, "LazyPainterLock");
            else
                InputLockManager.RemoveControlLock("LazyPainterLock");

            if (HighLogic.LoadedSceneIsFlight)
            {
                // Hide the flight UI.

                if (locked)
                    InputLockManager.SetControlLock(ControlTypes.PAUSE | ControlTypes.STAGING | ControlTypes.VESSEL_SWITCHING | ControlTypes.MAP_TOGGLE, "LazyPainterFlightLock");
                else
                    InputLockManager.RemoveControlLock("LazyPainterFlightLock");

                UIPartActionController.Instance.Show(!locked);

                foreach (GameObject element in ActionGroupsFlightController.Instance.flightUI)
                    element.SetActive(!locked);

                if (PartItemTransfer.Instance)
                    PartItemTransfer.Instance.Dismiss(PartItemTransfer.DismissAction.Cancelled, null);

                if (CrewHatchController.fetch)
                    CrewHatchController.fetch.ShowHatchTooltip(!locked);
            }
        }

        private void KinematicRigidbodies(bool enable)
        {
            foreach (Part part in PartsList)
                if (part.rb)
                {
                    part.rb.isKinematic = enable;
                    if (!enable)
                        part.rb.velocity = previousRbVelocity;
                }

            previousRbVelocity = FlightGlobals.ActiveVessel.rb_velocity;
        }

        #endregion

        #region Functions

        public void SelectAll()
        {
            DeselectAll();

            foreach (RecolourablePart recolPart in allRecolourables.Values)
                selectedSections.UnionWith(recolPart.sections);

            UpdateHighlighting();
        }

        public void DeselectAll() =>
            selectedSections.Clear();

        public void Select(RecolourableSection section) =>
            section.Selection(s => selectedSections.Add(s));

        public void EnqueueDeselect(RecolourableSection section) =>
            section.Selection(s => deselectionQueue.Add(s));

        public void DoDeselect()
        {
            selectedSections.ExceptWith(deselectionQueue);
            deselectionQueue.Clear();
        }

        private struct ModifierState
        {
            public bool ctrl;
            public bool shift;
            public bool alt;

            public static ModifierState Current()
            {
                ModifierState state = new ModifierState();
                state.ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                state.shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                state.alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

                return state;
            }

            public bool Equals(bool ctrl, bool shift, bool alt)
            {
                return this.ctrl == ctrl && this.shift == shift && this.alt == alt;
            }
        }

        private void ForEachMatchingSection(IEnumerable list, bool findStock, RecoloringData matchColour, Action<RecolourableSection> del)
        {
            foreach (RecolourableSection section in list)
            {
                bool isStock = !section.RecolouringEnabled;
                if (!findStock && isStock)
                    continue;

                if (findStock && isStock || !findStock && section.module.getSectionColors(string.Empty)[0].IsEqual(matchColour))
                    del(section);
            }
        }

        private void Selection()
        {
            // todo: throw this all out.

            if (Input.GetKeyDown(KeyCode.A) && Input.GetKey(KeyCode.LeftControl))
            {
                SelectAll();
                return;
            }

            if (!Input.GetMouseButtonUp(0) || imgui.MouseOverUI())
                return;

            ModifierState modifiers = ModifierState.Current();
            Part hoveredPart = Mouse.HoveredPart;

            if (hoveredPart == null)
            {
                // Clicked empty space without holding shift, clear selection
                if (!modifiers.shift)
                {
                    DeselectAll();
                    UpdateHighlighting();
                }

                return;
            }

            if (!allRecolourables.TryGetValue(hoveredPart, out RecolourablePart hoveredRecolourablePart))
                return;

            hoveredRecolourablePart.GetSection(out RecolourableSection hoveredSection);
            if (hoveredSection == null)
                return;

            // Clicked an already selected part without holding anything down
            if (modifiers.Equals(false, false, false) && selectedSections.Count == 1 && selectedSections.Contains(hoveredSection))
            {
                foreach (RecolourableSection section in hoveredSection.host.sections)
                    Select(section);

                UpdateHighlighting();
                return;
            }

            // Control alt click.
            if (modifiers.Equals(true, false, true))
            {
                bool findStock = !hoveredSection.RecolouringEnabled;
                RecoloringData matchColour = hoveredSection.module.getSectionColors(string.Empty)[0];

                // Control alt click on an already selected part, remove all parts with the same colour.
                if (selectedSections.Contains(hoveredSection))
                {
                    ForEachMatchingSection(selectedSections, findStock, matchColour, EnqueueDeselect);
                    DoDeselect();
                }
                // Control alt click on a new part, add all parts with the same colour.
                else
                    ForEachMatchingSection(allSections, findStock, matchColour, Select);

                UpdateHighlighting();
                return;
            }

            // Alt click.
            if (modifiers.Equals(false, false, true))
            {
                Eyedropper(hoveredSection);
                return;
            }

            float previousClickTime = lastClickTime;
            lastClickTime = Time.realtimeSinceStartup;

            // Control click or control shift click.
            if (modifiers.Equals(true, false, false) || modifiers.Equals(true, true, false))
            {
                // control click on an already selected part, remove all parts with the same name
                if (selectedSections.Contains(hoveredSection))
                {
                    foreach (RecolourableSection section in selectedSections)
                        if (section.code.Equals(hoveredSection.code, StringComparison.OrdinalIgnoreCase))
                            EnqueueDeselect(section);

                    DoDeselect();
                }
                // control click on a new part, select all parts with the same name
                else
                {
                    if (!modifiers.shift)
                        DeselectAll();

                    foreach (RecolourableSection section in allSections)
                        if (section.code.Equals(hoveredSection.code, StringComparison.OrdinalIgnoreCase))
                            Select(section);
                }

                UpdateHighlighting();
                return;
            }

            // Shift click
            if (modifiers.Equals(false, true, false))
            {
                bool doubleClick = Time.realtimeSinceStartup - previousClickTime < 0.2f;

                // Shift click on an already selected part, remove it
                if (selectedSections.Contains(hoveredSection))
                {
                    if (doubleClick && !hoveredSection.host.simple)
                    {
                        // Select siblings.
                        foreach (RecolourableSection section in hoveredSection.host.sections)
                            Select(section);
                    }
                    else
                    {
                        EnqueueDeselect(hoveredSection);
                        DoDeselect();
                    }
                }
                // Shift click on a new part, add it to the selection
                else
                {
                    if (doubleClick && !hoveredSection.host.simple)
                    {
                        if (hoveredSection.host.sections.Count(s => !selectedSections.Contains(s)) == 1)
                        {
                            // Deselect siblings.
                            foreach (RecolourableSection section in hoveredSection.host.sections)
                                EnqueueDeselect(section);

                            DoDeselect();
                        }
                        else
                        {
                            // Select siblings.
                            foreach (RecolourableSection section in hoveredSection.host.sections)
                                Select(section);
                        }
                    }
                    else
                        Select(hoveredSection);
                }

                UpdateHighlighting();
                return;
            }

            // Clicked an unselected part without holding anything down, clear selection and select the part
            DeselectAll();
            Select(hoveredSection);
            UpdateHighlighting();
        }

        private void UpdateHighlighting()
        {
            foreach (RecolourableSection section in allSections)
            {
                bool enable = selectedSections.Contains(section);
                section.Highlight(enable);

                if (mouseOverVessel)
                    section.Glow(enable);
            }
        }

        public void EnableRecolouring(bool enable, bool apply = true)
        {
            //https://github.com/shadowmage45/TexturesUnlimited/blob/ff1a460262d3aae884fb54fb32e7864b4255531b/Plugin/SSTUTools/KSPShaderTools/GUI/CraftRecolorGUI.cs#L573
            //https://github.com/shadowmage45/TexturesUnlimited/blob/ff1a460262d3aae884fb54fb32e7864b4255531b/Plugin/SSTUTools/KSPShaderTools/Module/KSPTextureSwitch.cs#L124
            //https://github.com/shadowmage45/TexturesUnlimited/blob/ff1a460262d3aae884fb54fb32e7864b4255531b/Plugin/SSTUTools/KSPShaderTools/Util/Utils.cs#L802

            // if !enable, set all selected back to stock and return.

            if (!enable)
            {
                foreach (RecolourableSection section in selectedSections)
                    section.Revert();

                return;
            }

            // set all selected to recolourable.

            foreach (RecolourableSection section in selectedSections)
            {
                if (apply || !section.RecolouringEnabled)
                    section.Enable();
            }

            if (enable && apply)
                ApplyRecolouring();
        }

        public void ApplyRecolouring()
        {
            RecoloringData[] apply = new RecoloringData[colourData.Length];
            for (int i = 0; i < colourData.Length; i++)
            {
                int slot = selectionState[i] ? i : ((i == 2 && selectionState[1]) ? 1 : 0);
                apply[i] = (RecoloringData)colourData[slot];
            }

            foreach (RecolourableSection section in selectedSections)
                section?.module.setSectionColors(string.Empty, apply);
        }

        public void Eyedropper(RecolourableSection section)
        {

            IRecolorable switcher = section.module;
            if (switcher == null)
                return;

            RecoloringData[] colours = switcher.getSectionColors(string.Empty);

            for (int i = 0; i < colourData.Length; i++)
                colourData[i] = colours[i];

            selectionState = new bool[] {
                true,
                !colours[0].IsEqual(colours[1]),
                !colours[1].IsEqual(colours[1]) && !colours[0].IsEqual(colours[2])
            };

            StartCoroutine(FlashSection(section));
            imgui.Refresh();
            ApplyRecolouring();
        }

        public IEnumerator FlashSection(RecolourableSection section)
        {
            section.Highlighter.FlashingOn();

            yield return new WaitForSecondsRealtime(0.5f);

            section.Highlighter.FlashingOff();
        }

        public RecoloringDataPreset ExportColourPreset() =>
            (RecoloringDataPreset)colourData[editingColour];

        public void PrintDebug()
        {
            // Group all sections by code and print counts for each code.
            allSections.GroupBy(s => s.code).OrderBy(g => g.Count()).ToList().ForEach(g => Debug.Log($"{g.Key}: {g.Count()}"));
        }

        #endregion
    }
}