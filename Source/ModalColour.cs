using KSPShaderTools;
using UnityEngine;

namespace LazyPainter
{
    public struct ModalColour
    {
        // This could be made extensible to support n colour modes, but it's not worth the time.

        public Color Colour
        {
            get => _colour;
            set
            {
                if (_colour == value)
                    return;

                _colour = value;
                hsvDirty = true;
                hexDirty = true;
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
                hexDirty = true;
                _hsv = value;
                _colour = value.Colour;
            }
        }

        private bool hexDirty;
        public bool hexValid;

        public string Hex
        {
            get
            {
                if (!hexDirty)
                    return _hex;

                hexDirty = false;
                hexValid = true;
                return _hex = "#" + ColorUtility.ToHtmlStringRGB(_colour);
            }
            set
            {
                if (_hex == value)
                    return;

                _hex = value;
                hexDirty = false;
                if (hexValid = TryParseHex(value, out Color colour, out bool correction, out string correctedHex))
                {
                    if (correction)
                        _hex = correctedHex;

                    _colour = colour;
                    hsvDirty = true;
                }
            }
        }

        private bool TryParseHex(string hex, out Color colour, out bool correction, out string correctedHex)
        {
            colour = default;
            correctedHex = default;
            correction = false;

            if (string.IsNullOrEmpty(hex))
                return false;

            if (ColorUtility.TryParseHtmlString(hex, out colour))
                return true;

            if (hex.Length < 6)
                return false;

            correctedHex = "#" + hex;
            if (ColorUtility.TryParseHtmlString(correctedHex, out colour))
            {
                correction = true;
                return true;
            }

            return false;
        }

        private string _hex;
        private Color _colour;
        private HSV _hsv;

        public float specular;
        public float metallic;
        public float detail;

        public ModalColour(Color colour, float specular, float metallic, float detail)
        {
            _colour = colour;
            _hsv = default;
            _hex = default;
            hexDirty = true;
            hsvDirty = true;
            hexValid = false;
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