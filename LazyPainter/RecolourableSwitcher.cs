using KSPShaderTools;
using System;
using System.Linq;

namespace LazyPainter
{
    public class RecolourableSwitcher : RecolourableSection
    {
        public new KSPTextureSwitch module;

        public override void Enable()
        {
            int current = Array.FindIndex(textureInfo.sets, set => set.name == module.currentTextureSet);
            int start = (current + 1) % textureInfo.sets.Length;
            int count = 0;
            for (int i = start; count <= textureInfo.sets.Length; i = (i + 1) % textureInfo.sets.Length)
            {
                count++;

                if (textureInfo.sets[i]?.supportsRecoloring ?? false)
                {
                    module.enableTextureSet(textureInfo.sets[i].name, false, false);
                    return;
                }
            }
        }

        public override void Revert()
        {
            if (!RecolouringEnabled)
                return;

            module.enableTextureSet(textureInfo.sets.FirstOrDefault().name, false, false);
        }
    }
}
