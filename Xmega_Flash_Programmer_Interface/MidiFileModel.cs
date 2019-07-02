using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xmega_Flash_Programmer_Interface
{
    public class MidiFileModel
    {
        public MidiFileModel()
        {
            FileBytes = new List<byte>();
        }

        public MidiFileModel(string fileName, IEnumerable<byte> fileBytes)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            if (fileBytes == null)
            {
                throw new ArgumentNullException(nameof(fileBytes));
            }

            FileName = fileName;
            FileBytes = new List<byte>(fileBytes);
        }


        public string FileName { get; set; }
        public List<byte> FileBytes { get; set; }
        public int ByteCount { get { return FileName.Length + FileBytes.Count; } }
    }
}
