using PixelFlow.Data;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Core
{
    /// <summary>
    /// Encapsulates all runtime state for a single level session.
    /// </summary>
    public sealed class LevelSession
    {
        /// <summary>
        /// The pixel grid model for this level.
        /// </summary>
        public PixelGridModel Grid { get; private set; }

        /// <summary>
        /// The waiting slots (capacity <c>slotCount</c>): tanks that finished a belt lap with ammo left.
        /// </summary>
        public SlotQueueManager Tray { get; private set; }

        /// <summary>
        /// The supply model (available tanks).
        /// </summary>
        public SupplyModel Supply { get; private set; }

        /// <summary>
        /// The conveyor belt: capacity <c>slotCount</c>, path around the <c>width x height</c> board.
        /// </summary>
        public BeltModel Belt { get; private set; }

        /// <summary>
        /// The belt shooting logic for this level. It uses <see cref="Tray"/> as the waiting slots.
        /// </summary>
        public BeltShootingLogic BeltShooting { get; private set; }

        /// <summary>
        /// The color palette for this level.
        /// </summary>
        public Color32[] Palette { get; private set; }

        /// <summary>
        /// Width of the grid in cells.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Height of the grid in cells.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Creates a new level session from level data.
        /// </summary>
        /// <param name="data">The level data to create the session from.</param>
        /// <returns>A configured level session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when palette, tanks or cells is null, tank.colorId >= palette.Length, a non-empty cell value
        /// >= palette.Length, cells.Length != width * height, a tank has no ammo, laneCount &lt; 1, or
        /// slotCount &lt; 1. Create has no side effects, so a throw leaves any existing session untouched.
        /// </exception>
        public static LevelSession Create(LevelData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.palette == null)
            {
                throw new ArgumentException("palette must not be null", nameof(data));
            }

            if (data.tanks == null)
            {
                throw new ArgumentException("tanks must not be null", nameof(data));
            }

            if (data.cells == null)
            {
                throw new ArgumentException("cells must not be null", nameof(data));
            }

            // Validate parameters
            if (data.laneCount < 1)
            {
                throw new ArgumentException("laneCount must be at least 1", nameof(data));
            }

            if (data.slotCount < 1)
            {
                throw new ArgumentException("slotCount must be at least 1", nameof(data));
            }

            // Validate tank colors
            for (int i = 0; i < data.tanks.Length; i++)
            {
                if (data.tanks[i].colorId >= data.palette.Length)
                {
                    throw new ArgumentException(
                        $"Tank {i} has colorId {data.tanks[i].colorId} which is outside palette range (0-{data.palette.Length - 1})",
                        nameof(data));
                }
            }

            // Validate cell colors
            byte[] cells = data.cells;
            int paletteLength = data.palette.Length;
            for (int i = 0; i < cells.Length; i++)
            {
                byte cell = cells[i];
                if (cell != LevelData.EmptyCell && cell >= paletteLength)
                {
                    throw new ArgumentException(
                        $"Cell {i} has colorId {cell} which is outside palette range (0-{paletteLength - 1})",
                        nameof(data));
                }
            }

            // Create tank models (ID = index in data.tanks)
            var tankModels = new List<ColorTankModel>(data.tanks.Length);
            for (int i = 0; i < data.tanks.Length; i++)
            {
                var tankData = data.tanks[i];
                tankModels.Add(new ColorTankModel(i, tankData.colorId, tankData.ammo));
            }

            // Create session
            var session = new LevelSession();
            session.Width = data.width;
            session.Height = data.height;
            session.Palette = data.palette;

            // Create models
            session.Grid = new PixelGridModel(data.width, data.height, data.cells);
            session.Tray = new SlotQueueManager(data.slotCount);
            session.Supply = new SupplyModel(tankModels, data.laneCount);
            session.Belt = new BeltModel(data.slotCount, new BeltPath(data.width, data.height));
            session.BeltShooting = new BeltShootingLogic(session.Grid, session.Belt, session.Tray);

            return session;
        }
    }
}
