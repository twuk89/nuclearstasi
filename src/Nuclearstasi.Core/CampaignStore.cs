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

        public void Save(CampaignState state)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(TempPath, JsonConvert.SerializeObject(state, JsonSettings));
            File.Copy(TempPath, SavePath, overwrite: true);
            File.Delete(TempPath);
        }

        public LoadResult Load()
        {
            if (!File.Exists(SavePath))
            {
                return new LoadResult { Outcome = LoadOutcome.NotFound };
            }
            try
            {
                CampaignState state = JsonConvert.DeserializeObject<CampaignState>(File.ReadAllText(SavePath), JsonSettings);
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
            catch (Exception e)
            {
                return Corrupt(e.Message);
            }
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
            try
            {
                File.Copy(SavePath, BadPath, overwrite: true);
                File.Delete(SavePath);
            }
            catch (Exception e)
            {
                error += "; could not move bad save: " + e.Message;
            }
            return new LoadResult { Outcome = LoadOutcome.Corrupt, Error = error };
        }
    }
}
