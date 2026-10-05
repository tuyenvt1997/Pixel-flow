using System;
using System.Globalization;
using PixelFlow.Core;
using TMPro;
using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Presentation of one <see cref="ColorTankModel"/>: a coloured body, a collider for tap picking and a
    /// world-space ammo label. Listens to <see cref="ColorTankModel.OnAmmoChanged"/> while bound; owns its own
    /// position tween (<see cref="MoveTo"/>) and depletion tween (<see cref="PlayDeplete"/>), both driven by
    /// <c>Update</c> without allocation. Instances are pooled by <see cref="TankBoardView"/>.
    /// </summary>
    public sealed class ColorTankView : MonoBehaviour
    {
        /// <summary>
        /// Default duration in seconds of the scale-down played when a tank leaves the tray.
        /// </summary>
        public const float DefaultDepleteDuration = 0.15f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock s_props;
        private static string[] s_ammoLabels = new string[0];

        [Tooltip("Renderer of the tank body; tinted with the tank colour.")]
        [SerializeField] private MeshRenderer body;

        [Tooltip("Collider used for tap picking.")]
        [SerializeField] private BoxCollider pickCollider;

        [Tooltip("World-space label showing the remaining ammo.")]
        [SerializeField] private TextMeshPro ammoText;

        [Tooltip("Optional point projectiles start from. If unset, the top centre of the body bounds is used.")]
        [SerializeField] private Transform muzzle;

        private Action<ColorTankModel, int> _onAmmoChanged;
        private Vector3 _baseScale = Vector3.one;

        private bool _moving;
        private Vector3 _moveFrom;
        private Vector3 _moveTo;
        private float _moveElapsed;
        private float _moveDuration;

        private bool _depleting;
        private float _depleteElapsed;
        private float _depleteDuration;
        private Action<ColorTankView> _onDepleteFinished;

        /// <summary>
        /// The model currently bound to this view, or null when unbound.
        /// </summary>
        public ColorTankModel Model { get; private set; }

        /// <summary>
        /// True while <see cref="PlayDeplete"/> is running.
        /// </summary>
        public bool IsDepleting => _depleting;

        /// <summary>
        /// The collider used for tap picking (may be null if not assigned).
        /// </summary>
        public Collider PickCollider => pickCollider;

        /// <summary>
        /// World-space point projectiles are launched from: the <c>muzzle</c> transform if assigned,
        /// otherwise the top centre of the body's bounds, otherwise this transform's position.
        /// </summary>
        public Vector3 MuzzlePosition
        {
            get
            {
                if (muzzle != null)
                    return muzzle.position;
                if (body != null)
                {
                    Bounds b = body.bounds;
                    return new Vector3(b.center.x, b.max.y, b.center.z);
                }
                return transform.position;
            }
        }

        private void Awake()
        {
            _onAmmoChanged = HandleAmmoChanged;
            _baseScale = transform.localScale;
            if (body == null)
                body = GetComponentInChildren<MeshRenderer>();
            if (pickCollider == null)
                pickCollider = GetComponentInChildren<BoxCollider>();
            if (ammoText == null)
                ammoText = GetComponentInChildren<TextMeshPro>();
        }

        /// <summary>
        /// Binds <paramref name="model"/> to this view: tints the body with <paramref name="color"/>, shows the
        /// current ammo and subscribes to <see cref="ColorTankModel.OnAmmoChanged"/>. Any previous binding is
        /// released first. Resets scale, stops any running tween and re-enables the pick collider.
        /// </summary>
        /// <param name="model">Tank model to present.</param>
        /// <param name="color">Tank colour (sRGB palette entry).</param>
        /// <exception cref="ArgumentNullException">Thrown if model is null.</exception>
        public void Bind(ColorTankModel model, Color32 color)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (_onAmmoChanged == null)
                _onAmmoChanged = HandleAmmoChanged;

            Unbind();

            Model = model;
            model.OnAmmoChanged += _onAmmoChanged;

            if (body != null)
            {
                if (s_props == null)
                    s_props = new MaterialPropertyBlock();
                s_props.SetColor(BaseColorId, (Color)color);
                body.SetPropertyBlock(s_props);
            }

            if (pickCollider != null)
                pickCollider.enabled = true;

            EnsureAmmoLabels(model.Ammo);
            SetAmmoText(model.Ammo);
        }

        /// <summary>
        /// Unsubscribes from the bound model (if any), clears <see cref="Model"/>, stops all tweens and restores
        /// the original scale. Safe to call when already unbound. A pending depletion callback is dropped.
        /// </summary>
        public void Unbind()
        {
            if (Model != null)
            {
                Model.OnAmmoChanged -= _onAmmoChanged;
                Model = null;
            }

            _moving = false;
            _depleting = false;
            _onDepleteFinished = null;
            transform.localScale = _baseScale;
        }

        /// <summary>
        /// Places the view at <paramref name="position"/> immediately, cancelling any move tween.
        /// </summary>
        /// <param name="position">World-space target.</param>
        public void SnapTo(Vector3 position)
        {
            _moving = false;
            transform.position = position;
        }

        /// <summary>
        /// Tweens the view from its current position to <paramref name="target"/> over
        /// <paramref name="duration"/> seconds (ease-out quad). A duration of zero or less snaps immediately.
        /// Calling again while moving restarts the tween from the current position.
        /// </summary>
        /// <param name="target">World-space destination.</param>
        /// <param name="duration">Tween duration in seconds.</param>
        public void MoveTo(Vector3 target, float duration = 0.18f)
        {
            if (duration <= 0f)
            {
                SnapTo(target);
                return;
            }

            _moveFrom = transform.position;
            _moveTo = target;
            _moveElapsed = 0f;
            _moveDuration = duration;
            _moving = true;
        }

        /// <summary>
        /// Plays the depletion animation: disables the pick collider and scales the view down to zero over
        /// <paramref name="duration"/> seconds (ease-in quad), then invokes <paramref name="onFinished"/>(this).
        /// The model stays bound until the owner calls <see cref="Unbind"/> (typically from the callback).
        /// </summary>
        /// <param name="onFinished">Completion callback; should be a cached delegate.</param>
        /// <param name="duration">Animation duration in seconds; zero or less finishes immediately.</param>
        public void PlayDeplete(Action<ColorTankView> onFinished, float duration = DefaultDepleteDuration)
        {
            if (pickCollider != null)
                pickCollider.enabled = false;

            _onDepleteFinished = onFinished;
            _depleteElapsed = 0f;
            _depleteDuration = duration;
            _depleting = true;

            if (duration <= 0f)
                FinishDeplete();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_moving)
            {
                _moveElapsed += dt;
                float k = _moveElapsed >= _moveDuration ? 1f : _moveElapsed / _moveDuration;
                float eased = 1f - (1f - k) * (1f - k);
                transform.position = Vector3.LerpUnclamped(_moveFrom, _moveTo, eased);
                if (k >= 1f)
                    _moving = false;
            }

            if (_depleting)
            {
                _depleteElapsed += dt;
                float k = _depleteElapsed >= _depleteDuration ? 1f : _depleteElapsed / _depleteDuration;
                transform.localScale = _baseScale * (1f - k * k);
                if (k >= 1f)
                    FinishDeplete();
            }
        }

        private void FinishDeplete()
        {
            _depleting = false;
            transform.localScale = Vector3.zero;
            Action<ColorTankView> callback = _onDepleteFinished;
            _onDepleteFinished = null;
            callback?.Invoke(this);
        }

        private void HandleAmmoChanged(ColorTankModel tank, int ammo)
        {
            SetAmmoText(ammo);
        }

        private void SetAmmoText(int ammo)
        {
            if (ammoText == null)
                return;

            // Cached labels: TMP_Text.SetText(string, float) rebuilds m_text (a new string) every call in the Editor.
            if (ammo >= 0 && ammo < s_ammoLabels.Length)
                ammoText.text = s_ammoLabels[ammo];
            else
                ammoText.SetText("{0}", ammo);
        }

        /// <summary>
        /// Grows the shared label cache to cover 0..<paramref name="maxAmmo"/>. Called from <see cref="Bind"/>
        /// (level build), never while shooting: ammo only decreases after binding.
        /// </summary>
        private static void EnsureAmmoLabels(int maxAmmo)
        {
            if (maxAmmo < s_ammoLabels.Length)
                return;

            var labels = new string[maxAmmo + 1];
            Array.Copy(s_ammoLabels, labels, s_ammoLabels.Length);
            for (int i = s_ammoLabels.Length; i < labels.Length; i++)
                labels[i] = i.ToString(CultureInfo.InvariantCulture);
            s_ammoLabels = labels;
        }
    }
}
