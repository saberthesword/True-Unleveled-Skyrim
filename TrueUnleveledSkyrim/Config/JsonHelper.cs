using System.IO;

using Newtonsoft.Json;


namespace TrueUnleveledSkyrim.Config
{
    public static class JsonHelper
    {
        public static T LoadConfig<T>(string configPath) where T : ConfigType
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException("Config file not found: " + configPath, configPath);

            T? configObject;
            try
            {
                configObject = JsonConvert.DeserializeObject<T>(File.ReadAllText(configPath));
            }
            catch (JsonException ex) // Covers both malformed JSON and values that don't match the expected format.
            {
                throw new InvalidDataException("Incorrect config format for file: " + configPath + "\n" + ex.Message +
                    "\nMake sure to check and compare with the format in the original files provided in the patcher.", ex);
            }

            if (configObject is null)
                throw new InvalidDataException("Config file is empty: " + configPath);

            return configObject;
        }
    }
}
