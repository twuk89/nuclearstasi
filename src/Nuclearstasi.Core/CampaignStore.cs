using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Nuclearstasi.Core
{
    public enum LoadOutcome
    {
        NotFound,
        Loaded,
        Corrupt,
    }

    public sealed class LoadResult
    {
        public LoadOutcome Outcome;
        public CampaignState State;
        public string Error;
        /// <summary>For Corrupt: true when the bad file was moved to the bad-save path.</summary>
        public bool MovedAside;
    }

    public sealed class CampaignStore
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
        };

        private readonly string directory;

        public CampaignStore(string directory)
        {
            this.directory = directory;
        }

        public string SavePath => Path.Combine(directory, "campaign.json");

        public string BadPath => Path.Combine(directory, "campaign.bad.json");

        private string TempPath => Path.Combine(directory, "campaign.json.tmp");

        /// <summary>
        /// Writes the state atomically (temp file, then replace). Throws on IO failure;
        /// callers must catch and handle it.
        /// </summary>
        public void Save(CampaignState state)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(TempPath, JsonConvert.SerializeObject(state, JsonSettings));
            if (!File.Exists(SavePath))
            {
                File.Move(TempPath, SavePath);
                return;
            }
            try
            {
                File.Replace(TempPath, SavePath, null);
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException)
            {
                File.Copy(TempPath, SavePath, overwrite: true);
                File.Delete(TempPath);
            }
        }

        /// <summary>
        /// Loads the save. Returns NotFound when there is no file. IO errors while reading
        /// (e.g. a sharing violation) propagate as exceptions and leave the file untouched.
        /// Only unparseable JSON, an empty (null) document or a too-new version yields
        /// Corrupt, in which case the file is moved aside to <see cref="BadPath"/>.
        /// </summary>
        public LoadResult Load()
        {
            if (!File.Exists(SavePath))
            {
                return new LoadResult { Outcome = LoadOutcome.NotFound };
            }
            string text = File.ReadAllText(SavePath);
            CampaignState state;
            try
            {
                state = JsonConvert.DeserializeObject<CampaignState>(text, JsonSettings);
            }
            catch (JsonException e)
            {
                return Corrupt(e.Message);
            }
            if (state == null)
            {
                return Corrupt("save file is empty");
            }
            if (state.Version > CampaignState.CurrentVersion)
            {
                return Corrupt($"save version {state.Version} is newer than supported version {CampaignState.CurrentVersion}");
            }
            state.EnsureCollections();
            return new LoadResult { Outcome = LoadOutcome.Loaded, State = state };
        }

        public void Delete()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }

        private LoadResult Corrupt(string error)
        {
            bool moved = false;
            try
            {
                File.Copy(SavePath, BadPath, overwrite: true);
                File.Delete(SavePath);
                moved = true;
            }
            catch (Exception e)
            {
                error += "; could not move bad save: " + e.Message;
            }
            return new LoadResult { Outcome = LoadOutcome.Corrupt, Error = error, MovedAside = moved };
        }
    }
}
