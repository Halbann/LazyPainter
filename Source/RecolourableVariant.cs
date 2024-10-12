using KSPShaderTools;
using System;
using System.Linq;

namespace LazyPainter
{
    public class RecolourableVariant : RecolourableSection
    {
        public override void Selection(Action<RecolourableSection> action)
        {
            if (!RecolouringEnabled)
            {
                foreach (RecolourableSection sibling in host.sections)
                    action(sibling);
            }
            else
            {
                action(this);
            }
        }

        private void ApplyEquivelantVariant(bool supportsRecolouring)
        {
            // Look for a variant that fulfills the recolouring requirement (yes or no)
            // and has the same meshes as the current variant.
            // todo: caching.

            PartVariant currentVariant = host.part.variants.SelectedVariant;
            ModulePartVariants variants = host.part.variants;

            for (int i = 0; i < textureInfo.sets.Length; i++)
            {
                bool defaultCanBeAny = !supportsRecolouring && i == 0;

                if ((defaultCanBeAny || (textureInfo.sets[i]?.supportsRecoloring ?? false) == supportsRecolouring)
                    && SameMesh(variants.variantList[i], currentVariant))
                {
                    variants.SetVariant(variants.variantList[i].name);
                    UpdatePAW(i);
                    return;
                }
            }
        }

        private void UpdatePAW(int index)
        {
            UIPartActionWindow paw = host.part.PartActionWindow;
            if (paw == null)
                return;

            UIPartActionVariantSelector selector = paw.ListItems.FirstOrDefault(i => i is UIPartActionVariantSelector) as UIPartActionVariantSelector;
            selector?.OnButtonPressed(index);

            SSTURecolorGUI gui;
            if ((gui = host.SSTURecolorGUI) != null)
            {
                BaseEvent evt = gui.Events[nameof(SSTURecolorGUI.recolorGUIEvent)];
                bool active = textureInfo.sets[index]?.supportsRecoloring ?? false;

                evt.guiActive = active;
                evt.guiActiveEditor = active;
            }
        }

        private bool SameMesh(PartVariant a, PartVariant b)
        {
            int count = 0;
            foreach (PartGameObjectInfo bObject in b.infoGameObjects)
            {
                if (bObject.status && !a.infoGameObjects.Any(aObject => aObject.status && aObject.name == bObject.name))
                    return false;

                if (bObject.status)
                    count++;
            }

            return count == a.infoGameObjects.Count(g => g.status);
        }

        public override void Enable()
        {
            if (RecolouringEnabled)
                return;

            ApplyEquivelantVariant(true);
        }

        public override void Revert()
        {
            if (!RecolouringEnabled)
                return;

            ApplyEquivelantVariant(false);
        }
    }
}
