using NUnit.Framework;
using PixelFlow.Meta;

namespace PixelFlow.Tests
{
    [TestFixture]
    public class MetaTests
    {
        #region MemoryStore Tests

        [Test]
        public void MemoryStore_GetInt_ReturnsDefaultUntilSet()
        {
            var store = new MemoryStore();

            Assert.That(store.GetInt("a", 7), Is.EqualTo(7));
            store.SetInt("a", 3);
            Assert.That(store.GetInt("a", 7), Is.EqualTo(3));
            Assert.That(store.HasKey("a"), Is.True);
            Assert.That(store.HasKey("b"), Is.False);
        }

        #endregion

        #region PlayerProgress Tests

        [Test]
        public void Progress_Default_IsLevelOne()
        {
            var progress = new PlayerProgress(new MemoryStore());

            Assert.That(progress.LevelNumber, Is.EqualTo(1));
            Assert.That(progress.LevelIndex(6), Is.EqualTo(0));
        }

        [Test]
        public void Progress_CompleteCurrent_AdvancesAndPersists()
        {
            var store = new MemoryStore();
            var progress = new PlayerProgress(store);

            progress.CompleteLevel(1);
            progress.CompleteLevel(2);

            Assert.That(progress.LevelNumber, Is.EqualTo(3));
            Assert.That(new PlayerProgress(store).LevelNumber, Is.EqualTo(3));
        }

        [Test]
        public void Progress_CompleteOldLevel_DoesNotMoveBack()
        {
            var progress = new PlayerProgress(new MemoryStore());
            progress.CompleteLevel(4);

            progress.CompleteLevel(2);

            Assert.That(progress.LevelNumber, Is.EqualTo(5));
        }

        [Test]
        public void Progress_LevelIndex_WrapsAroundLevelCount()
        {
            var progress = new PlayerProgress(new MemoryStore());
            progress.CompleteLevel(6);

            Assert.That(progress.LevelNumber, Is.EqualTo(7));
            Assert.That(progress.LevelIndex(6), Is.EqualTo(0));
            Assert.That(PlayerProgress.IndexOf(12, 6), Is.EqualTo(5));
            Assert.That(PlayerProgress.IndexOf(13, 6), Is.EqualTo(0));
        }

        [Test]
        public void Progress_CorruptStoredValue_ClampsToOne()
        {
            var store = new MemoryStore();
            store.SetInt(PlayerProgress.LevelNumberKey, -5);

            Assert.That(new PlayerProgress(store).LevelNumber, Is.EqualTo(1));
        }

        #endregion

        #region GameSettings Tests

        [Test]
        public void Settings_Defaults_AreAllOn()
        {
            var settings = new GameSettings(new MemoryStore());

            Assert.That(settings.Music, Is.True);
            Assert.That(settings.Sfx, Is.True);
            Assert.That(settings.Vibration, Is.True);
        }

        [Test]
        public void Settings_Set_PersistsToStore()
        {
            var store = new MemoryStore();
            var settings = new GameSettings(store);

            settings.Music = false;
            settings.Sfx = false;
            settings.Vibration = false;

            var reloaded = new GameSettings(store);
            Assert.That(reloaded.Music, Is.False);
            Assert.That(reloaded.Sfx, Is.False);
            Assert.That(reloaded.Vibration, Is.False);
        }

        [Test]
        public void Settings_Changed_RaisedOnlyOnRealChange()
        {
            var settings = new GameSettings(new MemoryStore());
            int changed = 0;
            settings.Changed += () => changed++;

            settings.Music = true;
            Assert.That(changed, Is.EqualTo(0));

            settings.Music = false;
            settings.Vibration = false;
            Assert.That(changed, Is.EqualTo(2));
        }

        #endregion

        #region MetaServices Tests

        [Test]
        public void Services_Use_SwapsStoreForProgressAndSettings()
        {
            var store = new MemoryStore();
            store.SetInt(PlayerProgress.LevelNumberKey, 4);

            MetaServices.Use(store);
            try
            {
                Assert.That(MetaServices.Store, Is.SameAs(store));
                Assert.That(MetaServices.Progress.LevelNumber, Is.EqualTo(4));
                MetaServices.Settings.Sfx = false;
                Assert.That(store.GetInt(GameSettings.SfxKey, 1), Is.EqualTo(0));
            }
            finally
            {
                MetaServices.Use(new MemoryStore());
            }
        }

        #endregion
    }
}
