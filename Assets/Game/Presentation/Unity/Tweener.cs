using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Minimal allocation-free tween runner for placeholder "juice": moves, pops, shakes and shrink-outs.
    /// Tweens live in a fixed array; starting a tween of the same kind on the same transform replaces it.
    /// </summary>
    internal sealed class Tweener : MonoBehaviour
    {
        public enum Kind
        {
            Move,
            Scale,
            Pop,
            Shake,
            ShrinkOut,
        }

        private struct Tween
        {
            public Transform Target;
            public Kind Kind;
            public Vector3 From;
            public Vector3 To;
            public float Start;
            public float Duration;
            public float Amplitude;
            public ItemView ReleaseView;
        }

        private const int Capacity = 256;
        private readonly Tween[] _tweens = new Tween[Capacity];
        private int _count;

        /// <summary>Called with views whose shrink-out finished, so the owner can return them to the pool.</summary>
        public System.Action<ItemView> Released;

        public int ActiveCount => _count;

        public void Move(Transform t, Vector3 to, float duration) => Add(t, Kind.Move, t.position, to, duration, 0f, null);

        public void ScaleTo(Transform t, float scale, float duration) =>
            Add(t, Kind.Scale, t.localScale, new Vector3(scale, scale, 1f), duration, 0f, null);

        /// <summary>Overshoot "pop" from <paramref name="fromScale"/> to <paramref name="toScale"/>.</summary>
        public void Pop(Transform t, float fromScale, float toScale, float duration)
        {
            t.localScale = new Vector3(fromScale, fromScale, 1f);
            Add(t, Kind.Pop, t.localScale, new Vector3(toScale, toScale, 1f), duration, 0f, null);
        }

        /// <summary>Short horizontal "invalid" wobble around the current position.</summary>
        public void Shake(Transform t, float amplitude, float duration) => Add(t, Kind.Shake, t.position, t.position, duration, amplitude, null);

        /// <summary>Move to <paramref name="to"/> while shrinking to nothing, then release the view.</summary>
        public void ShrinkOut(ItemView view, Vector3 to, float duration) =>
            Add(view.transform, Kind.ShrinkOut, view.transform.position, to, duration, view.transform.localScale.x, view);

        /// <summary>Stops every tween on <paramref name="t"/> (used when a drag grabs an animating item).</summary>
        public void Stop(Transform t)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_tweens[i].Target == t && _tweens[i].Kind != Kind.ShrinkOut)
                {
                    Finish(i, false);
                }
            }
        }

        private void Add(Transform t, Kind kind, Vector3 from, Vector3 to, float duration, float amplitude, ItemView release)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_tweens[i].Target == t && SameChannel(_tweens[i].Kind, kind))
                {
                    _tweens[i] = Make(t, kind, from, to, duration, amplitude, release);
                    return;
                }
            }

            if (_count == Capacity)
            {
                Finish(0, true); // never drop a release: complete the oldest tween immediately
            }

            _tweens[_count++] = Make(t, kind, from, to, duration, amplitude, release);
        }

        private static bool SameChannel(Kind a, Kind b)
        {
            bool aPos = a == Kind.Move || a == Kind.Shake || a == Kind.ShrinkOut;
            bool bPos = b == Kind.Move || b == Kind.Shake || b == Kind.ShrinkOut;
            return aPos == bPos;
        }

        private static Tween Make(Transform t, Kind kind, Vector3 from, Vector3 to, float duration, float amplitude, ItemView release) =>
            new Tween
            {
                Target = t,
                Kind = kind,
                From = from,
                To = to,
                Start = Time.unscaledTime,
                Duration = Mathf.Max(0.01f, duration),
                Amplitude = amplitude,
                ReleaseView = release,
            };

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _count - 1; i >= 0; i--)
            {
                ref Tween tw = ref _tweens[i];
                if (tw.Target == null)
                {
                    RemoveAt(i);
                    continue;
                }

                float p = Mathf.Clamp01((now - tw.Start) / tw.Duration);
                Apply(ref tw, p);
                if (p >= 1f)
                {
                    Finish(i, true);
                }
            }
        }

        private static void Apply(ref Tween tw, float p)
        {
            float easeOut = 1f - (1f - p) * (1f - p) * (1f - p);
            switch (tw.Kind)
            {
                case Kind.Move:
                    tw.Target.position = Vector3.LerpUnclamped(tw.From, tw.To, easeOut);
                    break;
                case Kind.Scale:
                    tw.Target.localScale = Vector3.LerpUnclamped(tw.From, tw.To, easeOut);
                    break;
                case Kind.Pop:
                    // Back-out easing: overshoots ~10% then settles.
                    const float s = 1.70158f;
                    float q = p - 1f;
                    float back = q * q * ((s + 1f) * q + s) + 1f;
                    tw.Target.localScale = Vector3.LerpUnclamped(tw.From, tw.To, back);
                    break;
                case Kind.Shake:
                    float offset = Mathf.Sin(p * Mathf.PI * 6f) * tw.Amplitude * (1f - p);
                    tw.Target.position = tw.From + new Vector3(offset, 0f, 0f);
                    break;
                case Kind.ShrinkOut:
                    tw.Target.position = Vector3.LerpUnclamped(tw.From, tw.To, easeOut);
                    float scale = tw.Amplitude * (1f - easeOut);
                    tw.Target.localScale = new Vector3(scale, scale, 1f);
                    break;
            }
        }

        private void Finish(int index, bool applyEnd)
        {
            Tween tw = _tweens[index];
            RemoveAt(index);
            if (tw.Target == null)
            {
                return;
            }

            if (applyEnd)
            {
                Apply(ref tw, 1f);
            }

            if (tw.Kind == Kind.Shake)
            {
                tw.Target.position = tw.From;
            }

            if (tw.ReleaseView != null)
            {
                Released?.Invoke(tw.ReleaseView);
            }
        }

        private void RemoveAt(int index)
        {
            _count--;
            _tweens[index] = _tweens[_count];
            _tweens[_count] = default;
        }
    }
}
