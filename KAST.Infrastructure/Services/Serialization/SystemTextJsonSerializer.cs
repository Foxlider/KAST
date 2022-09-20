using KAST.Application.Common.Interfaces.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace KAST.Infrastructure.Services.Serialization
{
    internal sealed class SystemTextJsonSerializer : ISerializer
    {


        public string Serialize<T>(T value) where T : class => JsonSerializer.Serialize(value, DefaultJsonSerializerOptions.Options);

        public T? Deserialize<T>(string value) where T : class => JsonSerializer.Deserialize<T>(value, DefaultJsonSerializerOptions.Options);

        public byte[] SerializeBytes<T>(T value) where T : class => JsonSerializer.SerializeToUtf8Bytes(value, DefaultJsonSerializerOptions.Options);

        public T? DeserializeBytes<T>(byte[] value) where T : class => JsonSerializer.Deserialize<T>(value, DefaultJsonSerializerOptions.Options);
    }
}