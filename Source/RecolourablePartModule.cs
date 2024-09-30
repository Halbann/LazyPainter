using KSPShaderTools;

namespace LazyPainter
{
    public class RecolourablePartModule
    {
        public Part part { get { return pv ? partVariant.part : switcher.part; } }
        public TUPartVariant partVariant;
        public KSPTextureSwitch switcher;
        public bool pv = false;

        public TextureSetContainer _textureSets;
        public TextureSetContainer TextureSets
        {
            get
            {
                if (_textureSets == null)
                    _textureSets = (TextureSetContainer)LazyPainter.textureSetsField.GetValue(switcher);

                return _textureSets;
            }
        }

        public ModulePartVariants _modulePartVariants;
        public ModulePartVariants VariantsModule
        {
            get
            {
                if (_modulePartVariants == null)
                    _modulePartVariants = part.FindModuleImplementing<ModulePartVariants>();

                return _modulePartVariants;
            }
        }

        public string CurrentTexture
        {
            get
            {
                return pv ? partVariant.textureSet : switcher.currentTextureSet;
            }
        }

        public RecolourablePartModule(KSPTextureSwitch switcher)
        {
            this.switcher = switcher;
        }

        public RecolourablePartModule(TUPartVariant partVariant)
        {
            this.partVariant = partVariant;
            pv = true;
        }

        public RecoloringData[] GetSectionColors()
        {
            if (pv)
                return partVariant.getSectionColors(partVariant.textureSet);
            else
                return switcher.getSectionColors(switcher.currentTextureSet);
        }

        public void SetSectionColors(RecoloringData[] colors)
        {
            if (pv)
                partVariant.setSectionColors(partVariant.textureSet, colors);
            else
                switcher.setSectionColors(switcher.currentTextureSet, colors);
        }

        public void SetTexture(string textureName)
        {
            if (pv)
                VariantsModule.SetVariant(textureName);
            else
                switcher.enableTextureSet(textureName, false, false);
        }

        public string[] GetTextures()
        {
            if (pv)
                return VariantsModule.GetVariantNames().ToArray();
            else
                return TextureSets.getTextureSetNames();
        }

        public void Disable()
        {
            if (pv)
                VariantsModule.SetVariant(VariantsModule.GetVariantNames()[0]);
            else
                switcher.enableTextureSet(TextureSets.getTextureSetNames()[0], false, false);
        }
    }
}
