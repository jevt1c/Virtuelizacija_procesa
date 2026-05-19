using System;
using System.IO;
using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class FileManipulationOptions : IDisposable
    {
        private bool _disposed = false;
        private MemoryStream _memoryStream;
        private string _keyWord;
        private string _fileName;

        public FileManipulationOptions(MemoryStream memoryStream, string keyWord, string fileName = "")
        {
            _memoryStream = memoryStream;
            _keyWord = keyWord;
            _fileName = fileName;
        }

        [DataMember] public MemoryStream MemoryStream { get => _memoryStream; set => _memoryStream = value; }
        [DataMember] public string KeyWord { get => _keyWord; set => _keyWord = value; }
        [DataMember] public string FileName { get => _fileName; set => _fileName = value; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _memoryStream?.Dispose();
                }
                _disposed = true;
            }
        }

        ~FileManipulationOptions()
        {
            Dispose(false);
        }
    }
}
