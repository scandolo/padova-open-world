using Padova.Architecture;
using UnityEngine;

namespace Padova.City
{
    /// <summary>Texture-array slices of the Padova/City material.</summary>
    public enum Layer : byte
    {
        Plain, Plaster, Brick, Stone, Roof, Trachyte, Sidewalk, CourtBrick, Asphalt, Grass, Water, Earth, Gravel, Foliage, Metal,
        PorticoFloor, Lead, Count
    }

    /// <summary>A surface of the distant city: slice, sRGB tint and metres per texture repeat.</summary>
    public struct Surface
    {
        public Layer Layer;
        public Color32 Tint;
        public float Metres;
        public Surface(Layer layer, float r, float g, float b, float metres = 3)
        {
            Layer = layer; Metres = metres;
            Tint = new Color32((byte)(Mathf.Clamp01(r) * 255), (byte)(Mathf.Clamp01(g) * 255), (byte)(Mathf.Clamp01(b) * 255), (byte)layer);
        }
        public Surface Shade(float k) => new Surface(Layer, Tint.r / 255f * k, Tint.g / 255f * k, Tint.b / 255f * k, Metres);
    }

    /// <summary>Per-class façade vocabulary and the distant-city colours that match the detailed materials.
    /// Everything here is typology chosen deterministically per building; nothing is surveyed per façade.</summary>
    public static class CityStyle
    {
        static readonly Slot[] Modern = { Slot.Plaster1, Slot.Plaster6, Slot.Plaster9, Slot.Plaster4, Slot.Plaster9, Slot.Plaster6, Slot.Plaster3, Slot.Plaster1 };
        static readonly Slot[] Pastel = { Slot.Plaster1, Slot.Plaster4, Slot.Plaster3, Slot.Plaster9, Slot.Plaster0, Slot.Plaster6, Slot.Plaster7, Slot.Plaster5 };
        static readonly Slot[] Plain = { Slot.White, Slot.Plaster6, Slot.Plaster9, Slot.Plaster6 };
        static readonly Slot[] Civic = { Slot.Plaster1, Slot.Plaster6, Slot.Plaster0, Slot.Plaster9 };

        public static UnitStyle For(uint seed, CityClass cls)
        {
            var s = UnitStyle.Seeded((int)(seed & 0x7fffffff));
            var r = s.Rng;
            float F(float a, float b) => a + (float)r.NextDouble() * (b - a);
            bool Roll(float p) => r.NextDouble() < p;
            switch (cls)
            {
                case CityClass.Townhouse:
                    if (Roll(.05f)) { s.Wall = Slot.Brick; s.Brick = true; }
                    break;
                case CityClass.Palazzo:
                    s.FloorH = F(3.6f, 4.3f); s.GfH = F(4.6f, 5.3f); s.Bay = F(3.3f, 4.0f); s.WinH = F(2.1f, 2.5f);
                    s.Surround = true; s.StringCourse = true; s.Rusticated = Roll(.45f); s.Balcony = Roll(.6f); s.Plinth = true;
                    break;
                case CityClass.Condo:
                    s.Wall = Modern[r.Next(Modern.Length)];
                    s.Bay = F(3.5f, 4.4f); s.WinW = F(1.25f, 1.6f); s.WinH = F(1.4f, 1.55f); s.FloorH = F(2.95f, 3.15f); s.GfH = F(3.0f, 3.4f);
                    s.UpperArch = Arch.None; s.GroundArch = Arch.None; s.Surround = false; s.StringCourse = Roll(.25f);
                    s.Balcony = Roll(.7f); s.Shutters = Roll(.35f); s.Rusticated = false; s.RoundColumns = false; s.ArcadeArch = Arch.None;
                    break;
                case CityClass.House:
                case CityClass.Rural:
                    s.Wall = Pastel[r.Next(Pastel.Length)];
                    s.Bay = F(3.3f, 4.1f); s.WinW = F(1.0f, 1.2f); s.WinH = F(1.35f, 1.5f); s.FloorH = F(2.9f, 3.1f); s.GfH = F(2.9f, 3.2f);
                    s.UpperArch = Arch.None; s.GroundArch = Arch.None; s.Surround = Roll(.3f); s.StringCourse = Roll(.2f);
                    s.Balcony = Roll(.25f); s.Shutters = Roll(.85f); s.Rusticated = false; s.RoundColumns = false; s.ArcadeArch = Arch.None;
                    break;
                case CityClass.Shed:
                case CityClass.Commercial:
                case CityClass.Sport:
                case CityClass.Station:
                    s.Wall = Plain[r.Next(Plain.Length)];
                    s.Bay = F(5.5f, 7.5f); s.WinW = F(2.4f, 3.4f); s.WinH = 1.15f; s.FloorH = 6.5f; s.GfH = F(4.2f, 4.8f);
                    s.UpperArch = Arch.None; s.GroundArch = Arch.None; s.Surround = false; s.StringCourse = false; s.Balcony = false;
                    s.Shutters = false; s.Rusticated = false; s.Plinth = false; s.RoundColumns = false; s.ArcadeArch = Arch.None;
                    s.Blind = cls == CityClass.Shed && Roll(.45f);
                    break;
                case CityClass.Office:
                    s.Wall = Plain[r.Next(Plain.Length)];
                    s.Bay = F(2.6f, 3.2f); s.WinW = s.Bay * .72f; s.WinH = 1.9f; s.FloorH = F(3.3f, 3.6f); s.GfH = 4.2f;
                    s.UpperArch = Arch.None; s.GroundArch = Arch.None; s.Surround = false; s.StringCourse = false; s.Balcony = false;
                    s.Shutters = false; s.Rusticated = false; s.RoundColumns = false; s.ArcadeArch = Arch.None;
                    break;
                case CityClass.Civic:
                    s.Wall = Civic[r.Next(Civic.Length)];
                    s.Bay = F(3.5f, 4.2f); s.WinW = F(1.25f, 1.45f); s.WinH = F(2.0f, 2.4f); s.FloorH = F(3.8f, 4.3f); s.GfH = F(4.4f, 5.0f);
                    s.Surround = true; s.StringCourse = true; s.Shutters = false; s.Balcony = false; s.Plinth = true;
                    break;
                case CityClass.Church:
                    s.Church = true; s.Brick = true; s.Wall = Slot.Brick; s.Shutters = false; s.Balcony = false;
                    s.Bay = 6.5f; s.WinW = 1.5f; s.UpperArch = Arch.Round; s.StringCourse = false; s.Surround = true;
                    break;
                case CityClass.Tower:
                    s.Tower = true; s.Brick = true; s.Wall = Slot.Brick; s.Shutters = false; s.Balcony = false;
                    s.Bay = 30; s.WinW = 0.9f; s.WinH = 1.6f; s.FloorH = 6f; s.UpperArch = Arch.Round; s.StringCourse = false;
                    break;
                case CityClass.Minor:
                    s.Wall = Plain[r.Next(Plain.Length)];
                    s.Blind = true; s.Plinth = false; s.StringCourse = false; s.Surround = false; s.Balcony = false; s.Shutters = false;
                    break;
            }
            return s;
        }

        /// <summary>Distant-city surface that matches a detailed material slot.</summary>
        public static Surface Wall(Slot slot)
        {
            switch (slot)
            {
                case Slot.Plaster0: return new Surface(Layer.Plaster, .93f, .76f, .5f, 3.2f);
                case Slot.Plaster1: return new Surface(Layer.Plaster, .96f, .88f, .72f, 3.2f);
                case Slot.Plaster2: return new Surface(Layer.Plaster, .76f, .42f, .31f, 3.2f);
                case Slot.Plaster3: return new Surface(Layer.Plaster, .92f, .66f, .56f, 3.2f);
                case Slot.Plaster4: return new Surface(Layer.Plaster, .97f, .85f, .58f, 3.2f);
                case Slot.Plaster5: return new Surface(Layer.Plaster, .88f, .58f, .38f, 3.2f);
                case Slot.Plaster6: return new Surface(Layer.Plaster, .87f, .83f, .76f, 3.2f);
                case Slot.Plaster7: return new Surface(Layer.Plaster, .84f, .6f, .55f, 3.2f);
                case Slot.Plaster8: return new Surface(Layer.Plaster, .8f, .52f, .36f, 3.2f);
                case Slot.Plaster9: return new Surface(Layer.Plaster, .95f, .92f, .86f, 3.2f);
                case Slot.Brick: return new Surface(Layer.Brick, .95f, .9f, .86f, 2.2f);
                case Slot.Stone: return new Surface(Layer.Stone, .97f, .95f, .9f, 1.6f);
                case Slot.GreyStone: return new Surface(Layer.Stone, .58f, .58f, .6f, 1.6f);
                case Slot.White: return new Surface(Layer.Plaster, .92f, .89f, .8f, 3.2f);
                default: return new Surface(Layer.Plaster, .9f, .84f, .74f, 3.2f);
            }
        }

        public static readonly Surface RoofTiles = new Surface(Layer.Roof, 1f, .93f, .88f, 2.4f);
        public static readonly Surface RoofMetal = new Surface(Layer.Metal, .66f, .69f, .72f, 1.2f);
        public static readonly Surface RoofGravel = new Surface(Layer.Gravel, .6f, .59f, .57f, 3f);
        public static readonly Surface RoofLead = new Surface(Layer.Lead, .86f, .88f, .9f, 2.4f);
        public static readonly Surface Wood = new Surface(Layer.Plain, .3f, .2f, .13f);
        public static readonly Surface Ceiling = new Surface(Layer.Plaster, .93f, .9f, .84f, 3.2f);
        public static readonly Surface StoneTrim = new Surface(Layer.Stone, .97f, .95f, .9f, 1.6f);
        public static readonly Surface Curb = new Surface(Layer.Stone, .8f, .79f, .75f, 1.6f);
        public static readonly Surface Bank = new Surface(Layer.Brick, .62f, .58f, .54f, 2.2f);
        public static readonly Surface Bark = new Surface(Layer.Plain, .28f, .2f, .12f);
        public static readonly Surface GardenWall = new Surface(Layer.Plaster, .86f, .8f, .68f, 3.2f);
        public static readonly Surface BrickWall = new Surface(Layer.Brick, .9f, .84f, .8f, 2.2f);
        public static readonly Surface HedgeGreen = new Surface(Layer.Foliage, .2f, .34f, .15f, 2f);

        /// <summary>Ground surfaces by survey kind (order of GroundKind).</summary>
        public static readonly Surface[] Ground =
        {
            new Surface(Layer.Earth, .62f, .6f, .5f, 6f),        // base: land the survey does not classify
            new Surface(Layer.Asphalt, .3f, .31f, .33f, 5f),     // carriageway
            new Surface(Layer.Sidewalk, 1f, 1f, 1f, 3f),
            new Surface(Layer.Trachyte, .95f, .95f, .95f, 4f),   // pedestrian area
            new Surface(Layer.Asphalt, .62f, .3f, .24f, 5f),     // cycle path
            new Surface(Layer.Asphalt, .36f, .36f, .37f, 5f),    // tram
            new Surface(Layer.Gravel, .48f, .43f, .4f, 3f),      // railway
            new Surface(Layer.Asphalt, .37f, .38f, .4f, 5f),     // parking
            new Surface(Layer.Asphalt, .44f, .44f, .44f, 5f),    // other street area
            new Surface(Layer.Grass, .5f, .63f, .36f, 5f),       // garden
            new Surface(Layer.Grass, .47f, .68f, .33f, 5f),      // lawn
            new Surface(Layer.Grass, .4f, .55f, .3f, 5f),        // tree area
            new Surface(Layer.Grass, .5f, .6f, .34f, 5f),        // flowerbed
            new Surface(Layer.Foliage, .22f, .36f, .16f, 2f),    // hedge
            new Surface(Layer.Gravel, .7f, .66f, .58f, 3f),      // courtyard
            new Surface(Layer.Grass, .33f, .46f, .26f, 5f),      // wood
            new Surface(Layer.Earth, .66f, .68f, .42f, 6f),      // field
            new Surface(Layer.Grass, .56f, .64f, .38f, 5f),      // meadow
            new Surface(Layer.Water, .2f, .34f, .28f, 9f),       // water
            new Surface(Layer.Grass, .36f, .66f, .3f, 5f),       // pitch
            new Surface(Layer.Earth, .78f, .42f, .28f, 6f),      // clay court
            new Surface(Layer.Water, .25f, .62f, .78f, 5f),      // pool
            new Surface(Layer.PorticoFloor, 1f, 1f, 1f, 1.6f),
            new Surface(Layer.Sidewalk, .92f, .92f, .92f, 3f),   // bridge deck
            new Surface(Layer.Sidewalk, .86f, .86f, .84f, 3f),   // traffic island
        };

        /// <summary>Builds the texture array for the Padova/City material from the detailed materials plus generated grain.</summary>
        public static Texture2DArray BuildLayers(Material[] slots, int size = 512)
        {
            var array = new Texture2DArray(size, size, (int)Layer.Count, TextureFormat.RGBA32, true, false)
            { name = "Padova city layers", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var scratch = new Texture2D(size, size, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.DontSave };
            void Copy(Layer layer, Slot slot)
            {
                var source = slots != null && (int)slot < slots.Length && slots[(int)slot] ? slots[(int)slot].mainTexture : null;
                if (!source) { Grain(array, layer, size, .9f, .1f, 8, 3); return; }
                var previous = RenderTexture.active;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                scratch.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                RenderTexture.active = previous;
                array.SetPixels32(scratch.GetPixels32(), (int)layer);
            }
            var white = new Color32[size * size];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
            array.SetPixels32(white, (int)Layer.Plain);
            Copy(Layer.Plaster, Slot.Plaster0); Copy(Layer.Brick, Slot.Brick); Copy(Layer.Stone, Slot.Stone); Copy(Layer.Roof, Slot.Roof);
            Copy(Layer.Trachyte, Slot.Trachyte); Copy(Layer.Sidewalk, Slot.Sidewalk); Copy(Layer.CourtBrick, Slot.Courtyard);
            Copy(Layer.PorticoFloor, Slot.PorticoFloor); Copy(Layer.Lead, Slot.Lead);
            Grain(array, Layer.Asphalt, size, .86f, .14f, 96, 4);
            Grain(array, Layer.Grass, size, .8f, .2f, 24, 4);
            Grain(array, Layer.Water, size, .5f, .5f, 6, 3);
            Grain(array, Layer.Earth, size, .84f, .16f, 12, 4);
            Grain(array, Layer.Gravel, size, .82f, .18f, 64, 3);
            Grain(array, Layer.Foliage, size, .7f, .3f, 20, 3);
            Corrugated(array, Layer.Metal, size);
            array.Apply(true, true);
            RenderTexture.ReleaseTemporary(rt);
            if (Application.isPlaying) Object.Destroy(scratch); else Object.DestroyImmediate(scratch);
            return array;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        /// <summary>Tileable value noise as a grey multiplier around <paramref name="level"/>.</summary>
        static void Grain(Texture2DArray array, Layer layer, int size, float level, float amount, int cells, int octaves)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float sum = 0, weight = 0, amp = 1;
                    int c = cells;
                    for (int o = 0; o < octaves; o++)
                    {
                        float fx = x * c / (float)size, fy = y * c / (float)size;
                        int ix = (int)fx, iy = (int)fy;
                        float tx = fx - ix, ty = fy - iy;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        float a = Hash(ix % c, iy % c, o + (int)layer * 7), b = Hash((ix + 1) % c, iy % c, o + (int)layer * 7);
                        float d = Hash(ix % c, (iy + 1) % c, o + (int)layer * 7), e = Hash((ix + 1) % c, (iy + 1) % c, o + (int)layer * 7);
                        sum += Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(d, e, tx), ty) * amp;
                        weight += amp; amp *= .5f; c *= 2;
                    }
                    byte v = (byte)(Mathf.Clamp01(level + (sum / weight - .5f) * 2 * amount) * 255);
                    pixels[y * size + x] = new Color32(v, v, v, 255);
                }
            array.SetPixels32(pixels, (int)layer);
        }

        static void Corrugated(Texture2DArray array, Layer layer, int size)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float rib = .5f + .5f * Mathf.Cos(x * Mathf.PI * 2 * 12 / size);
                    byte v = (byte)(Mathf.Clamp01(.78f + .2f * rib - .05f * Hash(x / 4, y / 16, 5)) * 255);
                    pixels[y * size + x] = new Color32(v, v, v, 255);
                }
            array.SetPixels32(pixels, (int)layer);
        }
    }
}
