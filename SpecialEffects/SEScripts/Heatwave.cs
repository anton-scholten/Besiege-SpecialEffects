using Modding;
using UnityEngine;

namespace SpecialEffectsMod
{
    // The material the Particle Emitter's Heatwave toggle draws its shimmer with:
    // a screen-grab distortion shader that bends whatever is behind the particle,
    // the way air does over something hot.
    //
    // Nothing here is authored in Unity, because this mod ships no shader and no
    // texture for it. The shader is the game's own "Particles/Distort" -- the one
    // Besiege uses for its own heat effects -- and the two textures it reads are
    // generated once in code and shared by every emitter in the machine.
    public static class Heatwave
    {
        private const int Size = 128;       // texture edge, in pixels
        private const int Ripples = 3;      // wave crests from the centre to the rim
        private const float Slope = 6f;     // how steep those waves are

        // The shader's own default for _BumpAmt is 6 and the Heatwave Strength
        // slider defaults to 0.15, so at the default the effect is the strength
        // the shader was authored for.
        private const float StrengthToBumpAmt = 40f;

        private static Shader distort;
        private static bool searched;
        private static Texture2D normals;
        private static Texture2D mask;

        // False when the game has no distortion shader to hand, in which case the
        // Heatwave toggle simply does nothing rather than drawing garbage.
        public static bool Available
        {
            get { return FindShader() != null; }
        }

        public static Material CreateMaterial()
        {
            Material material = new Material(FindShader());
            material.SetTexture("_BumpMap", RippleNormals());
            if (material.HasProperty("_Mask")) material.SetTexture("_Mask", RadialMask());
            return material;
        }

        public static void SetStrength(Material material, float strength)
        {
            if (material.HasProperty("_BumpAmt"))
                material.SetFloat("_BumpAmt", strength * StrengthToBumpAmt);
        }

        // "Particles/Distort" is a billboard shader that grabs the screen once a
        // frame and offsets it -- what this effect wants. The fallback is the
        // stained-glass shader the mod loader exposes, which is the same idea on
        // an opaque quad: it will look like a hard-edged pane rather than haze,
        // but it is better than nothing.
        private static Shader FindShader()
        {
            if (searched) return distort;
            searched = true;

            distort = Shader.Find("Particles/Distort");
            if (distort == null)
            {
                distort = GameMaterials.Shaders.Particles.StainedBumpDistort;
                ModConsole.Log("Heatwave: Particles/Distort not found, falling back.");
            }
            if (distort == null) ModConsole.Log("Heatwave: no distortion shader; the effect is off.");
            return distort;
        }

        // Concentric waves, as normals rather than heights, because that is what
        // the shader offsets the screen by.
        //
        // The waves fade to nothing before they reach the edge of the square. That
        // is what keeps the particle from showing as a rectangle: with a flat rim
        // the offset there is zero, so the quad's own boundary bends nothing and
        // stays invisible.
        private static Texture2D RippleNormals()
        {
            if (normals != null) return normals;

            float[] height = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.5f) / Size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    if (r >= 1f) continue;

                    // Raised cosine: 1 at the centre, and both nought and flat at
                    // the rim, so the waves die out rather than being cut off.
                    float fade = 0.5f + 0.5f * Mathf.Cos(r * Mathf.PI);
                    height[y * Size + x] = Mathf.Sin(r * Ripples * 2f * Mathf.PI) * fade * fade;
                }
            }

            Color[] pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = height[y * Size + Step(x, 1)] - height[y * Size + Step(x, -1)];
                    float dy = height[Step(y, 1) * Size + x] - height[Step(y, -1) * Size + x];
                    Vector3 normal = new Vector3(-dx * Slope, -dy * Slope, 1f).normalized;

                    // X goes in red *and* alpha. The shader unpacks the normal the
                    // DXT5nm way -- alpha for X, green for Y -- while a plain RGB
                    // unpack reads red and green, and this reads correctly either
                    // way. Z is written but recomputed by the DXT5nm path.
                    pixels[y * Size + x] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        normal.x * 0.5f + 0.5f);
                }
            }

            normals = Build(pixels);
            return normals;
        }

        // How much of the distortion survives, per pixel: all of it at the centre,
        // none at the rim. Belt and braces with the flat rim above -- if the
        // shader ever stops honouring one, the other still hides the quad edge.
        private static Texture2D RadialMask()
        {
            if (mask != null) return mask;

            Color[] pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.5f) / Size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = r >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(r * Mathf.PI);
                    pixels[y * Size + x] = new Color(a, a, a, a);
                }
            }

            mask = Build(pixels);
            return mask;
        }

        // Linear, not sRGB: both textures are read as numbers -- a direction and a
        // multiplier -- and not as colour. HideAndDontSave keeps them alive across
        // a level change, since they are static and built once.
        private static Texture2D Build(Color[] pixels)
        {
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static int Step(int i, int delta)
        {
            return Mathf.Clamp(i + delta, 0, Size - 1);
        }
    }
}
