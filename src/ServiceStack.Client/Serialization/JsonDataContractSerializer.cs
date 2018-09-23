using System;
using System.IO;
using System.Runtime.Serialization;
using ServiceStack.Text;

namespace ServiceStack.Serialization
{
    public partial class JsonDataContractSerializer : IStringSerializer
    {
        public static JsonDataContractSerializer Instance = new JsonDataContractSerializer();

        public IStringSerializer TextSerializer { get; set; }

        public static void UseSerializer(IStringSerializer textSerializer)
        {
            Instance.TextSerializer = textSerializer;
        }

        public bool UseBcl { get; set; }

        public string SerializeToString<T>(T obj)
        {
            if (TextSerializer != null)
                return TextSerializer.SerializeToString(obj);

            if (!UseBcl)
                return JsonSerializer.SerializeToString(obj);

            if (obj == null) return null;
            var type = obj.GetType();
            try
            {
                using (var ms = MemoryStreamFactory.GetStream())
                {
                    var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(type);
                    serializer.WriteObject(ms, obj);
                    return ms.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                throw new SerializationException("JsonDataContractSerializer: Error converting type: " + ex.Message, ex);
            }
        }

        public void SerializeToStream<T>(T obj, Stream stream)
        {
            if (obj == null) return;

            var streamSerializer = TextSerializer as IStringStreamSerializer;
            if (TextSerializer != null)
            {
                streamSerializer?.SerializeToStream(obj, stream);
            }
            else if (UseBcl)
            {
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(obj.GetType());
                serializer.WriteObject(stream, obj);
            }
            else
            {
                JsonSerializer.SerializeToStream(obj, stream);
            }
        }
    }
}
