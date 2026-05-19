using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace Common
{
    public enum ResultType
    {
        Success,
        Warning,
        Failed
    }

    [DataContract]
    public class FileManipulationResults : IDisposable
    {
        private bool _disposed = false;
        private ResultType _resultType;
        private string _resultMessage;
        private Dictionary<string, MemoryStream> _memoryStreamCollection;

        public FileManipulationResults(ResultType resultType, string resultMessage,
            Dictionary<string, MemoryStream> memoryStreamCollection = null)
        {
            _resultType = resultType;
            _resultMessage = resultMessage;
            _memoryStreamCollection = memoryStreamCollection ?? new Dictionary<string, MemoryStream>();
        }

        [DataMember] public ResultType ResultType { get => _resultType; set => _resultType = value; }
        [DataMember] public string ResultMessage { get => _resultMessage; set => _resultMessage = value; }
        [DataMember] public Dictionary<string, MemoryStream> MemoryStreamCollection
        {
            get => _memoryStreamCollection;
            set => _memoryStreamCollection = value;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _memoryStreamCollection != null)
                {
                    foreach (var ms in _memoryStreamCollection.Values)
                        ms?.Dispose();
                }
                _disposed = true;
            }
        }

        ~FileManipulationResults()
        {
            Dispose(false);
        }
    }
}
