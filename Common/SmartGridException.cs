using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class SmartGridException
    {
        public SmartGridException(string message) { Message = message; }
        [DataMember] public string Message { get; set; }
    }

    [DataContract]
    public class DataFormatFault
    {
        public DataFormatFault(string message, string field = "") { Message = message; Field = field; }
        [DataMember] public string Message { get; set; }
        [DataMember] public string Field   { get; set; }
    }

    [DataContract]
    public class ValidationFault
    {
        public ValidationFault(string message, string field = "") { Message = message; Field = field; }
        [DataMember] public string Message { get; set; }
        [DataMember] public string Field   { get; set; }
    }
}
