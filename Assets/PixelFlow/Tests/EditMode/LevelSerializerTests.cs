using NUnit.Framework;
using PixelFlow.Data;
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for LevelSerializer binary and JSON serialization.
    /// </summary>
    public class LevelSerializerTests
    {
        [Test]
        public void ToBytes_FromBytes_RoundTripsAllFields()
        {
            // Arrange: 3x2 level with empty cell, 2 colors, 2 tanks
            var palette = new Color32[]
            {
                new Color32(255, 0, 0, 255),    // color 0: red
                new Color32(0, 255, 0, 255)     // color 1: green
            };

            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 0, ammo = 10 },
                new ColorTankData { colorId = 1, ammo = 20 }
            };

            var original = TestLevels.Create(new[] { "01.", "1.0" }, palette, tanks, lanes: 2, slots: 3);

            // Act: round-trip through binary
            byte[] bytes = LevelSerializer.ToBytes(original);
            var restored = ScriptableObject.CreateInstance<LevelData>();
            LevelSerializer.FromBytes(bytes, restored);

            // Assert: all fields match
            Assert.AreEqual(original.width, restored.width, "width");
            Assert.AreEqual(original.height, restored.height, "height");
            Assert.AreEqual(original.laneCount, restored.laneCount, "laneCount");
            Assert.AreEqual(original.slotCount, restored.slotCount, "slotCount");

            Assert.AreEqual(original.palette.Length, restored.palette.Length, "palette length");
            for (int i = 0; i < original.palette.Length; i++)
            {
                Assert.AreEqual(original.palette[i], restored.palette[i], $"palette[{i}]");
            }

            Assert.AreEqual(original.cells.Length, restored.cells.Length, "cells length");
            for (int i = 0; i < original.cells.Length; i++)
            {
                Assert.AreEqual(original.cells[i], restored.cells[i], $"cells[{i}]");
            }

            Assert.AreEqual(original.tanks.Length, restored.tanks.Length, "tanks length");
            for (int i = 0; i < original.tanks.Length; i++)
            {
                Assert.AreEqual(original.tanks[i].colorId, restored.tanks[i].colorId, $"tanks[{i}].colorId");
                Assert.AreEqual(original.tanks[i].ammo, restored.tanks[i].ammo, $"tanks[{i}].ammo");
            }
        }

        [Test]
        public void ToJson_FromJson_RoundTripsAllFields()
        {
            // Arrange: same as binary test
            var palette = new Color32[]
            {
                new Color32(255, 0, 0, 255),
                new Color32(0, 255, 0, 255)
            };

            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 0, ammo = 10 },
                new ColorTankData { colorId = 1, ammo = 20 }
            };

            var original = TestLevels.Create(new[] { "01.", "1.0" }, palette, tanks, lanes: 2, slots: 3);

            // Act: round-trip through JSON
            string json = LevelSerializer.ToJson(original);
            var restored = ScriptableObject.CreateInstance<LevelData>();
            LevelSerializer.FromJson(json, restored);

            // Assert: all fields match
            Assert.AreEqual(original.width, restored.width, "width");
            Assert.AreEqual(original.height, restored.height, "height");
            Assert.AreEqual(original.laneCount, restored.laneCount, "laneCount");
            Assert.AreEqual(original.slotCount, restored.slotCount, "slotCount");

            Assert.AreEqual(original.palette.Length, restored.palette.Length, "palette length");
            for (int i = 0; i < original.palette.Length; i++)
            {
                Assert.AreEqual(original.palette[i], restored.palette[i], $"palette[{i}]");
            }

            Assert.AreEqual(original.cells.Length, restored.cells.Length, "cells length");
            for (int i = 0; i < original.cells.Length; i++)
            {
                Assert.AreEqual(original.cells[i], restored.cells[i], $"cells[{i}]");
            }

            Assert.AreEqual(original.tanks.Length, restored.tanks.Length, "tanks length");
            for (int i = 0; i < original.tanks.Length; i++)
            {
                Assert.AreEqual(original.tanks[i].colorId, restored.tanks[i].colorId, $"tanks[{i}].colorId");
                Assert.AreEqual(original.tanks[i].ammo, restored.tanks[i].ammo, $"tanks[{i}].ammo");
            }
        }

        [Test]
        public void ToBytes_UniformLevel_IsCompressed()
        {
            // Arrange: 64x64 level with all cells the same color
            var palette = new Color32[] { new Color32(100, 150, 200, 255) };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 50 } };

            string[] rows = new string[64];
            for (int i = 0; i < 64; i++)
            {
                rows[i] = new string('0', 64);
            }

            var level = TestLevels.Create(rows, palette, tanks);

            // Act
            byte[] bytes = LevelSerializer.ToBytes(level);

            // Assert: RLE should compress 4096 cells to under 100 bytes
            Assert.Less(bytes.Length, 100, $"Expected compressed size < 100 bytes, got {bytes.Length}");
        }

        [Test]
        public void FromBytes_5000Cells_LoadsUnder50ms()
        {
            // Arrange: 100x50 level with random colors (seed 42)
            var random = new System.Random(42);
            var palette = new Color32[8];
            for (int i = 0; i < 8; i++)
            {
                palette[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            }

            string[] rows = new string[50];
            for (int y = 0; y < 50; y++)
            {
                var chars = new char[100];
                for (int x = 0; x < 100; x++)
                {
                    chars[x] = (char)('0' + random.Next(8));
                }
                rows[y] = new string(chars);
            }

            var level = TestLevels.Create(rows, palette, new ColorTankData[0]);
            byte[] bytes = LevelSerializer.ToBytes(level);

            var target = ScriptableObject.CreateInstance<LevelData>();

            // Warm-up run
            LevelSerializer.FromBytes(bytes, target);

            // Act: timed run
            var sw = Stopwatch.StartNew();
            LevelSerializer.FromBytes(bytes, target);
            sw.Stop();

            // Assert
            Assert.Less(sw.ElapsedMilliseconds, 50, $"Expected load time < 50ms, got {sw.ElapsedMilliseconds}ms");
        }

        [Test]
        public void FromBytes_BadMagic_Throws()
        {
            // Arrange: invalid magic header
            byte[] badData = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var target = ScriptableObject.CreateInstance<LevelData>();

            // Act & Assert
            Assert.Throws<InvalidDataException>(() => LevelSerializer.FromBytes(badData, target));
        }

        [Test]
        public void FromBytes_Truncated_Throws()
        {
            // Arrange: valid level, then truncate the byte array
            var palette = new Color32[] { new Color32(255, 0, 0, 255) };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 10 } };
            var level = TestLevels.Create(new[] { "00", "00" }, palette, tanks);

            byte[] fullBytes = LevelSerializer.ToBytes(level);
            byte[] truncated = new byte[fullBytes.Length / 2];
            Array.Copy(fullBytes, truncated, truncated.Length);

            var target = ScriptableObject.CreateInstance<LevelData>();

            // Act & Assert: should throw InvalidDataException (wrapping EndOfStreamException)
            Assert.Throws<InvalidDataException>(() => LevelSerializer.FromBytes(truncated, target));
        }

        [Test]
        public void FromBytes_RunsDoNotMatchSize_Throws()
        {
            // Arrange: manually craft binary data with mismatched run lengths
            using (var ms = new MemoryStream())
            using (var writer = new System.IO.BinaryWriter(ms))
            {
                // Header
                writer.Write(new char[] { 'P', 'X', 'F', '1' });
                writer.Write((byte)1); // version
                writer.Write((ushort)2); // width
                writer.Write((ushort)2); // height (total = 4 cells)

                // Palette
                writer.Write((byte)1);
                writer.Write((byte)255); // R
                writer.Write((byte)0);   // G
                writer.Write((byte)0);   // B
                writer.Write((byte)255); // A

                // Runs: total length = 3, but width*height = 4
                writer.Write((int)1); // runCount
                writer.Write((byte)0); // colorId
                writer.Write((ushort)3); // length = 3 (WRONG! should be 4)

                // Tanks
                writer.Write((ushort)0); // tankCount

                // Lanes/slots
                writer.Write((byte)1);
                writer.Write((byte)5);

                byte[] badData = ms.ToArray();
                var target = ScriptableObject.CreateInstance<LevelData>();

                // Act & Assert
                Assert.Throws<InvalidDataException>(() => LevelSerializer.FromBytes(badData, target));
            }
        }

        [Test]
        public void GetCell_UsesBottomRowAsYZero()
        {
            // Arrange: rows[0]="1." (top), rows[1]="0." (bottom)
            var palette = new Color32[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255) };
            var level = TestLevels.Create(new[] { "1.", "0." }, palette, new ColorTankData[0]);

            // Act & Assert: y=0 should be the bottom row
            Assert.AreEqual(0, level.GetCell(0, 0), "GetCell(0,0) should be 0 (bottom-left)");
            Assert.AreEqual(1, level.GetCell(0, 1), "GetCell(0,1) should be 1 (top-left)");
            Assert.AreEqual(255, level.GetCell(1, 0), "GetCell(1,0) should be 255 (bottom-right, empty)");
        }

        [Test]
        public void ToBytes_AmmoAboveUShort_Throws()
        {
            // Arrange: tank with ammo > 65535
            var palette = new Color32[] { new Color32(255, 0, 0, 255) };
            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 0, ammo = 70000 } // exceeds ushort max
            };
            var level = TestLevels.Create(new[] { "0" }, palette, tanks);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => LevelSerializer.ToBytes(level));
        }

        [Test]
        public void FromJson_Malformed_Throws()
        {
            // Arrange: a valid JSON to derive the broken variants from
            var palette = new Color32[] { new Color32(255, 0, 0, 255) };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 1 } };
            var level = TestLevels.Create(new[] { "0" }, palette, tanks);
            string valid = LevelSerializer.ToJson(level);
            string paletteEntry = "\"#FF0000FF\"";
            StringAssert.Contains(paletteEntry, valid);

            string[] malformed =
            {
                null,                                              // no input -> null DTO
                "",                                                // no input -> null DTO
                "{ not json",                                      // JsonUtility parse error (ArgumentException)
                valid.Replace(paletteEntry, "\"#ZZ0000FF\""),      // bad hex digit (FormatException)
                valid.Replace(paletteEntry, "\"#F\""),             // short hex (ArgumentOutOfRangeException)
                valid.Replace("\"cellsRle\":\"", "\"cellsRle\":\"!!"), // bad Base64 (FormatException)
            };

            for (int i = 0; i < malformed.Length; i++)
            {
                var target = ScriptableObject.CreateInstance<LevelData>();
                var untouched = new Color32[0];
                target.palette = untouched;
                Assert.Throws<InvalidDataException>(() => LevelSerializer.FromJson(malformed[i], target),
                    $"Variant {i} did not throw InvalidDataException");
                Assert.AreSame(untouched, target.palette, $"Variant {i} partially populated the target");
                Assert.AreEqual(0, target.width, $"Variant {i} partially populated the target");
                UnityEngine.Object.DestroyImmediate(target);
            }

            UnityEngine.Object.DestroyImmediate(level);
        }
    }
}
