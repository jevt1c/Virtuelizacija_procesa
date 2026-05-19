using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace Common
{
    [ServiceContract]
    public interface ISmartGridService
    {
        [OperationContract]
        [FaultContract(typeof(ValidationFault))]
        [FaultContract(typeof(DataFormatFault))]
        SmartGridResponse StartSession(SessionMeta meta);

        [OperationContract]
        [FaultContract(typeof(ValidationFault))]
        [FaultContract(typeof(DataFormatFault))]
        SmartGridResponse PushSample(SmartGridSample sample);

        [OperationContract]
        SmartGridResponse EndSession();

        [OperationContract]
        [FaultContract(typeof(SmartGridException))]
        List<SmartGridSample> GetAllRecords();

        [OperationContract]
        [FaultContract(typeof(SmartGridException))]
        GridSummary GetSummary();

        [OperationContract]
        [FaultContract(typeof(SmartGridException))]
        FileManipulationResults GetFiles(FileManipulationOptions options);
    }


    public enum AckStatus  { ACK, NACK }
    public enum SessionStatus { IN_PROGRESS, COMPLETED }

    [DataContract]
    public class SessionMeta
    {
        [DataMember] public string SessionId { get; set; }
        [DataMember] public string SourceFile { get; set; }
        [DataMember] public int ExpectedRows { get; set; }
    }

    [DataContract]
    public class SmartGridResponse
    {
        [DataMember] public AckStatus Ack { get; set; }
        [DataMember] public SessionStatus Status { get; set; }
        [DataMember] public string Message { get; set; }
        [DataMember] public int ReceivedSamples { get; set; }
    }

    [DataContract]
    public class GridSummary
    {
        [DataMember] public int TotalRecords    { get; set; }
        [DataMember] public double AvgVoltage   { get; set; }
        [DataMember] public double AvgFrequency { get; set; }
        [DataMember] public double AvgPower     { get; set; }
        [DataMember] public int TotalFaults     { get; set; }
        [DataMember] public int TotalAnomalies  { get; set; }
    }
}
