using System.Collections.Generic;
using System.Linq;
using Modding;
using Modding.Modules;
using UnityEngine;

namespace SpecialEffectsMod
{
    // Draws a line of text in the world. The mesh comes from DynamicText, which
    // Besiege ships (DynamicText.dll) and uses for its own in-level signs; the
    // fonts come out of the mod's asset bundle.
    public class TextBlockBehaviour : BlockModuleBehaviour<TextBlock>
    {
        // Frames of simulation to wait before hiding the block's own mesh. The
        // visual controller is not ready on frame zero.
        private const int HideVisualsFrame = 3;

        private ModAssetBundle fontBundle;
        private IDictionary<string, Font> fontDict = new Dictionary<string, Font>();
        private IDictionary<string, FontStyle> fontStyleDict = new Dictionary<string, FontStyle>();
        private MMenu fontMenu;
        private MMenu fontStyleMenu;

        private GameObject textObject;
        private DynamicText textComponent;

        private MText displayText;
        private MSlider textSize;
        private MSlider textOpacity;
        private MColourSlider textColor;
        private MSlider letterSpacing;
        private MToggle collider;

        private bool hasStarted;
        private int startFrames;

        public override void SafeAwake()
        {
            Skins.Hide(BlockBehaviour);
            fontBundle = ModResource.GetAssetBundle("FontsBundle");
            foreach (Font font in fontBundle.AssetBundle.LoadAllAssets<Font>())
                fontDict.Add(font.name, font);
            fontMenu = AddMenu("FontMenuKey", 0, fontDict.Keys.ToList(), false);

            fontStyleDict.Add("Normal", FontStyle.Normal);
            fontStyleDict.Add("Bold", FontStyle.Bold);
            fontStyleDict.Add("Italic", FontStyle.Italic);
            fontStyleDict.Add("Bold and Italic", FontStyle.BoldAndItalic);
            fontStyleMenu = AddMenu("FontStyleMenuKey", 0, fontStyleDict.Keys.ToList(), false);

            CreateTextObject();

            displayText = AddText("Text", "DisplayTextKey", "Besiege");
            textSize = AddSlider("Size", "DisplayTextSizeKey", 1f, 0f, 10f);
            textOpacity = AddSlider("Opacity", "DisplayTextOpacityKey", 0.75f, 0f, 1f);
            textColor = AddColourSlider("Color", "DisplayTextColor", Color.yellow, false);
            letterSpacing = AddSliderUnclamped("Letter Spacing", "DisplayTextLetterSpacing", 0f, -1f, 1f);
            collider = AddToggle("Collider", "DisplayTextCollider", true);

            fontMenu.ValueChanged += FontChanged;
            fontStyleMenu.ValueChanged += FontStyleChanged;
            displayText.TextChanged += TextChanged;
            textSize.ValueChanged += SizeChanged;
            textOpacity.ValueChanged += OpacityChanged;
            textColor.ValueChanged += ColorChanged;
            letterSpacing.ValueChanged += LetterSpacingChanged;
        }

        // The text lives on a child object so it survives the block's own mesh
        // being hidden.
        private void CreateTextObject()
        {
            bool created;
            textObject = Attach.Child(gameObject.transform, "TextHandler", out created);
            if (created)
            {
                textObject.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                textObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                textObject.transform.localScale = new Vector3(1f, 1f, 1f);
            }

            textComponent = Attach.Component<DynamicText>(textObject);
            textComponent.alignment = TextAlignment.Center;
            textComponent.anchor = DynamicTextAnchor.MiddleCenter;
        }

        private void FontChanged(int value)
        {
            textComponent.font = fontDict[fontMenu.Selection];
        }

        private void FontStyleChanged(int value)
        {
            textComponent.fontStyle = fontStyleDict[fontStyleMenu.Selection];
        }

        private void TextChanged(string text)
        {
            textComponent.SetText(text);
        }

        private void SizeChanged(float value)
        {
            textComponent.size = value;
        }

        private void OpacityChanged(float value)
        {
            ApplyColor();
        }

        private void ColorChanged(Color color)
        {
            ApplyColor();
        }

        private void LetterSpacingChanged(float value)
        {
            textComponent.letterSpacing = value;
        }

        // Colour and opacity are one setting to DynamicText, so both sliders land
        // here. The font's material needs the tint too: the mesh colour alone does
        // not reach the glyph shader.
        private void ApplyColor()
        {
            Color col = textColor.Value;
            Color tint = new Color(col.r, col.g, col.b, textOpacity.Value);
            textComponent.color = tint;
            textComponent.font.material.SetColor("_TintColor", tint);
        }

        public override void SimulateUpdateHost()
        {
            if (hasStarted) return;

            // A collider-less text block is meant to be decoration, so it also
            // loses its mass rather than hanging weight off the machine.
            Rigidbody.detectCollisions = collider.IsActive;
            Rigidbody.mass = collider.IsActive ? 0.1f : 0f;

            if (startFrames == HideVisualsFrame)
            {
                hasStarted = true;
                VisualController.SetInvisible();
            }
            else
            {
                startFrames++;
            }
        }

        // Besiege keeps the behaviour alive between runs, so this has to be armed
        // again or a second run never hides the mesh.
        public override void OnSimulateStart()
        {
            hasStarted = false;
            startFrames = 0;
        }
    }
}
