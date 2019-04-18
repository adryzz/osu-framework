﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osuTK;
using osuTK.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;

namespace osu.Framework.Graphics.UserInterface
{
    /// <summary>
    /// A basic checkbox for framework internal use and for prototyping UI.
    /// </summary>
    public class BasicCheckbox : Checkbox
    {
        /// <summary>
        /// The color of the checkbox when the checkbox is checked. Defaults to White
        /// </summary>
        /// <remarks>
        /// The changes done to this property are only applied when <see cref="Checkbox.Current"/>'s value changes.
        /// </remarks>
        public Color4 CheckedColor { get; set; } = Color4.White;

        /// <summary>
        /// The color of the checkbox when the checkbox is not checked. Default is an white with low opacity.
        /// </summary>
        /// <remarks>
        /// The changes done to this property are only applied when <see cref="Checkbox.Current"/>'s value changes.
        /// </remarks>
        public Color4 UncheckedColor { get; set; } = Color4.White.Opacity(0.2f);

        /// <summary>
        /// The length of the duration between checked and unchecked.
        /// </summary>
        /// <remarks>
        /// Changes to this property only affect future transitions between checked and unchecked.
        /// Transitions between checked and unchecked that are already in progress are unaffected.
        /// </remarks>
        public int FadeDuration { get; set; } = 50;

        /// <summary>
        /// The text in the label.
        /// </summary>
        public string LabelText
        {
            get => labelSpriteText.Text;
            set => labelSpriteText.Text = value;
        }

        /// <summary>
        /// The spacing between the checkbox and the label.
        /// </summary>
        public float LabelSpacing
        {
            get => fillFlowContainer.Spacing.X;
            set => fillFlowContainer.Spacing = new Vector2(value, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        public bool RightHandedCheckbox
        {
            get => fillFlowContainer.GetLayoutPosition(labelSpriteText) < -0.5f;
            set => fillFlowContainer.SetLayoutPosition(labelSpriteText, value ? -1 : 1);
        }

        private readonly SpriteText labelSpriteText;
        private readonly FillFlowContainer fillFlowContainer;

        public BasicCheckbox()
        {
            Box box;

            AutoSizeAxes = Axes.Both;

            Child = fillFlowContainer = new FillFlowContainer
            {
                Direction = FillDirection.Horizontal,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    new Container
                    {
                        BorderColour = Color4.White,
                        BorderThickness = 3,
                        Masking = true,
                        Size = new Vector2(20, 20),
                        Child = box = new Box
                        {
                            RelativeSizeAxes = Axes.Both
                        }
                    },
                    labelSpriteText = new SpriteText
                    {
                        Depth = float.MinValue
                    },
                }
            };

            Current.ValueChanged += e => box.FadeColour(e.NewValue ? CheckedColor : UncheckedColor, FadeDuration);
            Current.TriggerChange();
        }
    }
}
