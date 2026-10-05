using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PixelFlow.Data
{
    /// <summary>
    /// Serializes and deserializes LevelData to/from binary and JSON formats with RLE compression.
    /// </summary>
    public static class LevelSerializer
    {
        private const string MagicHeader = "PXF1";
        private const byte Version = 1;

        #region Binary Serialization

        /// <summary>
        /// Serializes a LevelData instance to a binary byte array with RLE compression.
        /// </summary>
        /// <param name="level">The level data to serialize.</param>
        /// <returns>Compressed binary representation of the level.</returns>
        /// <exception cref="ArgumentException">Thrown when field values exceed binary format limits.</exception>
        public static byte[] ToBytes(LevelData level)
        {
            ValidateFieldWidths(level);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Header
                writer.Write(MagicHeader.ToCharArray());
                writer.Write(Version);
                writer.Write((ushort)level.width);
                writer.Write((ushort)level.height);

                // Palette
                writer.Write((byte)level.palette.Length);
                foreach (var color in level.palette)
                {
                    writer.Write(color.r);
                    writer.Write(color.g);
                    writer.Write(color.b);
                    writer.Write(color.a);
                }

                // Cells (RLE compressed)
                WriteRLE(writer, level.cells);

                // Tanks
                writer.Write((ushort)level.tanks.Length);
                foreach (var tank in level.tanks)
                {
                    writer.Write(tank.colorId);
                    writer.Write((ushort)tank.ammo);
                }

                // Lanes and slots
                writer.Write((byte)level.laneCount);
                writer.Write((byte)level.slotCount);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserializes binary data into a LevelData instance.
        /// </summary>
        /// <param name="data">Binary data to deserialize.</param>
        /// <param name="target">Target LevelData instance to populate.</param>
        /// <exception cref="InvalidDataException">Thrown when data is corrupted or invalid.</exception>
        public static void FromBytes(byte[] data, LevelData target)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var reader = new BinaryReader(ms))
                {
                    // Validate header
                    char[] magic = reader.ReadChars(4);
                    if (new string(magic) != MagicHeader)
                    {
                        throw new InvalidDataException($"Invalid magic header. Expected '{MagicHeader}', got '{new string(magic)}'");
                    }

                    byte version = reader.ReadByte();
                    if (version != Version)
                    {
                        throw new InvalidDataException($"Unsupported version {version}");
                    }

                    // Dimensions
                    target.width = reader.ReadUInt16();
                    target.height = reader.ReadUInt16();
                    int expectedCellCount = target.width * target.height;

                    // Palette
                    int paletteCount = reader.ReadByte();
                    target.palette = new Color32[paletteCount];
                    for (int i = 0; i < paletteCount; i++)
                    {
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
                        byte a = reader.ReadByte();
                        target.palette[i] = new Color32(r, g, b, a);
                    }

                    // Cells (RLE compressed)
                    target.cells = ReadRLE(reader, expectedCellCount);

                    // Tanks
                    int tankCount = reader.ReadUInt16();
                    target.tanks = new ColorTankData[tankCount];
                    for (int i = 0; i < tankCount; i++)
                    {
                        target.tanks[i] = new ColorTankData
                        {
                            colorId = reader.ReadByte(),
                            ammo = reader.ReadUInt16()
                        };
                    }

                    // Lanes and slots
                    target.laneCount = reader.ReadByte();
                    target.slotCount = reader.ReadByte();
                }
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("Unexpected end of stream while reading level data", ex);
            }
            catch (InvalidDataException)
            {
                throw; // Re-throw our own exceptions
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Failed to deserialize level data", ex);
            }
        }

        private static void WriteRLE(BinaryWriter writer, byte[] cells)
        {
            var runs = new List<(byte colorId, int length)>();

            if (cells.Length > 0)
            {
                byte currentColor = cells[0];
                int currentLength = 1;

                for (int i = 1; i < cells.Length; i++)
                {
                    if (cells[i] == currentColor && currentLength < ushort.MaxValue)
                    {
                        currentLength++;
                    }
                    else
                    {
                        runs.Add((currentColor, currentLength));
                        currentColor = cells[i];
                        currentLength = 1;
                    }
                }

                runs.Add((currentColor, currentLength));
            }

            writer.Write(runs.Count);
            foreach (var run in runs)
            {
                writer.Write(run.colorId);
                writer.Write((ushort)run.length);
            }
        }

        private static byte[] ReadRLE(BinaryReader reader, int expectedCellCount)
        {
            int runCount = reader.ReadInt32();
            var cells = new List<byte>(expectedCellCount);

            for (int i = 0; i < runCount; i++)
            {
                byte colorId = reader.ReadByte();
                int length = reader.ReadUInt16();

                for (int j = 0; j < length; j++)
                {
                    cells.Add(colorId);
                }
            }

            if (cells.Count != expectedCellCount)
            {
                throw new InvalidDataException($"Run-length decoded cell count {cells.Count} does not match expected size {expectedCellCount}");
            }

            return cells.ToArray();
        }

        private static void ValidateFieldWidths(LevelData level)
        {
            if (level.width > ushort.MaxValue)
                throw new ArgumentException($"Width {level.width} exceeds maximum {ushort.MaxValue}");

            if (level.height > ushort.MaxValue)
                throw new ArgumentException($"Height {level.height} exceeds maximum {ushort.MaxValue}");

            if (level.palette.Length > 254)
                throw new ArgumentException($"Palette size {level.palette.Length} exceeds maximum 254");

            if (level.laneCount > byte.MaxValue)
                throw new ArgumentException($"Lane count {level.laneCount} exceeds maximum {byte.MaxValue}");

            if (level.slotCount > byte.MaxValue)
                throw new ArgumentException($"Slot count {level.slotCount} exceeds maximum {byte.MaxValue}");

            foreach (var tank in level.tanks)
            {
                if (tank.ammo > ushort.MaxValue)
                    throw new ArgumentException($"Tank ammo {tank.ammo} exceeds maximum {ushort.MaxValue}");
            }
        }

        #endregion

        #region JSON Serialization

        /// <summary>
        /// Serializes a LevelData instance to a JSON string.
        /// </summary>
        /// <param name="level">The level data to serialize.</param>
        /// <returns>JSON representation of the level.</returns>
        public static string ToJson(LevelData level)
        {
            var dto = new LevelDataDTO
            {
                width = level.width,
                height = level.height,
                palette = PaletteToStrings(level.palette),
                cellsRle = CellsToBase64RLE(level.cells),
                tanks = level.tanks,
                laneCount = level.laneCount,
                slotCount = level.slotCount
            };

            return JsonUtility.ToJson(dto, prettyPrint: false);
        }

        /// <summary>
        /// Deserializes JSON data into a LevelData instance.
        /// </summary>
        /// <param name="json">JSON string to deserialize.</param>
        /// <param name="target">Target LevelData instance to populate.</param>
        public static void FromJson(string json, LevelData target)
        {
            var dto = JsonUtility.FromJson<LevelDataDTO>(json);

            target.width = dto.width;
            target.height = dto.height;
            target.palette = StringsToPalette(dto.palette);
            target.cells = Base64RLEToCells(dto.cellsRle, dto.width * dto.height);
            target.tanks = dto.tanks;
            target.laneCount = dto.laneCount;
            target.slotCount = dto.slotCount;
        }

        private static string[] PaletteToStrings(Color32[] palette)
        {
            var result = new string[palette.Length];
            for (int i = 0; i < palette.Length; i++)
            {
                var c = palette[i];
                result[i] = $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
            }
            return result;
        }

        private static Color32[] StringsToPalette(string[] paletteStrings)
        {
            var result = new Color32[paletteStrings.Length];
            for (int i = 0; i < paletteStrings.Length; i++)
            {
                string hex = paletteStrings[i].TrimStart('#');
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                byte a = Convert.ToByte(hex.Substring(6, 2), 16);
                result[i] = new Color32(r, g, b, a);
            }
            return result;
        }

        private static string CellsToBase64RLE(byte[] cells)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                WriteRLE(writer, cells);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        private static byte[] Base64RLEToCells(string base64Rle, int expectedCellCount)
        {
            byte[] rleBytes = Convert.FromBase64String(base64Rle);
            using (var ms = new MemoryStream(rleBytes))
            using (var reader = new BinaryReader(ms))
            {
                return ReadRLE(reader, expectedCellCount);
            }
        }

        [Serializable]
        private class LevelDataDTO
        {
            public int width;
            public int height;
            public string[] palette;
            public string cellsRle;
            public ColorTankData[] tanks;
            public int laneCount;
            public int slotCount;
        }

        #endregion
    }
}
