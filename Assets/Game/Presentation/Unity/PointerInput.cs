using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>
    /// The only place that touches the Input System. <see cref="Pointer.current"/> is the mouse in the Editor
    /// and the primary touch on a phone, so the same code drives both.
    /// </summary>
    internal static class PointerInput
    {
        public static bool TryRead(out Vector2 position, out bool pressed)
        {
            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                position = default;
                pressed = false;
                return false;
            }

            position = pointer.position.ReadValue();
            pressed = pointer.press.isPressed;
            return true;
        }
    }
}
