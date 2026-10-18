using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// Builds the block texture array: one 16×16 layer per registry texture name, in registry order. A PNG at
/// <c>res://assets/textures/blocks/&lt;name&gt;.png</c> is used when present, otherwise a deterministic placeholder.
/// </summary>
public static class BlockTextures
{
    public const int Size = 16;
    const string Dir = "res://assets/textures/blocks/";
    const float Noise = 0.08f;

    public static Texture2DArray Build(BlockRegistry registry)
    {
        var images = new Godot.Collections.Array<Image>();
        foreach (string name in registry.TextureNames)
        {
            var img = LoadOrNull(name) ?? Placeholder(name);
            img.GenerateMipmaps();
            images.Add(img);
        }
        var tex = new Texture2DArray();
        var err = tex.CreateFromImages(images);
        if (err != Error.Ok) GD.PushError($"BlockTextures: creating the texture array failed ({err})");
        return tex;
    }

    static Image? LoadOrNull(string name)
    {
        string path = Dir + name + ".png";
        if (!ResourceLoader.Exists(path)) return null;
        var img = GD.Load<Texture2D>(path)?.GetImage();
        if (img == null) { GD.PushWarning($"BlockTextures: could not read {path}; using a placeholder"); return null; }
        if (img.IsCompressed()) img.Decompress();
        img.ClearMipmaps();
        img.Convert(Image.Format.Rgba8);
        if (img.GetWidth() != Size || img.GetHeight() != Size)
        {
            GD.PushWarning($"BlockTextures: {path} is {img.GetWidth()}×{img.GetHeight()}, expected {Size}×{Size}; resizing");
            img.Resize(Size, Size, Image.Interpolation.Nearest);
        }
        return img;
    }

    /// <summary>Base color with ±8% per-pixel noise seeded by the name, plus a per-texture pattern.</summary>
    public static Image Placeholder(string name)
    {
        int seed = StableHash(name);
        var px = new Color[Size * Size];
        var (baseHex, pattern) = Style(name);
        var baseColor = Color.FromHtml(baseHex);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                px[y * Size + x] = baseColor;

        pattern?.Invoke(px, seed);

        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float n = 1f + (Hash.Unit(seed, x, y, 0) * 2f - 1f) * Noise;
                var c = px[y * Size + x];
                img.SetPixel(x, y, new Color(Mathf.Clamp(c.R * n, 0, 1), Mathf.Clamp(c.G * n, 0, 1), Mathf.Clamp(c.B * n, 0, 1)));
            }
        return img;
    }

    static (string Color, Action<Color[], int>? Pattern) Style(string name) => name switch
    {
        "grass_top" => ("#5fa83a", null),
        "grass_side" => ("#8a5f3c", (px, _) => FillRows(px, 0, 3, Color.FromHtml("#5fa83a"))),
        "dirt" => ("#8a5f3c", null),
        "sand" => ("#d8c98c", null),
        "stone" => ("#8c8c8c", null),
        "deep_stone" => ("#5d5f66", null),
        "bedrock" => ("#2b2b2e", null),
        "coal_ore" => ("#8c8c8c", (px, s) => Specks(px, s, Color.FromHtml("#2a2a2e"))),
        "copper_ore" => ("#8c8c8c", (px, s) => Specks(px, s, Color.FromHtml("#c8703a"))),
        "iron_ore" => ("#8c8c8c", (px, s) => Specks(px, s, Color.FromHtml("#c9a48a"))),
        "gold_ore" => ("#8c8c8c", (px, s) => Specks(px, s, Color.FromHtml("#f2d24b"))),
        "water" => ("#3d7fd6", null),
        "oak_log" => ("#6b4a2b", (px, _) => VerticalStripes(px)),
        "oak_log_top" => ("#9c7444", (px, _) => Rings(px, Color.FromHtml("#6b4a2b"))),
        "pine_log" => ("#4a3320", (px, _) => VerticalStripes(px)),
        "pine_log_top" => ("#7a5a36", (px, _) => Rings(px, Color.FromHtml("#4a3320"))),
        "oak_leaves" => ("#3d7d2a", null),
        "pine_leaves" => ("#2d5e2a", null),
        "cobblestone" => ("#7a7a7a", Blotches),
        "planks" => ("#b48a52", (px, _) => Boards(px)),
        "stone_bricks" => ("#9a9a9a", (px, _) => Bricks(px)),
        // Unknown textures still get a distinct, stable color.
        _ => ("#" + ((uint)StableHash(name) & 0xFFFFFF).ToString("x6"), null),
    };

    static void FillRows(Color[] px, int from, int count, Color c)
    {
        for (int y = from; y < from + count; y++)
            for (int x = 0; x < Size; x++) px[y * Size + x] = c;
    }

    static void Specks(Color[] px, int seed, Color c)
    {
        for (int i = 0; i < 6; i++)
        {
            int sx = (int)(Hash.Unit(seed, i, 0, 1) * (Size - 1));
            int sy = (int)(Hash.Unit(seed, i, 0, 2) * (Size - 1));
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++) px[(sy + dy) * Size + sx + dx] = c;
        }
    }

    static void VerticalStripes(Color[] px)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (x % 4 == 1) px[y * Size + x] = px[y * Size + x].Darkened(0.25f);
                else if (x % 4 == 3) px[y * Size + x] = px[y * Size + x].Lightened(0.08f);
    }

    static void Rings(Color[] px, Color bark)
    {
        const float c = (Size - 1) / 2f;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                if (d > 6.6f) px[y * Size + x] = bark;
                else if ((int)d % 2 == 1) px[y * Size + x] = px[y * Size + x].Darkened(0.18f);
            }
    }

    /// <summary>Irregular stones: nearest-of-seven-points cells with their own shade and dark seams.</summary>
    static void Blotches(Color[] px, int seed)
    {
        const int n = 7;
        Span<float> cx = stackalloc float[n], cy = stackalloc float[n], shade = stackalloc float[n];
        for (int i = 0; i < n; i++)
        {
            cx[i] = Hash.Unit(seed, i, 1, 0) * Size;
            cy[i] = Hash.Unit(seed, i, 2, 0) * Size;
            shade[i] = 0.85f + Hash.Unit(seed, i, 3, 0) * 0.3f;
        }
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d1 = float.MaxValue, d2 = float.MaxValue; int best = 0;
                for (int i = 0; i < n; i++)
                {
                    // Wrapped distance so the texture tiles seamlessly.
                    float dx = MathF.Abs(x + 0.5f - cx[i]); dx = MathF.Min(dx, Size - dx);
                    float dy = MathF.Abs(y + 0.5f - cy[i]); dy = MathF.Min(dy, Size - dy);
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; best = i; }
                    else if (d < d2) d2 = d;
                }
                var c = px[y * Size + x];
                px[y * Size + x] = d2 - d1 < 1.0f ? c.Darkened(0.4f) : new Color(c.R * shade[best], c.G * shade[best], c.B * shade[best]);
            }
    }

    static void Boards(Color[] px)
    {
        for (int y = 3; y < Size; y += 4)
            for (int x = 0; x < Size; x++) px[y * Size + x] = px[y * Size + x].Darkened(0.3f);
    }

    static void Bricks(Color[] px)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int offset = (y / 4) % 2 == 0 ? 0 : 4;
                bool mortar = y % 4 == 3 || (x + offset) % 8 == 7;
                if (mortar) px[y * Size + x] = px[y * Size + x].Darkened(0.35f);
            }
    }

    /// <summary>FNV-1a; string.GetHashCode is randomized per process.</summary>
    static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return (int)h;
        }
    }
}
