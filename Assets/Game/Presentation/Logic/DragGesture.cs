using System;

namespace Game.Presentation
{
    public enum GestureEvent
    {
        None = 0,
        /// <summary>The pointer moved past the threshold while pressed: start dragging the picked item.</summary>
        DragStarted,
        /// <summary>Pressed and released without passing the threshold.</summary>
        Tap,
        /// <summary>Released after dragging.</summary>
        Drop,
        /// <summary>The interaction was abandoned (app paused, focus lost, pointer lost).</summary>
        Cancelled,
    }

    /// <summary>Pure tap-versus-drag state machine for a single pointer. No allocations.</summary>
    public sealed class DragGesture
    {
        private float _startX;
        private float _startY;

        public DragGesture(float thresholdPx)
        {
            ThresholdPx = thresholdPx;
        }

        public float ThresholdPx { get; set; }
        public bool IsPressed { get; private set; }
        public bool IsDragging { get; private set; }
        public float StartX => _startX;
        public float StartY => _startY;

        public void Press(float x, float y)
        {
            _startX = x;
            _startY = y;
            IsPressed = true;
            IsDragging = false;
        }

        public GestureEvent Move(float x, float y)
        {
            if (!IsPressed || IsDragging)
            {
                return GestureEvent.None;
            }

            float dx = x - _startX, dy = y - _startY;
            if (dx * dx + dy * dy >= ThresholdPx * ThresholdPx)
            {
                IsDragging = true;
                return GestureEvent.DragStarted;
            }

            return GestureEvent.None;
        }

        public GestureEvent Release()
        {
            if (!IsPressed)
            {
                return GestureEvent.None;
            }

            bool wasDragging = IsDragging;
            IsPressed = false;
            IsDragging = false;
            return wasDragging ? GestureEvent.Drop : GestureEvent.Tap;
        }

        public GestureEvent Cancel()
        {
            if (!IsPressed)
            {
                return GestureEvent.None;
            }

            IsPressed = false;
            IsDragging = false;
            return GestureEvent.Cancelled;
        }
    }
}
