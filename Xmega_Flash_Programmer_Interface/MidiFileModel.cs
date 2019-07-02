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

        string FileName { get; set; }
        List<byte> FileBytes { get; set; }
    }
}
