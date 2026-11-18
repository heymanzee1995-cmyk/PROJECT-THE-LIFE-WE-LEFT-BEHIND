using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Flat placeholder shapes generated at runtime (no art assets yet). Final art replaces these in M8.
    /// Sprites are 1 world unit wide; views scale them to the cell size in pixels.
    /// </summary>
    internal static class PlaceholderArt
    {
        private const int Size = 64;
        private static Sprite _rounded;
        private static Sprite _circle;
        private static Font _font;

        public static Sprite Rounded
        {
            get
            {
                // Unity's == null also catches textures destroyed when play mode ends.
                if (_rounded == null)
                {
                    _rounded = Make(0.22f);
                }

                return _rounded;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    _circle = Make(0.5f);
                }

                return _circle;
            }
        }

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return _font;
            }
        }

        public static Color ParseColor(string hex, Color fallback) =>
            hex != null && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;

        /// <summary>Rounded square with anti-aliased edges; <paramref name="radius"/> is a fraction of the size.</summary>
        private static Sprite Make(float radius)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "PlaceholderShape",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            float r = radius * Size;
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, r, Size - r), cy = Mathf.Clamp(py, r, Size - r);
                    float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float alpha = Mathf.Clamp01(r - distance + 0.5f);
                    if (r <= 0f)
                    {
                        alpha = 1f;
                    }

                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
