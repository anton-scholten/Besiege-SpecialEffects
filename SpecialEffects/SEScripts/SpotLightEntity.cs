using Modding;
using UnityEngine;

namespace SpecialEffectsMod
{
    // The pieces the Spot Light entity is built from, shared by the prefab setup
    // in Mod and by the entity's own behaviour.
    public static class SpotLightEntity
    {
        public const string LensName = "LightLens";

        private const string HeadName = "LightHead";
        private const string BeamName = "LightPiece";

        // The name EntityPrefabCreator gives the object carrying the housing mesh.
        private const string VisName = "Vis";

        // The block puts its lens 0.963 forward of the block origin and its
        // housing mesh 0.5 forward of it (SpotLight.xml). The entity draws the
        // same mesh at the same 0.4 scale but centred on its own origin, so the
        // lens sits the same distance in front of the mesh: 0.963 - 0.5.
        private const float LensOffset = 0.463f;
        private const float LensRadius = 0.95f;
        private const float LensDepth = 0.01f;

        private const float IntensityToAlpha = 10f;

        // Everything that must point where the barrel points hangs off one child,
        // so the beam and the lens cannot drift apart.
        //
        // Its rotation is read off the housing rather than written down here. The
        // <Mesh><Rotation> in the XML, whatever the mod loader does with it, and
        // whatever the editor does when the object is turned all land in that one
        // transform, so copying it is right by construction -- where repeating the
        // XML angle as a constant was twice wrong.
        public static void AimAtBarrel(GameObject entity)
        {
            Transform head = Attach.Find(entity.transform, HeadName);
            Transform housing = Attach.Find(entity.transform, VisName);
            if (head != null && housing != null) head.rotation = housing.rotation;
        }

        public static Light Beam(GameObject entity)
        {
            GameObject piece = Attach.Child(Head(entity), BeamName);
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;
            return Attach.Component<Light>(piece);
        }

        // The glowing disc in the housing's rim, the same one the block draws.
        public static GameObject Lens(GameObject entity)
        {
            GameObject lens = Attach.Child(Head(entity), LensName);
            lens.transform.localPosition = Vector3.forward * LensOffset;
            lens.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            lens.transform.localScale = new Vector3(LensRadius, LensDepth, LensRadius);

            Attach.Component<MeshFilter>(lens).mesh = ModResource.GetMesh("Lens");
            Attach.Component<MeshRenderer>(lens).material.shader =
                GameMaterials.Shaders.Particles.AlphaBlended;
            return lens;
        }

        public static void TintLens(MeshRenderer lens, Color tint, float intensity)
        {
            if (lens != null)
                lens.material.SetColor("_TintColor",
                    new Color(tint.r, tint.g, tint.b, intensity / IntensityToAlpha));
        }

        private static Transform Head(GameObject entity)
        {
            GameObject head = Attach.Child(entity.transform, HeadName);
            head.transform.localPosition = Vector3.zero;
            head.transform.localScale = Vector3.one;
            return head.transform;
        }
    }
}
