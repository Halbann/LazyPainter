using Highlighting;
using KSPShaderTools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LazyPainter
{
    public abstract class RecolourableSection
    {
        public struct TextureSetInfo
        {
            public TextureSet[] sets;
            public TextureSet[] recolourableSets;
        }

        public struct CreationInfo
        {
            public Part part;
            public RecolourablePart host;
            public IRecolorable module;
            public TextureSetInfo setInfo;
            public Transform[] transforms;
        }

        public string name;
        public string code;
        public IRecolorable module;
        public Transform[] transforms;
        public MeshCollider[] colliders;
        public RecolourablePart host;
        public TextureSetInfo textureInfo;
        public Highlighter _highlighter;
        public bool glowing = false;
        public Highlighter Highlighter => host.simple ? host.part.highlighter : _highlighter;
        public static float rimFallOff = 1.5f;

        private Renderer[] renderersCache;

        public bool RecolouringEnabled => module.getSectionTexture(string.Empty)?.supportsRecoloring ?? false;

        public virtual void Enable() { }
        public virtual void Revert() { }

        public virtual void Selection(Action<RecolourableSection> action)
        {
            action(this);
        }

        public void Glow(bool enable)
        {
            if (glowing == enable)
                return;

            MaterialPropertyBlock mpb = host.part.mpb;
            glowing = enable;
            mpb.SetFloat(PropertyIDs._RimFalloff, rimFallOff);
            mpb.SetColor(PropertyIDs._RimColor, enable ? Color.white : Color.clear);

            IList<Renderer> highlightRenderers;
            if (host.simple)
            {
                highlightRenderers = host.part.highlightRenderer;
            }
            else
            {
                if (renderersCache == null || renderersCache.Any(r => r == null))
                    renderersCache = transforms.Select(t => t?.GetComponent<Renderer>()).Where(r => r != null).ToArray();

                highlightRenderers = renderersCache;
            }

            foreach (Renderer renderer in highlightRenderers)
                renderer?.SetPropertyBlock(mpb);
        }

        public void Highlight(bool enable)
        {
            Highlighter highlighter = host.simple ? host.part.highlighter : _highlighter;
            if (highlighter.constantly == enable)
                return;

            if (enable)
            {
                if (Time.timeScale != 0)
                    highlighter.ConstantOn(Color.white);
                else
                    highlighter.ConstantOnImmediate(Color.white);
            }
            else
                highlighter.Off();
        }

        public static bool Create(RecolourablePart host, CreationInfo info, out RecolourableSection section)
        {
            section = default;

            if (!host.simple)
            {
                if (!FindRecolourableTransforms(info.part, info.module, info.setInfo.recolourableSets, out Transform[] transforms))
                    return false;

                info.transforms = transforms;
            }

            switch (info.module)
            {
                case KSPTextureSwitch switcher:
                    section = new RecolourableSwitcher()
                    {
                        module = switcher
                    };
                    break;
                case TUPartVariant _:
                    section = new RecolourableVariant();
                    break;
                default:
                    return false;
            }

            section.host = host;

            try { section.Init(info); }
            catch { return false; }

            return true;
        }

        private void Init(CreationInfo info)
        {
            module = info.module;
            name = module.getSectionNames()[0];
            textureInfo = info.setInfo;
            code = $"{info.part.name}.{name}";

            if (host.simple)
                return;

            transforms = info.transforms;
            colliders = transforms.Select(AddMeshCollider).Where(c => c != null).ToArray();
            _highlighter = AddHighlighter(transforms);
        }

        public void Cleanup()
        {
            if (!host.simple)
            {
                if (_highlighter != null)
                    UnityEngine.Object.Destroy(_highlighter);

                foreach (MeshCollider collider in colliders)
                    UnityEngine.Object.Destroy(collider.gameObject);
            }

            //host.part.mpb.SetColor(PropertyIDs._RimColor, Part.defaultHighlightNone);
            host.part.mpb.SetFloat(PropertyIDs._RimFalloff, 2f);
        }

        public static bool FindRecolourableTexturesets(Part part, IRecolorable recolourable, out TextureSetInfo info)
        {
            info = new TextureSetInfo();

            // Find all texture sets for this section.
            TextureSet[] sets = null;

            switch (recolourable)
            {
                case KSPTextureSwitch switcher:
                    sets = switcher.textureSets.textureSets;
                    break;
                case TUPartVariant tuVariant:
                    // todo: per part type caching.
                    sets = part.variants.variantList.Select(partVariant => CheckSet(tuVariant, partVariant)).ToArray();
                    break;
            }

            info.sets = sets;
            info.recolourableSets = sets.Where(set => set != null && set.supportsRecoloring).ToArray();

            return info.recolourableSets.Length > 0;
        }

        public static bool FindRecolourableTransforms(Part part, IRecolorable recolourable, TextureSet[] sets, out Transform[] transforms)
        {
            string sectionName = recolourable.getSectionNames()[0];
            transforms = null;

            // KSPTextureSwitch models support custom section roots (not 'model'), so we need to use that.
            // I'm not sure what to do if there's multiple. Presumably only one is correct.

            Transform root;
            if (recolourable is KSPTextureSwitch)
            {
                Transform[] roots = (recolourable as KSPTextureSwitch).getModelTransforms();
                if (roots.Length > 1)
                    throw new NotImplementedException("[Lazy Painter]: Multiple roots not supported.");

                root = roots.FirstOrDefault();
            }
            else
                root = part.transform.FindRecursive("model");

            // Go through the materials for each set and find the transforms based on the mesh names in the material.
            // I'm pretty sure this is the only way to find out which sections act on which meshes/transforms.

            HashSet<Transform> allTextureSetTransforms = new HashSet<Transform>();

            foreach (TextureSet set in sets)
            {
                foreach (TextureSetMaterialData material in set.textureData)
                {
                    Transform[] materialTransforms = TextureSet.findApplicableTransforms(root, material.meshNames, material.excludedMeshes);
                    allTextureSetTransforms.UnionWith(materialTransforms);
                }
            }

            transforms = allTextureSetTransforms.ToArray();
            if (transforms.Length < 1)
            {
                Debug.Log($"[Lazy Painter]: No applicable transforms found for {sectionName} on {part.partInfo.title}. Failing silenty.");
                //throw new Exception();
                return false;
            }

            return true;
        }

        public static TextureSet CheckSet(TUPartVariant tu, PartVariant variant)
        {
            string keyName = "textureSet";

            // If module index greater than zero, always append index number to the key lookup
            if (tu.moduleIndex > 0)
                keyName += tu.moduleIndex.ToString();

            string setName = variant.GetExtraInfoValue(keyName);

            // If module index == 0, and initial name lookup failed, attempt by appending key-name with '0'
            if (tu.moduleIndex == 0 && string.IsNullOrEmpty(setName))
                setName = variant.GetExtraInfoValue(keyName + "0");

            if (!string.IsNullOrEmpty(setName))
                return TexturesUnlimitedLoader.getTextureSet(setName);

            setName = variant.GetExtraInfoValue("modelShader");
            if (!string.IsNullOrEmpty(setName))
                return TexturesUnlimitedLoader.getModelShaderTextureSet(setName);

            // If nothing found, clear out references.
            if (TexturesUnlimitedLoader.logErrors || TexturesUnlimitedLoader.logAll)
                Debug.LogError("Could not load texture set for part variant: " + variant?.Name + " for part: " + tu.part.partInfo.title);

            return null;
        }

        public static MeshCollider AddMeshCollider(Transform parent)
        {
            // Try either MeshFilter or SkinnedMeshRenderer for getting the sharedMesh.

            Mesh mesh = null;
            SkinnedMeshRenderer skinned = parent.GetComponent<SkinnedMeshRenderer>();
            if (skinned == null)
            {
                mesh = parent.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                    return null;
            }

            GameObject go = new GameObject("LazyPainterMeshCollider");
            go.transform.SetParent(parent, false);

            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.convex = false;

            if (skinned != null)
            {
                Mesh bakedMesh = new Mesh();
                collider.sharedMesh = bakedMesh;
                skinned.BakeMesh(bakedMesh);
                Vector3 s = parent.transform.lossyScale;
                go.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            }
            else
            {
                collider.sharedMesh = mesh;
            }

            return collider;
        }

        public static Highlighter AddHighlighter(Transform[] transforms)
        {
            Highlighter highlighter = transforms[0].gameObject.AddComponent<Highlighter>();

            // Find renderers.
            List<Renderer> renderers = new List<Renderer>();
            foreach (Transform transform in transforms)
            {
                foreach (Type type in Highlighter.types)
                {
                    System.Collections.IEnumerator enumerator = transform.gameObject.GetComponents(type).GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        Renderer renderer = enumerator.Current as Renderer;
                        if (renderer.gameObject.layer != 1 && !renderer.material.name.Contains("KSP/Alpha/Translucent Additive"))
                        {
                            renderers.Add(renderer);
                        }
                    }
                }
            }

            // Cache renderers.
            highlighter.highlightableRenderers = new List<Highlighter.RendererCache>();
            int count = renderers.Count;
            for (int i = 0; i < count; i++)
            {
                Highlighter.RendererCache item = new Highlighter.RendererCache(renderers[i], highlighter.opaqueMaterial, highlighter.zTestFloat, highlighter.stencilRefFloat);
                highlighter.highlightableRenderers.Add(item);
            }

            highlighter.highlighted = false;
            highlighter.renderersDirty = false;
            highlighter.currentColor = Color.clear;

            return highlighter;
        }
    }
}
