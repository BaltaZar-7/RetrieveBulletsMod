using ModData;

namespace RetrieveBullets
{
    internal class SaveDataManager
    {
        private readonly ModDataManager modData = new ModDataManager(nameof(RetrieveBullets));

        public bool Save(string data, string suffix)
        {
            return modData.Save(data, suffix);
        }

        public string? Load(string suffix)
        {
            return modData.Load(suffix);
        }
    }
}
