using System.IO;
using System.Threading.Tasks;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public class JsonPersistence<T>
    {
        private readonly JsonPersistenceSettings settings;
        public JsonPersistence(JsonPersistenceSettings settings)
        {
            this.settings = settings;
        }

        public async Task SaveAsync(T dataObject)
        {
            System.Text.Json.JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(dataObject, options);
            await File.WriteAllBytesAsync(this.settings.PersistenceFilePath, bytes);
        }

        public async Task<T> LoadAsync()
        {
            if (!File.Exists(this.settings.PersistenceFilePath))
            {
                return default;
            }
            using var fileStream = File.OpenRead(this.settings.PersistenceFilePath);
            var dataObject = await System.Text.Json.JsonSerializer.DeserializeAsync<T>(fileStream);
            return dataObject;
        }
    }
}
