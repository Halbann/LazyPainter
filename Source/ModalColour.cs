using KSPShaderTools;
using UnityEngine;

namespace LazyPainter
{
    public struct ModalColour
    {
        public Color Colour
        {
            get => _colour;
            set
            {
                if (_colour == value)
                    return;

                _colour = value;
                hsvDirty = true;
            }
        }

        private bool hsvDirty;

        public HSV HSV
        {
            get
            {
                if (!hsvDirty)
                    return _hsv;

                hsvDirty = false;
                return _hsv = new HSV(_colour);
            }
            set
            {
                if (_hsv == value)
                    return;

                hsvDirty = false;
                _hsv = value;
                _colour = value.Colour;
            }
        }

        private Color _colour;
        private HSV _hsv;

        public float specular;
        public float metallic;
        public float detail;

        public ModalColour(Color colour, float specular, float metallic, float detail)
        {
            _colour = colour;
            _hsv = default;
            hsvDirty = true;
            this.specular = specular;
            this.metallic = metallic;
            this.detail = detail;
        }

        public static implicit operator ModalColour(RecoloringData v)
        {
            return new ModalColour(v.color, v.specular, v.metallic, v.detail);
        }

        public static explicit operator RecoloringData(ModalColour v)
        {
            return new RecoloringData(v.Colour, v.specular, v.metallic, v.detail);
        }

        public static explicit operator RecoloringDataPreset(ModalColour v)
        {
            return new RecoloringDataPreset()
            {
                color = v.Colour,
                specular = v.specular,
                metallic = v.metallic,
            };
        }
    }

    public struct HSV
    {
        public float hue;
        public float saturation;
        public float value;

        public HSV(Color colour) =>
            Color.RGBToHSV(colour, out hue, out saturation, out value);

        public Color Colour =>
            Color.HSVToRGB(hue, saturation, value);

        public static bool operator ==(HSV a, HSV b) =>
            a.hue == b.hue && a.saturation == b.saturation && a.value == b.value;

        public static bool operator !=(HSV a, HSV b) =>
            !(a == b);
    }
}