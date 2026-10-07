using System;
using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Short-lived pooled cube that stands in for a destroyed pixel: it appears at the cell, shrinks from
    /// full size to zero over <see cref="ShrinkDuration"/> seconds (ease-in), then bursts debris and reports
    /// back to its owner so it can be returned to a pool. Colour is applied through a shared static
    /// <see cref="MaterialPropertyBlock"/> (no per-instance material copies).
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class PixelCellView : MonoBehaviour
    {
        /// <summary>
        /// Time in seconds for the cube to shrink from its start size to zero.
        /// </summary>
        public const float ShrinkDuration = 0.12f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock s_props;

        private MeshRenderer _renderer;
        private Vector3 _worldPos;
        private Color32 _color;
        private float _size;
        private float _elapsed;
        private bool _playing;
        private DebrisFx _debris;
        private Action<PixelCellView> _onFinished;

        /// <summary>
        /// True while the shrink animation is running.
        /// </summary>
        public bool IsPlaying => _playing;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Starts the destroy animation. The cube is placed at <paramref name="worldPos"/>, coloured
        /// <paramref name="color"/> and scaled to <paramref name="size"/>; it shrinks to zero in
        /// <see cref="ShrinkDuration"/> seconds (ease-in), then calls <c>debris.Burst(worldPos, color, size)</c> (debris scales with the pixel)
        /// (skipped if debris is null) and finally <paramref name="onFinished"/>(this).
        /// </summary>
        /// <param name="worldPos">World-space centre of the cell.</param>
        /// <param name="color">Pixel colour (sRGB palette entry).</param>
        /// <param name="size">Start edge length of the cube in world units.</param>
        /// <param name="debris">Particle burst played when the cube disappears; may be null.</param>
        /// <param name="onFinished">Completion callback; should be a delegate cached by the caller, not a new lambda per call.</param>
        public void Play(Vector3 worldPos, Color32 color, float size, DebrisFx debris, Action<PixelCellView> onFinished)
        {
            if (_renderer == null)
                _renderer = GetComponent<MeshRenderer>();
            if (s_props == null)
                s_props = new MaterialPropertyBlock();

            _worldPos = worldPos;
            _color = color;
            _size = size;
            _elapsed = 0f;
            _debris = debris;
            _onFinished = onFinished;
            _playing = true;

            Transform t = transform;
            t.position = worldPos;
            t.localScale = new Vector3(size, size, size);

            s_props.SetColor(BaseColorId, (Color)color);
            _renderer.SetPropertyBlock(s_props);
        }

        private void Update()
        {
            if (!_playing)
                return;

            _elapsed += Time.deltaTime;
            float k = _elapsed >= ShrinkDuration ? 1f : _elapsed / ShrinkDuration;
            float eased = k * k; // ease-in quad
            float s = _size * (1f - eased);
            transform.localScale = new Vector3(s, s, s);

            if (k < 1f)
                return;

            _playing = false;
            DebrisFx debris = _debris;
            Action<PixelCellView> onFinished = _onFinished;
            _debris = null;
            _onFinished = null;

            if (debris != null)
                debris.Burst(_worldPos, _color, _size);
            onFinished?.Invoke(this);
        }
    }
}
