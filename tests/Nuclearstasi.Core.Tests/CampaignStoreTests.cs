using System;
using System.Collections.Generic;
using System.IO;
using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public sealed class CampaignStoreTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "nsTest_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        private static CampaignState Sample()
        {
            return new CampaignState
            {
                MissionName = "Nuclearstasi - Heartland",
                Status = CampaignStatus.Active,
                ElapsedSeconds = 3600.5,
                Aggression = 42,
                CapitalUnlocked = true,
                RebelTreasury = 123.25f,
                AirbaseOwners = new Dictionary<string, string> { ["base_a"] = "Rebels", ["base_b"] = "" },
                Warheads = new Dictionary<string, int> { ["base_c"] = 6 },
                Supplies = new Dictionary<string, Dictionary<string, int>>
                {
                    ["Occupiers"] = new Dictionary<string, int> { ["tank"] = 12 },
                },
            };
        }

        [Fact]
        public void LoadWithoutFileIsNotFound()
        {
            Assert.Equal(LoadOutcome.NotFound, new CampaignStore(dir).Load().Outcome);
        }

        [Fact]
        public void SaveThenLoadRoundTrips()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            LoadResult result = store.Load();
            Assert.Equal(LoadOutcome.Loaded, result.Outcome);
            CampaignState s = result.State;
            Assert.Equal("Nuclearstasi - Heartland", s.MissionName);
            Assert.Equal(3600.5, s.ElapsedSeconds);
            Assert.Equal(42, s.Aggression);
            Assert.True(s.CapitalUnlocked);
            Assert.Equal(123.25f, s.RebelTreasury);
            Assert.Equal("Rebels", s.AirbaseOwners["base_a"]);
            Assert.Equal("", s.AirbaseOwners["base_b"]);
            Assert.Equal(6, s.Warheads["base_c"]);
            Assert.Equal(12, s.Supplies["Occupiers"]["tank"]);
        }

        [Fact]
        public void StatusIsStoredAsText()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            Assert.Contains("\"Status\": \"Active\"", File.ReadAllText(store.SavePath));
        }

        [Fact]
        public void CorruptFileIsMovedAsideAndReported()
        {
            var store = new CampaignStore(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(store.SavePath, "{ not json");
            LoadResult result = store.Load();
            Assert.Equal(LoadOutcome.Corrupt, result.Outcome);
            Assert.NotNull(result.Error);
            Assert.False(File.Exists(store.SavePath));
            Assert.True(File.Exists(store.BadPath));
            Assert.Equal("{ not json", File.ReadAllText(store.BadPath));
        }

        [Fact]
        public void CorruptFileReportsMovedAside()
        {
            var store = new CampaignStore(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(store.SavePath, "{ not json");
            Assert.True(store.Load().MovedAside);
        }

        [Fact]
        public void NewerSaveVersionIsTreatedAsCorrupt()
        {
            var store = new CampaignStore(dir);
            CampaignState s = Sample();
            s.Version = CampaignState.CurrentVersion + 1;
            store.Save(s);
            Assert.Equal(LoadOutcome.Corrupt, store.Load().Outcome);
        }

        [Fact]
        public void MissingCollectionsAreFilledIn()
        {
            var store = new CampaignStore(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(store.SavePath, "{ \"Version\": 1, \"Aggression\": 5, \"AirbaseOwners\": null }");
            LoadResult result = store.Load();
            Assert.Equal(LoadOutcome.Loaded, result.Outcome);
            Assert.NotNull(result.State.AirbaseOwners);
            Assert.NotNull(result.State.Warheads);
            Assert.NotNull(result.State.Supplies);
        }

        [Fact]
        public void DeleteRemovesSave()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            store.Delete();
            Assert.Equal(LoadOutcome.NotFound, store.Load().Outcome);
        }

        [Fact]
        public void SaveOverwritesPreviousSave()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            CampaignState s = Sample();
            s.Aggression = 77;
            store.Save(s);
            Assert.Equal(77, store.Load().State.Aggression);
        }

        [Fact]
        public void SaveLeavesNoTempFile()
        {
            var store = new CampaignStore(dir);
            string tmp = Path.Combine(dir, "campaign.json.tmp");
            store.Save(Sample());
            Assert.False(File.Exists(tmp));
            store.Save(Sample());
            Assert.False(File.Exists(tmp));
        }

        [Fact]
        public void NullDocumentIsCorrupt()
        {
            var store = new CampaignStore(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(store.SavePath, "null");
            Assert.Equal(LoadOutcome.Corrupt, store.Load().Outcome);
        }

        [Fact]
        public void InnerNullsAreReplaced()
        {
            var store = new CampaignStore(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(store.SavePath, "{ \"Version\": 1, \"Supplies\": { \"Occupiers\": null }, \"AirbaseOwners\": { \"a\": null } }");
            LoadResult result = store.Load();
            Assert.Equal(LoadOutcome.Loaded, result.Outcome);
            Assert.Empty(result.State.Supplies["Occupiers"]);
            Assert.Equal("", result.State.AirbaseOwners["a"]);
        }
    }
}
