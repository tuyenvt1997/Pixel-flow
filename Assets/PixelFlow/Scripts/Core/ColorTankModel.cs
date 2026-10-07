using System;

namespace PixelFlow.Core
{
    /// <summary>
    /// Represents a color tank with finite ammo that can be consumed.
    /// Fires events when ammo changes and when the tank depletes.
    /// </summary>
    public sealed class ColorTankModel
    {
        private int _ammo;
        private bool _isDepleted;

        /// <summary>
        /// Unique identifier for this tank.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// The color ID (palette index) that this tank shoots.
        /// </summary>
        public byte ColorId { get; }

        /// <summary>
        /// Current remaining ammo count.
        /// </summary>
        public int Ammo => _ammo;

        /// <summary>
        /// Whether this tank has been depleted (ammo reached zero).
        /// </summary>
        public bool IsDepleted => _isDepleted;

        /// <summary>
        /// Raised when ammo changes. Parameters: (tank, newAmmo).
        /// </summary>
        public event Action<ColorTankModel, int> OnAmmoChanged;

        /// <summary>
        /// Raised exactly once when the tank depletes (ammo reaches zero).
        /// Fires after OnAmmoChanged.
        /// </summary>
        public event Action<ColorTankModel> OnDepleted;

        /// <summary>
        /// Creates a new color tank with the specified properties.
        /// </summary>
        /// <param name="id">Unique tank identifier.</param>
        /// <param name="colorId">Color palette index.</param>
        /// <param name="ammo">Initial ammo count.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when ammo is less than or equal to zero.</exception>
        public ColorTankModel(int id, byte colorId, int ammo)
        {
            if (ammo <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ammo), "Ammo must be greater than zero.");
            }

            Id = id;
            ColorId = colorId;
            _ammo = ammo;
            _isDepleted = false;
        }

        /// <summary>
        /// Attempts to consume one unit of ammo.
        /// </summary>
        /// <returns>True if ammo was consumed; false if the tank is already depleted.</returns>
        public bool TryConsume()
        {
            if (_isDepleted)
            {
                return false;
            }

            _ammo--;
            OnAmmoChanged?.Invoke(this, _ammo);

            if (_ammo == 0)
            {
                _isDepleted = true;
                OnDepleted?.Invoke(this);
            }

            return true;
        }
    }
}
