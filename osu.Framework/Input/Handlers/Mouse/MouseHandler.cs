// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using osu.Framework.Bindables;
using osu.Framework.Input.StateChanges;
using osu.Framework.Platform;
using osu.Framework.Statistics;
using osuTK;
using osuTK.Input;

namespace osu.Framework.Input.Handlers.Mouse
{
    /// <summary>
    /// Handles mouse events from an <see cref="SDL2DesktopWindow"/>.
    /// Will use relative mouse mode where possible.
    /// </summary>
    public class MouseHandler : InputHandler, IHasCursorSensitivity, INeedsMousePositionFeedback
    {
        /// <summary>
        /// Whether relative mode should be preferred when the window has focus and the cursor is contained.
        /// </summary>
        public BindableBool UseRelativeMode { get; } = new BindableBool(true)
        {
            Description = "Allows for sensitivity adjustment and tighter control of input",
        };

        public BindableDouble Sensitivity { get; } = new BindableDouble(1)
        {
            MinValue = 0.1,
            MaxValue = 10,
            Precision = 0.01
        };

        public override string Description => "Mouse";

        public override bool IsActive => true;

        public override int Priority => 0;

        private SDL2DesktopWindow window;

        private Vector2? lastPosition;

        private IBindable<bool> isActive;
        private IBindable<bool> cursorInWindow;

        /// <summary>
        /// Whether a non-relative mouse event has ever been received.
        /// This is used as a starting location for relative movement.
        /// </summary>
        private bool absolutePositionReceived;

        public override bool Initialize(GameHost host)
        {
            if (!base.Initialize(host))
                return false;

            if (!(host.Window is SDL2DesktopWindow desktopWindow))
                return false;

            window = desktopWindow;

            isActive = window.IsActive.GetBoundCopy();
            isActive.BindValueChanged(_ => updateRelativeMode());

            cursorInWindow = host.Window.CursorInWindow.GetBoundCopy();
            cursorInWindow.BindValueChanged(_ => updateRelativeMode());

            UseRelativeMode.BindValueChanged(_ =>
            {
                if (window != null)
                    updateRelativeMode();
            });

            Enabled.BindValueChanged(enabled =>
            {
                updateRelativeMode();

                if (enabled.NewValue)
                {
                    window.MouseMove += HandleMouseMove;
                    window.MouseMoveRelative += HandleMouseMoveRelative;
                    window.MouseDown += handleMouseDown;
                    window.MouseUp += handleMouseUp;
                    window.MouseWheel += handleMouseWheel;
                }
                else
                {
                    window.MouseMove -= HandleMouseMove;
                    window.MouseMoveRelative -= HandleMouseMoveRelative;
                    window.MouseDown -= handleMouseDown;
                    window.MouseUp -= handleMouseUp;
                    window.MouseWheel -= handleMouseWheel;
                }
            }, true);

            return true;
        }

        public void FeedbackMousePositionChange(Vector2 position)
        {
            if (!Enabled.Value)
                return;

            if (window.RelativeMouseMode)
            {
                updateRelativeMode();

                // store the last mouse position to propagate back to the host window manager when exiting relative mode.
                lastPosition = position;

                // handle the case where relative / raw input is active, but the cursor may have exited the window
                // bounds and is not intended to be confined.
                if (!window.CursorConfined)
                {
                    bool positionOutsideWindow = position.X < 0 || position.Y < 0 || position.X >= window.Size.Width || position.Y >= window.Size.Height;

                    if (positionOutsideWindow)
                    {
                        // setting relative mode to false will allow the window manager to take control until the next
                        // updateRelativeMode() call succeeds (likely from the cursor returning inside the window).
                        window.RelativeMouseMode = false;
                        transferLastPositionToHostCursor();
                    }
                }
            }
        }

        public override void Reset()
        {
            Sensitivity.SetDefault();
            base.Reset();
        }

        private void updateRelativeMode()
        {
            window.RelativeMouseMode = UseRelativeMode.Value && Enabled.Value && absolutePositionReceived && (isActive.Value && (window.CursorInWindow.Value || window.CursorConfined));

            if (!window.RelativeMouseMode)
                transferLastPositionToHostCursor();
        }

        protected virtual void HandleMouseMove(Vector2 position)
        {
            absolutePositionReceived = true;
            enqueueInput(new MousePositionAbsoluteInput { Position = position });
        }

        protected virtual void HandleMouseMoveRelative(Vector2 delta)
        {
            enqueueInput(new MousePositionRelativeInput { Delta = delta * (float)Sensitivity.Value });
        }

        private void handleMouseDown(MouseButton button) => enqueueInput(new MouseButtonInput(button, true));

        private void handleMouseUp(MouseButton button) => enqueueInput(new MouseButtonInput(button, false));

        private void handleMouseWheel(Vector2 delta, bool precise) => enqueueInput(new MouseScrollRelativeInput { Delta = delta, IsPrecise = precise });

        private void enqueueInput(IInput input)
        {
            PendingInputs.Enqueue(input);
            FrameStatistics.Increment(StatisticsCounterType.MouseEvents);
        }

        private void transferLastPositionToHostCursor()
        {
            // while a noop on windows, some platforms (macOS) will not warp the host mouse when in relative mode.
            Debug.Assert(!window.RelativeMouseMode);

            if (lastPosition != null)
            {
                window.UpdateMousePosition(lastPosition.Value);
                lastPosition = null;
            }
        }
    }
}
