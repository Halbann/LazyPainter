using KSPShaderTools;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LazyPainter
{
    public class RecolourablePart : MonoBehaviour
    {
        public bool simple;
        public Part part;
        public Dictionary<Transform, RecolourableSection> table;
        public RecolourableSection[] sections;

        public SSTURecolorGUI SSTURecolorGUI
        {
            private set => _SSTURecolorGUI = value;
            get => _SSTURecolorGUI ?? (_SSTURecolorGUI = part.FindModuleImplementing<SSTURecolorGUI>());
        }

        private SSTURecolorGUI _SSTURecolorGUI;
        public Collider[] stockColliders = null;

        public static RecolourablePart Create(Part part)
        {
            List<IRecolorable> recolourables = part.FindModulesImplementing<IRecolorable>();
            if (recolourables.Count < 1)
                return null;

            List<RecolourableSection.CreationInfo> newSections = null;

            foreach (IRecolorable recolour in recolourables)
            {
                if (!RecolourableSection.FindRecolourableTexturesets(part, recolour, out RecolourableSection.TextureSetInfo setInfo))
                    continue;

                if (newSections == null)
                    newSections = new List<RecolourableSection.CreationInfo>();

                newSections.Add(new RecolourableSection.CreationInfo { part = part, module = recolour, setInfo = setInfo });
            }

            if (newSections == null)
                return null;

            RecolourablePart recolourablePart = part.gameObject.AddComponent<RecolourablePart>();
            recolourablePart.Init(part, newSections);

            return recolourablePart;
        }

        private void Init(Part part, List<RecolourableSection.CreationInfo> sections)
        {
            this.part = part;
            this.sections = new RecolourableSection[sections.Count];
            simple = sections.Count == 1;

            if (!simple)
            {
                stockColliders = part.GetPartColliders();
                foreach (Collider collider in stockColliders)
                    collider.enabled = false;

                table = new Dictionary<Transform, RecolourableSection>();
            }

            bool prune = false;
            for (int i = 0; i < sections.Count; i++)
            {
                if (!RecolourableSection.Create(this, sections[i], out RecolourableSection section))
                {
                    prune = true;
                    continue;
                }

                this.sections[i] = section;

                if (!simple && section.transforms != null)
                    foreach (Transform transform in section.transforms)
                        table.Add(transform, section);
            }

            if (prune)
                this.sections = this.sections.Where(s => s != null).ToArray();
        }

        public void OnDestroy()
        {
            foreach (RecolourableSection section in sections)
                section.Cleanup();

            if (!simple)
                foreach (Collider collider in stockColliders)
                    collider.enabled = true;
        }

        public bool GetSection(out RecolourableSection mouseOverSection)
        {
            if (simple)
            {
                mouseOverSection = sections.First();
                return true;
            }

            // This approach is only valid if stock colliders are already disabled.
            return table.TryGetValue(Mouse.MouseButton.hoveredPartHitInfo.collider.transform.parent, out mouseOverSection);
        }
    }
}
